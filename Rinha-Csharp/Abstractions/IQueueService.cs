namespace Rinha_Csharp.Abstractions;

public interface IInMemoryPaymentQueue
{
    ValueTask<bool> EnqueueAsync(Models.PaymentRequest payment);
    IAsyncEnumerable<Models.PaymentRequest> ReadAllAsync(CancellationToken cancellationToken);
    int Count { get; }
}
