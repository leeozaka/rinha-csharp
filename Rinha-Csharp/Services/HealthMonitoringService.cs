using Rinha_Csharp.Abstractions;
using Rinha_Csharp.Models;
using Rinha_Csharp.Configuration;
using StackExchange.Redis;
using System.Text.Json;

namespace Rinha_Csharp.Services;

public class HealthMonitoringService(
    IPaymentProcessorHealthService healthService,
    IDatabase database,
    AppConfiguration config,
    ILogger<HealthMonitoringService> logger
    ) : BackgroundService
{
    private readonly PaymentProcessorConfiguration _config = config.PaymentProcessor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WarmupAsync(stoppingToken).ConfigureAwait(false);
        
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_config.HealthCheckIntervalMs));
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
                
                var tasks = new[]
                {
                    UpdateProcessorHealthAsync(ProcessorType.Default),
                    UpdateProcessorHealthAsync(ProcessorType.Fallback)
                };
                
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task WarmupAsync(CancellationToken cancellationToken)
    {
        
        try
        {
            var warmupTasks = new[]
            {
                WarmupRedisAsync(),
                WarmupProcessorHealthAsync(ProcessorType.Default),
                WarmupProcessorHealthAsync(ProcessorType.Fallback)
            };
            
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            
            await Task.WhenAll(warmupTasks).WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            Console.WriteLine("Warmup complete");
            
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Warmup completed with some errors, continuing startup");
        }
    }

    private async Task WarmupRedisAsync()
    {
        try
        {
            await database.PingAsync().ConfigureAwait(false);
            await database.StringSetAsync("warmup:test", "ok", TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            await database.StringGetAsync("warmup:test").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis warmup failed");
            throw;
        }
    }

    private async Task WarmupProcessorHealthAsync(ProcessorType processorType)
    {
        try
        {
            await healthService.GetHealthInfoAsync(processorType).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Processor {ProcessorType} health warmup failed", processorType);
        }
    }

    private async Task UpdateProcessorHealthAsync(ProcessorType processorType)
    {
        try
        {
            await healthService.GetHealthInfoAsync(processorType).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update health for processor {ProcessorType}", processorType);
        }
    }
}