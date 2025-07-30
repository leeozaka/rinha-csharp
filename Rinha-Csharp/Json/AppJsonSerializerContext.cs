using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Rinha_Csharp.Models;

namespace Rinha_Csharp.Json;

[JsonSerializable(typeof(PaymentRequestDto))]
[JsonSerializable(typeof(PaymentRequest))]
[JsonSerializable(typeof(PaymentProcessorRequest))]
[JsonSerializable(typeof(ProcessedPayment))]
[JsonSerializable(typeof(PaymentSummaryResponse))]
[JsonSerializable(typeof(ProcessorSummary))]
[JsonSerializable(typeof(HealthStatus))]
[JsonSerializable(typeof(ProcessorHealthResponse))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
