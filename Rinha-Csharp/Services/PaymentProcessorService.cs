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

        // var json = JsonSerializer.Serialize(processorRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync($"{url}/payments", content).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            try
            {
                await SaveAsync(payment, nameof(processorType)).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            healthService.UpdateHealthAsync(processorType);
            return false;
        }
    }

    private async Task SaveAsync(PaymentRequest payment, string processorType)
    {
        var processingKey = $"processed:{payment.Id}";
        
        var wasSet = await database.StringSetAsync(processingKey, processorType, TimeSpan.FromHours(24), When.NotExists).ConfigureAwait(false);
        if (!wasSet) return;
        
        var amountCents = ConvertToAmountCents(payment.Amount);
        
        var paymentStorage = new PaymentStorageData(
            payment.Id,
            amountCents,
            payment.CreatedAt,
            processorType
        );

        var transaction = database.CreateTransaction();
        
        _ = transaction.ListLeftPushAsync($"payments:{processorType}:data", 
            JsonSerializer.Serialize(paymentStorage, AppJsonSerializerContext.Default.PaymentStorageData));
            // JsonSerializer.Serialize(paymentStorage));

        _ = transaction.StringIncrementAsync($"summary:{processorType}:total_amount_cents", amountCents, CommandFlags.FireAndForget);
        _ = transaction.StringIncrementAsync($"summary:{processorType}:count", 1, CommandFlags.FireAndForget);
            
        await transaction.ExecuteAsync().ConfigureAwait(false);
    }

    private static long ConvertToAmountCents(decimal amount) => (long)Math.Round(amount * 100, MidpointRounding.ToEven);

    public async Task<PaymentSummaryResponse> GetPaymentsSummaryAsync(DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var (defaultSummary, fallbackSummary) = await GetPaymentsInfoAsync(from, to).ConfigureAwait(false);
        
        return new PaymentSummaryResponse(defaultSummary, fallbackSummary);
    }

    private async Task<(ProcessorSummary Default, ProcessorSummary Fallback)> GetPaymentsInfoAsync(DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        if (from.HasValue || to.HasValue)
        {
            return await GetPaymentsInfoWithDateFilterAsync(from, to).ConfigureAwait(false);
        }
        
        var tasks = Task.WhenAll(
            GetProcessorSummaryFromCountersAsync("Default"),
            GetProcessorSummaryFromCountersAsync("Fallback")
        );
        
        var results = await tasks.ConfigureAwait(false);
        
        return (results[0], results[1]);
    }

    private async Task<ProcessorSummary> GetProcessorSummaryFromCountersAsync(string processorType)
    {
        var tasks = Task.WhenAll(
            database.StringGetAsync($"summary:{processorType}:total_amount_cents"),
            database.StringGetAsync($"summary:{processorType}:count")
        );
        
        var results = await tasks.ConfigureAwait(false);
        
        var totalAmountCents = results[0].HasValue ? (long)results[0] : 0L;
        var count = results[1].HasValue ? (int)results[1] : 0;
        
        var totalAmount = totalAmountCents / 100m;
        
        return new ProcessorSummary(count, totalAmount);
    }

    private async Task<(ProcessorSummary Default, ProcessorSummary Fallback)> GetPaymentsInfoWithDateFilterAsync(DateTimeOffset? from, DateTimeOffset? to)
    {
        var paymentTasks = Task.WhenAll(
            database.ListRangeAsync("payments:Default:data"),
            database.ListRangeAsync("payments:Fallback:data")
        );
        
        var paymentLists = await paymentTasks.ConfigureAwait(false);
        
        int defaultCount = 0, fallbackCount = 0;
        long defaultAmountCents = 0, fallbackAmountCents = 0;
        
        for (int i = 0; i < paymentLists.Length; i++)
        {
            var processorType = i == 0 ? "Default" : "Fallback";
            
            foreach (var paymentData in paymentLists[i])
            {
                if (paymentData.HasValue)
                {
                    // var storageData = JsonSerializer.Deserialize(paymentData!, AppJsonSerializerContext.Default.PaymentStorageData);
                    var storageData = JsonSerializer.Deserialize<PaymentStorageData>(paymentData!);
                    
                    if (storageData != null)
                    {
                        var createdAt = storageData.CreatedAt;
                        
                        if ((from == null || createdAt >= from) && (to == null || createdAt <= to))
                        {
                            if (processorType == "Default")
                            {
                                defaultCount++;
                                defaultAmountCents += storageData.AmountCents;
                            }
                            else
                            {
                                fallbackCount++;
                                fallbackAmountCents += storageData.AmountCents;
                            }
                        }
                    }
                }
            }
        }
        
        var defaultAmount = defaultAmountCents / 100m;
        var fallbackAmount = fallbackAmountCents / 100m;
        
        return (
            new ProcessorSummary(defaultCount, defaultAmount),
            new ProcessorSummary(fallbackCount, fallbackAmount)
        );
    }
}