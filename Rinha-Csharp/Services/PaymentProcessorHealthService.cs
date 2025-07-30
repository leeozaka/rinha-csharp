using System.Text.Json;
using Rinha_Csharp.Abstractions;
using Rinha_Csharp.Configuration;
using Rinha_Csharp.Models;
using Rinha_Csharp.Json;

namespace Rinha_Csharp.Services;

public class PaymentProcessorHealthService(
    HttpClient httpClient,
    AppConfiguration config
    ) : IPaymentProcessorHealthService
{
    private readonly PaymentProcessorConfiguration _config = config.PaymentProcessor;
    private readonly Dictionary<ProcessorType, ProcessorHealthInfo> _healthCache = new()
    {
        [ProcessorType.Default] = new(true, 50, DateTime.MinValue),
        [ProcessorType.Fallback] = new(true, 100, DateTime.MinValue)
    };

    public async Task<ProcessorHealthInfo> GetHealthInfoAsync(ProcessorType processorType)
    {
        var now = DateTime.UtcNow;
        var cached = _healthCache[processorType];
        
        if ((now - cached.LastChecked).TotalMilliseconds < _config.HealthCheckIntervalMs)
        {
            return cached;
        }

        var healthInfo = await CheckProcessorHealthAsync(processorType).ConfigureAwait(false);
        _healthCache[processorType] = healthInfo;
        
        return healthInfo;
    }

    private async Task<ProcessorHealthInfo> CheckProcessorHealthAsync(ProcessorType processorType)
    {
        var url = processorType == ProcessorType.Default
            ? _config.DefaultUrl
            : _config.FallbackUrl;

        var response = await httpClient.GetAsync($"{url}/payments/service-health").ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return new ProcessorHealthInfo(false, 10000, DateTime.UtcNow);

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var healthResponse = JsonSerializer.Deserialize(content, AppJsonSerializerContext.Default.ProcessorHealthResponse);

        if (healthResponse == null)
            return new ProcessorHealthInfo(false, 10000, DateTime.UtcNow);

        var isHealthy = !healthResponse.Failing;

        return new ProcessorHealthInfo(isHealthy, healthResponse.MinResponseTime, DateTime.UtcNow);
    }

    public ProcessorType GetBestProcessor() =>
        _healthCache[ProcessorType.Default].IsHealthy ? ProcessorType.Default : ProcessorType.Fallback;
}