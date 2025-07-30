using Rinha_Csharp.Abstractions;
using Rinha_Csharp.Models;
using System.Runtime.CompilerServices;

namespace Rinha_Csharp.Services;

public class InMemoryPaymentQueue : IInMemoryPaymentQueue
{
    private readonly Queue<PaymentRequest> _queue = new();

    public ValueTask<bool> EnqueueAsync(PaymentRequest payment)
    {
        if (payment != null){
            _queue.Enqueue(payment);
            return ValueTask.FromResult(true);
        }
        
        return ValueTask.FromResult(false);
    }

    public async IAsyncEnumerable<PaymentRequest> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_queue.TryDequeue(out var payment) && payment != null)
                yield return payment;
            else
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }

    public int Count => _queue.Count;
}