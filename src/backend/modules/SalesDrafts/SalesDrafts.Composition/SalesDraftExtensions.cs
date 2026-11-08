using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SalesDrafts.Application;
using SalesDrafts.Infrastructure;
using SalesDrafts.Api;
namespace SalesDrafts.Composition;

public static class SalesDraftExtensions
{
    public static IServiceCollection AddSalesDrafts(this IServiceCollection services)
    {
        services.AddScoped<PostgresOrderDraftRepository>();
        services.AddScoped<IOrderDraftRepository>(p=>p.GetRequiredService<PostgresOrderDraftRepository>());
        services.AddScoped<IOrderChangeApprovalRepository>(p=>p.GetRequiredService<PostgresOrderDraftRepository>());
        services.AddScoped<IOrderMaterialRequestLink>(p=>p.GetRequiredService<PostgresOrderDraftRepository>());
        services.AddScoped<CreateOrderDraftHandler>();services.AddScoped<UpdateOrderDraftHandler>();return services;
    }
    public static IApplicationBuilder UseSalesDrafts(this IApplicationBuilder app)
    {
        using var scope=app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<PostgresOrderDraftRepository>().EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();return app;
    }
    public static IEndpointRouteBuilder MapSalesDrafts(this IEndpointRouteBuilder endpoints)=>endpoints.MapOrderDraftEndpoints().MapOrderDraftCardEndpoints();
}
