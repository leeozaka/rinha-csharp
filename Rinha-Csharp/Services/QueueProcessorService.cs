using Rinha_Csharp.Abstractions;

namespace Rinha_Csharp.Services;

public class QueueProcessor(
    IInMemoryPaymentQueue paymentQueue,
    IPaymentProcessorService paymentProcessor
    ) : BackgroundService
{
    private readonly int _consumerCount = Environment.ProcessorCount;
    private Task[]? _consumerTasks;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _consumerTasks = new Task[_consumerCount];
            
            for (int i = 0; i < _consumerCount; i++)
            {
                _consumerTasks[i] = Task.Run(async () =>
                {
                    await foreach (var payment in paymentQueue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
                    {
                        await paymentProcessor.ProcessPaymentAsync(payment).ConfigureAwait(false);
                    }
                }, stoppingToken);
            }

            await Task.WhenAll(_consumerTasks).ConfigureAwait(false);
        }
        finally
        {
            if (_consumerTasks != null)
            {
                await Task.WhenAll(_consumerTasks.Where(t => t.IsCompletedSuccessfully)).ConfigureAwait(false);
            }
        }
    }

    public override void Dispose() => GC.SuppressFinalize(this);
}
