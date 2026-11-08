using DataIntakeAnalytics.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataIntakeAnalytics.Composition;

/// <summary>Prepare versioned read models without waiting for the first visitor.</summary>
internal sealed class DashboardWarmupService(IServiceScopeFactory scopes, ILogger<DashboardWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PostgresExecutiveDashboard>().GetAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                // Never publish stale data on failure; normal endpoints keep their
                // existing version checks and the next tick retries preparation.
                logger.LogWarning("Dashboard preparation failed ({ErrorType}); retrying on next tick.", error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
