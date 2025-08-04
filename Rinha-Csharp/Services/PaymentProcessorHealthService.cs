using System.Text.Json;
using StackExchange.Redis;
using Rinha_Csharp.Abstractions;
using Rinha_Csharp.Configuration;
using Rinha_Csharp.Models;
using Rinha_Csharp.Json;

namespace Rinha_Csharp.Services;

public class PaymentProcessorHealthService(
    HttpClient httpClient,
    IDatabase database,
    AppConfiguration config
    ) : IPaymentProcessorHealthService
{
    private readonly PaymentProcessorConfiguration _config = config.PaymentProcessor;
    private readonly Dictionary<ProcessorType, ProcessorHealthInfo> _localCache = new()
    {
        [ProcessorType.Default] = new(true, 50, DateTime.MinValue),
        [ProcessorType.Fallback] = new(true, 100, DateTime.MinValue)
    };
    private readonly SemaphoreSlim _healthCheckSemaphore = new(1, 1);

    public async Task<ProcessorHealthInfo> GetHealthInfoAsync(ProcessorType processorType)
    {
        var now = DateTimeOffset.UtcNow;
        var redisKey = $"health:{processorType}";
        
        var cachedData = await database.StringGetAsync(redisKey).ConfigureAwait(false);
        
        if (cachedData.HasValue)
        {
            var healthInfo = JsonSerializer.Deserialize(cachedData!, AppJsonSerializerContext.Default.ProcessorHealthInfo);
            // var healthInfo = JsonSerializer.Deserialize<ProcessorHealthInfo>(cachedData!);
            if (healthInfo != null && (now - healthInfo.LastChecked).TotalMilliseconds < _config.HealthCheckIntervalMs)
            {
                _localCache[processorType] = healthInfo;
                return healthInfo;
            }
        }

        if (_healthCheckSemaphore.Wait(0))
        {
            try
            {
                var healthInfo = await CheckProcessorHealthAsync(processorType).ConfigureAwait(false);
                
                var serializedHealth = JsonSerializer.Serialize(healthInfo, AppJsonSerializerContext.Default.ProcessorHealthInfo);
                // var serializedHealth = JsonSerializer.Serialize(healthInfo);
                await database.StringSetAsync(redisKey, serializedHealth, TimeSpan.FromSeconds(_config.HealthCheckIntervalMs / 1000 + 10)).ConfigureAwait(false);
                
                _localCache[processorType] = healthInfo;
                return healthInfo;
            }
            finally
            {
                _healthCheckSemaphore.Release();
            }
        }
        
        return _localCache[processorType];
    }

    private async Task<ProcessorHealthInfo> CheckProcessorHealthAsync(ProcessorType processorType)
    {
        var url = processorType == ProcessorType.Default
            ? _config.DefaultUrl
            : _config.FallbackUrl;

        var response = await httpClient.GetAsync($"{url}/payments/service-health").ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return new ProcessorHealthInfo(false, 10000, DateTimeOffset.UtcNow);

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var healthResponse = JsonSerializer.Deserialize(content, AppJsonSerializerContext.Default.ProcessorHealthResponse);
        // var healthResponse = JsonSerializer.Deserialize<ProcessorHealthResponse>(content);

        if (healthResponse == null)
            return new ProcessorHealthInfo(false, 10000, DateTimeOffset.UtcNow);

        var isHealthy = !healthResponse.Failing;

        return new ProcessorHealthInfo(isHealthy, healthResponse.MinResponseTime, DateTimeOffset.UtcNow);
    }

    public ProcessorType GetBestProcessor()
    {
        var defaultHealth = _localCache[ProcessorType.Default];
        var fallbackHealth = _localCache[ProcessorType.Fallback];
        
        if (!defaultHealth.IsHealthy)
            return ProcessorType.Fallback;

        if (!fallbackHealth.IsHealthy)
            return ProcessorType.Default;
            
        return defaultHealth.MinResponseTime <= fallbackHealth.MinResponseTime 
            ? ProcessorType.Default 
            : ProcessorType.Fallback;
    }

    public void UpdateHealthAsync(ProcessorType processorType)
    {
        try
        {
            _ = Task.Run(async () =>
            {
                await GetHealthInfoAsync(processorType).ConfigureAwait(false);
            });
        }
        finally
        {
            Console.WriteLine("Health updated");
        }
    }
}