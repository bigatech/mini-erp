using Cutting.Api;
using Cutting.Application;
using Cutting.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
namespace Cutting.Composition;
public static class CuttingExtensions
{
    public static IServiceCollection AddCutting(this IServiceCollection services) { services.AddScoped<PostgresCuttingStore>(); services.AddScoped<ICuttingStore>(p=>p.GetRequiredService<PostgresCuttingStore>()); return services; }
    public static IApplicationBuilder UseCutting(this IApplicationBuilder app)
    {
        using var scope=app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<PostgresCuttingStore>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return app;
    }
    public static IEndpointRouteBuilder MapCutting(this IEndpointRouteBuilder endpoints) => endpoints.MapCuttingEndpoints();
}
