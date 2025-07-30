using System.Text.Json;
using StackExchange.Redis;
using Rinha_Csharp.Services;
using Rinha_Csharp.Abstractions;
using Rinha_Csharp.Models;
using Rinha_Csharp.Json;
using Rinha_Csharp.Configuration;

var builder = WebApplication.CreateSlimBuilder(args);

var appConfig = new AppConfiguration();
builder.Configuration.Bind(appConfig);

builder.Services.Configure<AppConfiguration>(builder.Configuration);
builder.Services.Configure<RedisConfiguration>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<PaymentProcessorConfiguration>(builder.Configuration.GetSection("PaymentProcessor"));
builder.Services.Configure<QueueConfiguration>(builder.Configuration.GetSection("Queue"));

builder.Services.AddSingleton(appConfig);

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = sp.GetRequiredService<AppConfiguration>();
    
    var connectionString = !string.IsNullOrEmpty(config.Redis.ConnectionString) 
        ? config.Redis.ConnectionString 
        : builder.Configuration.GetConnectionString("Redis") ?? "redis:6379";
    
    var configurationOptions = ConfigurationOptions.Parse(connectionString);
    configurationOptions.AbortOnConnectFail = false;
    configurationOptions.ConnectRetry = 3;
    configurationOptions.ConnectTimeout = 1000;
    configurationOptions.SyncTimeout = 500;
    configurationOptions.AsyncTimeout = 1000;
    configurationOptions.KeepAlive = 60;
    configurationOptions.CommandMap = CommandMap.Create(
    [
        "INFO", "CONFIG", "CLUSTER", "PING", "ECHO", "CLIENT"
    ], available: false);
    
    try
    {
        var multiplexer = ConnectionMultiplexer.Connect(configurationOptions);
        return multiplexer;
    }
    catch (Exception)
    {
        throw;
    }
});

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase()
);

builder.Services.AddSingleton<IInMemoryPaymentQueue, InMemoryPaymentQueue>();

builder.Services.AddHttpClient<PaymentProcessorService>((sp, client) =>
{
    var config = sp.GetRequiredService<AppConfiguration>();
    
    var timeoutMs = config.PaymentProcessor.RequestTimeoutMs > 0 ? config.PaymentProcessor.RequestTimeoutMs : 2000;
    
    client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
    client.DefaultRequestHeaders.Add("User-Agent", "Rinha-Backend-2025");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    MaxConnectionsPerServer = 20,
    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
    EnableMultipleHttp2Connections = true
});

builder.Services.AddHttpClient<PaymentProcessorHealthService>((sp, client) =>
{
    var config = sp.GetRequiredService<AppConfiguration>();
    
    var timeoutMs = config.PaymentProcessor.HealthCheckTimeoutMs > 0 ? config.PaymentProcessor.HealthCheckTimeoutMs + 500 : 1500;
    
    client.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
    client.DefaultRequestHeaders.Add("User-Agent", "Rinha-Backend-Health");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    MaxConnectionsPerServer = 20,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    EnableMultipleHttp2Connections = true
});

builder.Services.AddSingleton<IPaymentProcessorHealthService, PaymentProcessorHealthService>();
builder.Services.AddSingleton<IPaymentProcessorService, PaymentProcessorService>();
builder.Services.AddHostedService<QueueProcessorService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.DefaultBufferSize = 32768;
    options.SerializerOptions.TypeInfoResolver = AppJsonSerializerContext.Default;
});

var app = builder.Build();

app.MapPost("/payments", async (PaymentRequestDto request, IInMemoryPaymentQueue paymentQueue) =>
{
    try
    {
        var payment = new PaymentRequest(
            request.CorrelationId,
            request.Amount,
            DateTime.UtcNow
        );
        
        var accepted = await paymentQueue.EnqueueAsync(payment).ConfigureAwait(false);
        
        if (!accepted)
        {
            return Results.Problem("Payment rejected by queue", statusCode: 503);
        }
        
        return Results.Accepted();
    }
    catch (Exception)
    {
        return Results.InternalServerError();
    }
});

app.MapGet("/payments-summary", async (IPaymentProcessorService paymentProcessorService, DateTime? from, DateTime? to) =>
{
    try
    {
        var summary = await paymentProcessorService.GetPaymentsSummaryAsync(from, to).ConfigureAwait(false);
        return Results.Ok(summary);
    }
    catch (Exception)
    {
        return Results.InternalServerError();
    }
});

app.MapGet("/health", () => 
{
    return Results.Ok(new HealthStatus("healthy", "connected"));
});

app.Run();
