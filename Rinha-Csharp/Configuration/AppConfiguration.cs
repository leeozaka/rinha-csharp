namespace Rinha_Csharp.Configuration;

public class AppConfiguration
{
    public RedisConfiguration Redis { get; set; } = new();
    public PaymentProcessorConfiguration PaymentProcessor { get; set; } = new();
    public QueueConfiguration Queue { get; set; } = new();
}

public class RedisConfiguration
{
    public string ConnectionString { get; set; } = string.Empty;
}

public class PaymentProcessorConfiguration
{
    public string DefaultUrl { get; set; } = string.Empty;
    public string FallbackUrl { get; set; } = string.Empty;
    public int MaxRetries { get; set; }
    public int RequestTimeoutMs { get; set; }
    public int HealthCheckIntervalMs { get; set; }
    public int HealthCheckTimeoutMs { get; set; }
    public int Workers { get; set; }
}

public class QueueConfiguration
{
    public int Capacity { get; set; }
}