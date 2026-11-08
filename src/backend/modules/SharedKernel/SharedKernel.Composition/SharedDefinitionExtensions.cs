using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Api;
using SharedKernel.Application;
using SharedKernel.Infrastructure;

namespace SharedKernel.Composition;

public static class SharedDefinitionExtensions
{
    // Uses the existing host NpgsqlDataSource registration; credentials are not duplicated.
    public static IServiceCollection AddSharedDefinitions(this IServiceCollection services)
    {
        services.AddScoped<PostgresDefinitionRepository>();
        services.AddScoped<PostgresMaterialAttributeCatalog>();
        services.AddScoped<IMaterialAttributeCatalog>(p=>p.GetRequiredService<PostgresMaterialAttributeCatalog>());
        services.AddScoped<IMaterialAttributeWriter>(p=>p.GetRequiredService<PostgresMaterialAttributeCatalog>());
        services.AddScoped<IDefinitionRepository>(provider=>provider.GetRequiredService<PostgresDefinitionRepository>());
        return services;
    }
    // Call after UseProductLifecycle so existing values can be carried across.
    public static IApplicationBuilder UseSharedDefinitions(this IApplicationBuilder app)
    {
        using var scope=app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<PostgresDefinitionRepository>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        scope.ServiceProvider.GetRequiredService<PostgresMaterialAttributeCatalog>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return app;
    }
    public static IEndpointRouteBuilder MapSharedDefinitions(this IEndpointRouteBuilder endpoints)=>endpoints.MapDefinitionEndpoints().MapMaterialAttributeEndpoints();
}
