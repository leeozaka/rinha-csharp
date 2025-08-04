using System.Text.Json.Serialization;

namespace Rinha_Csharp.Models;

public record PaymentRequest(
    [property: JsonPropertyName("correlationId")] string Id,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt
);

public record PaymentProcessorRequest(
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("default")] bool Default
);

public record ProcessedPayment(
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("cents")] int Cents,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("processorType")] string ProcessorType
);

public record PaymentSummaryResponse(
    [property: JsonPropertyName("default")] ProcessorSummary Default,
    [property: JsonPropertyName("fallback")] ProcessorSummary Fallback
);

public record ProcessorSummary(
    [property: JsonPropertyName("totalRequests")] int TotalRequests,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount
);

public record HealthStatus(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("database")] string Database
);

public record ProcessorHealthResponse(
    [property: JsonPropertyName("failing")] bool Failing,
    [property: JsonPropertyName("minResponseTime")] int MinResponseTime
);

public record ProcessorHealthInfo(
    bool IsHealthy,
    int MinResponseTime,
    DateTimeOffset LastChecked
);

public record PaymentRequestDto(
    string CorrelationId,
    decimal Amount
);

public record PaymentStorageData(
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("amountCents")] long AmountCents,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("processorType")] string ProcessorType
);

public record PaymentSummaryData(
    long TotalAmountCents,
    int TotalCount,
    DateTimeOffset LastUpdated
);

public enum ProcessorType
{
    Default,
    Fallback
} 