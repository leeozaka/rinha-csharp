using Rinha_Csharp.Models;

namespace Rinha_Csharp.Abstractions;

public interface IPaymentProcessorHealthService
{
    Task<ProcessorHealthInfo> GetHealthInfoAsync(ProcessorType processorType);
    ProcessorType GetBestProcessor();
}