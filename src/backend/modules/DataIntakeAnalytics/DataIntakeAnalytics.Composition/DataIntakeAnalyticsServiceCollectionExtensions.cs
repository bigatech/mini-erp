using DataIntakeAnalytics.Api;
using DataIntakeAnalytics.Application;
using DataIntakeAnalytics.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DataIntakeAnalytics.Composition;

public static class DataIntakeAnalyticsServiceCollectionExtensions
{
    public static IServiceCollection AddDataIntakeAnalytics(this IServiceCollection services, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString)) services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<ISpreadsheetReader, SpreadsheetReader>();
        services.AddSingleton<IPdfReader, PdfReader>();
        services.AddScoped<PostgresStagingRepository>();
        services.AddScoped<PostgresExecutiveDashboard>();
        services.AddScoped<PostgresProcessDashboardCache>();
        services.AddScoped<IProcessDashboardCache>(sp => sp.GetRequiredService<PostgresProcessDashboardCache>());
        services.AddScoped<PostgresAnalyticsWorksheet>();
        services.AddScoped<IAnalyticsWorksheetStore>(sp => sp.GetRequiredService<PostgresAnalyticsWorksheet>());
        services.AddScoped<IStagingRepository>(sp => sp.GetRequiredService<PostgresStagingRepository>());
        services.AddScoped<GetProcessDashboardHandler>(); services.AddScoped<ImportFileHandler>(); services.AddScoped<ListStagedFilesHandler>(); services.AddScoped<GetStagedFileHandler>(); services.AddScoped<GetDashboardHandler>();
        services.AddScoped<GetDashboardValidationHandler>();
        services.AddHostedService<DashboardWarmupService>();
        return services;
    }

    public static IApplicationBuilder UseDataIntakeAnalytics(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<PostgresStagingRepository>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        scope.ServiceProvider.GetRequiredService<PostgresExecutiveDashboard>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        scope.ServiceProvider.GetRequiredService<PostgresProcessDashboardCache>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        scope.ServiceProvider.GetRequiredService<PostgresAnalyticsWorksheet>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return app;
    }

    public static IEndpointRouteBuilder MapDataIntakeAnalytics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/data-intake/dashboards/executive-snapshot", async (PostgresExecutiveDashboard dashboard, CancellationToken ct) =>
        {
            try { return Results.Ok(await dashboard.GetAsync(ct)); }
            catch (ExecutiveSourcesChangedException) { return Results.Problem(statusCode: 503, title: "Kaynaklar güncellendi. Gösterge panelini yeniden yükleyin."); }
        }).WithTags("DataIntakeAnalytics");
        endpoints.MapAnalyticsWorksheetEndpoints();
        return endpoints.MapDataIntakeEndpoints();
    }
}
