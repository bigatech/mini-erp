using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Emit structured JSON logs to stdout/stderr for local and container collection.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.UseUtcTimestamp = true;
});

// Placeholder OpenTelemetry pipeline. Instrumentation and exporters are configured
// when their repository-level choices are implemented; telemetry remains best-effort.
builder.AddOpenTelemetry()
    .WithMetrics(_ => { })
    .WithTracing(_ => { });

builder.Services.AddSingleton<WorkerLiveness>();
builder.Services.AddHostedService<WorkerHostService>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MiniErp.Worker");
logger.LogInformation("MiniErp.Worker host starting in {EnvironmentName} environment", builder.Environment.EnvironmentName);

await host.RunAsync();

internal sealed class WorkerLiveness
{
    private long _lastHeartbeatUtcTicks;

    public DateTimeOffset? LastHeartbeatUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastHeartbeatUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public bool IsAlive => LastHeartbeatUtc is not null;

    public void Beat() => Interlocked.Exchange(ref _lastHeartbeatUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
}

internal sealed class WorkerHostService(WorkerLiveness liveness, ILogger<WorkerHostService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker host loop started");

        while (!stoppingToken.IsCancellationRequested)
        {
            liveness.Beat();
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
