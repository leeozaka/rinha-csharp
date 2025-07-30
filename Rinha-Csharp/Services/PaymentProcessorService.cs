using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using Rinha_Csharp.Abstractions;
using Rinha_Csharp.Models;
using Rinha_Csharp.Json;
using Rinha_Csharp.Configuration;

namespace Rinha_Csharp.Services;

public class PaymentProcessorService(
    HttpClient httpClient,
    IInMemoryPaymentQueue paymentQueue,
    IDatabase database,
    IPaymentProcessorHealthService healthService,
    AppConfiguration config
    ) : IPaymentProcessorService
{
    private readonly PaymentProcessorConfiguration _config = config.PaymentProcessor;

    public async Task<bool> ProcessPaymentAsync(PaymentRequest payment)
    {
        var bestProcessor = healthService.GetBestProcessor();
        
        if (await TryProcessWithProcessor(payment, bestProcessor).ConfigureAwait(false))
        {
            return true;
        }

        var fallbackProcessor = bestProcessor == ProcessorType.Default 
            ? ProcessorType.Fallback 
            : ProcessorType.Default;
            
        if (await TryProcessWithProcessor(payment, fallbackProcessor).ConfigureAwait(false))
        {
            return true;
        }

        for (int i = 0; i < _config.MaxRetries - 1; i++)
        {
            if (await TryProcessWithProcessor(payment, bestProcessor).ConfigureAwait(false))
            {
                return true;
            }
        }
        
        await paymentQueue.EnqueueAsync(payment).ConfigureAwait(false);
        return false;
    }

    private async Task<bool> TryProcessWithProcessor(PaymentRequest payment, ProcessorType processorType)
    {
        var url = processorType == ProcessorType.Default 
            ? _config.DefaultUrl 
            : _config.FallbackUrl;

        var processorRequest = new PaymentProcessorRequest(
            payment.Id,
            payment.Amount,
            payment.CreatedAt,
            processorType == ProcessorType.Default
        );

        var json = JsonSerializer.Serialize(processorRequest, AppJsonSerializerContext.Default.PaymentProcessorRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync($"{url}/payments", content).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            await SaveAsync(payment, processorType.ToString().ToLower()).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async Task SaveAsync(PaymentRequest payment, string processorType)
    {
        var processedPayment = new ProcessedPayment(
            payment.Id,
            payment.Amount,
            payment.CreatedAt,
            processorType
        );

        var transaction = database.CreateTransaction();
        
        _ = transaction.ListLeftPushAsync($"processed-{processorType}", 
            JsonSerializer.Serialize(processedPayment, AppJsonSerializerContext.Default.ProcessedPayment));
        
        _ = transaction.StringIncrementAsync($"summary:{processorType}:count", 1);
        _ = transaction.StringIncrementAsync($"summary:{processorType}:amount_cents", (long)(payment.Amount * 100));
            
        await transaction.ExecuteAsync().ConfigureAwait(false);
    }

    public async Task<PaymentSummaryResponse> GetPaymentsSummaryAsync(DateTime? from = null, DateTime? to = null)
    {
        var summaryTasks = Task.WhenAll(
            GetProcessorSummary("default", from, to),
            GetProcessorSummary("fallback", from, to)
        );
        
        var summaries = await summaryTasks.ConfigureAwait(false);

        var defaultSummary = summaries[0];
        var fallbackSummary = summaries[1];
        
        return new PaymentSummaryResponse(defaultSummary, fallbackSummary);
    }

    private async Task<ProcessorSummary> GetProcessorSummary(string processorType, DateTime? from = null, DateTime? to = null)
    {
        if (from == null && to == null)
        {
            var countTask = database.StringGetAsync($"summary:{processorType}:count");
            var amountCentsTask = database.StringGetAsync($"summary:{processorType}:amount_cents");
            
            await Task.WhenAll(countTask, amountCentsTask).ConfigureAwait(false);
            
            var totalRequests = countTask.Result.HasValue ? (int)countTask.Result : 0;
            var totalAmountCents = amountCentsTask.Result.HasValue ? (long)amountCentsTask.Result : 0L;
            var totalAmount = totalAmountCents / 100m;
            
            return new ProcessorSummary(totalRequests, totalAmount);
        }

        var processedPayments = await database.ListRangeAsync($"processed-{processorType}").ConfigureAwait(false);
        
        int filteredCount = 0;
        decimal filteredAmount = 0m;
        
        foreach (var paymentData in processedPayments)
        {
            if (paymentData.HasValue)
            {
                var processedPayment = JsonSerializer.Deserialize(paymentData!, AppJsonSerializerContext.Default.ProcessedPayment);
                
                if (processedPayment != null)
                {
                    var createdAt = processedPayment.CreatedAt;
                    
                    if ((from == null || createdAt >= from) && (to == null || createdAt <= to))
                    {
                        filteredCount++;
                        filteredAmount += processedPayment.Amount;
                    }
                }
            }
        }
        
        return new ProcessorSummary(filteredCount, filteredAmount);
    }
}