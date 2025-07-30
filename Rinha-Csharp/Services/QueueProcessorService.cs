using Rinha_Csharp.Abstractions;
using Rinha_Csharp.Models;
using System.Collections.Concurrent;

namespace Rinha_Csharp.Services;

public class QueueProcessorService(
    IInMemoryPaymentQueue paymentQueue,
    IPaymentProcessorService paymentProcessor
    ) : BackgroundService
{
    private readonly ConcurrentQueue<PaymentRequest> _processingQueue = new();
    private readonly int _consumerCount = Environment.ProcessorCount * 2;
    private Task[]? _consumerTasks;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        try
        {
            _consumerTasks = new Task[_consumerCount];
            for (int i = 0; i < _consumerCount; i++)
            {
                var consumerId = i;
                _consumerTasks[i] = Task.Run(() => ProcessorConsumer(consumerId, stoppingToken), stoppingToken);
            }

            await foreach (var payment in paymentQueue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                _processingQueue.Enqueue(payment);
            }
        }
        finally
        {
            if (_consumerTasks != null)
            {
                await Task.WhenAll(_consumerTasks).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessorConsumer(int consumerId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_processingQueue.TryDequeue(out var payment))
            {
                await ProcessPayment(payment).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessPayment(PaymentRequest payment) =>
        await paymentProcessor.ProcessPaymentAsync(payment).ConfigureAwait(false);

    public override void Dispose() => GC.SuppressFinalize(this);
}
