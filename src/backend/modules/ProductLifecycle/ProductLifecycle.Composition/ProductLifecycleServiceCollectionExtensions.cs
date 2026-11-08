using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Npgsql;
using ProductLifecycle.Api;
using ProductLifecycle.Application;
using ProductLifecycle.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace ProductLifecycle.Composition;

public static class ProductLifecycleServiceCollectionExtensions
{
    public static IServiceCollection AddProductLifecycle(this IServiceCollection services, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        services.AddScoped<PostgresProductRepository>();
        services.AddSingleton(p =>
        {
            var config=p.GetRequiredService<IConfiguration>();
            return new S3ModelImageStore(config["Media:S3Endpoint"]??"http://object-storage:9000",config["Media:AccessKey"]??"",config["Media:SecretKey"]??"");
        });
        services.AddScoped<PostgresModelImageService>();
        services.AddScoped<IModelImageService>(p=>p.GetRequiredService<PostgresModelImageService>());
        services.AddScoped<PostgresPlmDraftRepository>();
        services.AddScoped<PostgresProductionRouteRepository>();
        services.AddScoped<PostgresRevisionLifecycleRepository>();
        services.AddScoped<IRevisionLifecycleRepository>(p => p.GetRequiredService<PostgresRevisionLifecycleRepository>());
        services.AddScoped<RevisionLifecycleHandler>();
        services.AddScoped<IProductionRouteRepository>(p => p.GetRequiredService<PostgresProductionRouteRepository>());
        services.AddScoped<ProductionRouteHandler>();
        services.AddScoped<IPlmDraftRepository>(p => p.GetRequiredService<PostgresPlmDraftRepository>());
        services.AddScoped<PlmDraftHandler>();
        services.AddScoped<UpdateBomLineHandler>();
        services.AddScoped<IRevisionBomOptionsReader,PostgresRevisionBomOptionsReader>();
        services.AddScoped<BomRequirementsHandler>();
        services.AddScoped<IPlmConfigurationReader>(p => p.GetRequiredService<PostgresPlmDraftRepository>());
        services.AddScoped<IProductRepository>(provider => provider.GetRequiredService<PostgresProductRepository>());
        services.AddScoped<CreateProductHandler>();
        services.AddScoped<UpdateProductHandler>();
        services.AddScoped<SetProductStatusHandler>();
        services.AddScoped<ListProductsHandler>();
        return services;
    }

    public static IApplicationBuilder UseProductLifecycle(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<PostgresProductRepository>();
        repository.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        scope.ServiceProvider.GetRequiredService<PostgresPlmDraftRepository>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        scope.ServiceProvider.GetRequiredService<PostgresProductionRouteRepository>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        scope.ServiceProvider.GetRequiredService<PostgresRevisionLifecycleRepository>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        scope.ServiceProvider.GetRequiredService<PostgresModelImageService>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return app;
    }

    public static IEndpointRouteBuilder MapProductLifecycle(this IEndpointRouteBuilder endpoints) => endpoints.MapProductEndpoints().MapPlmDraftEndpoints().MapProductionRouteEndpoints().MapRevisionLifecycleEndpoints().MapModelImageEndpoints().MapBomRequirementsEndpoints().MapBomMutationEndpoints();
}

