using Rinha_Csharp.Models;

namespace Rinha_Csharp.Abstractions;

public interface IPaymentProcessorService
{
    Task<bool> ProcessPaymentAsync(PaymentRequest payment);
    Task<PaymentSummaryResponse> GetPaymentsSummaryAsync(DateTimeOffset? from = null, DateTimeOffset? to = null);
} 