using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ProductLifecycle.Application;
namespace ProductLifecycle.Api;

public static class ProductionRouteEndpoints
{
    public sealed record OperationRequest(string Code,string Name,decimal? StandardDuration,string DurationUnit);
    public sealed record RouteStepRequest(Guid OperationId,int Sequence);
    public static IEndpointRouteBuilder MapProductionRouteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/plm").WithTags("ProductLifecycle");
        group.MapGet("/operations",async(IProductionRouteRepository repository,CancellationToken ct)=>Results.Ok(new{data=await repository.OperationsAsync(ct)}));
        group.MapPost("/operations",async(OperationRequest request,ProductionRouteHandler handler,CancellationToken ct)=>await Run(async()=>
        {
            if(request.StandardDuration is null)throw new ArgumentException("Standart süre girin.","standardDuration");
            return Results.Created("/api/v1/plm/operations",await handler.CreateOperation(request.Code,request.Name,request.StandardDuration.Value,request.DurationUnit,ct));
        }));
        group.MapGet("/revisions/{id:guid}/route",async(Guid id,IProductionRouteRepository repository,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await repository.RouteAsync(id,ct)})));
        group.MapPost("/revisions/{id:guid}/route",async(Guid id,RouteStepRequest request,ProductionRouteHandler handler,CancellationToken ct)=>await Run(async()=>Results.Created($"/api/v1/plm/revisions/{id}/route",await handler.AddStep(id,request.OperationId,request.Sequence,ct))));
        return endpoints;
    }
    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try{return await action();}
        catch(ArgumentException ex){return Results.ValidationProblem(new Dictionary<string,string[]>{[ex.ParamName??"route"]=[ex.Message]});}
        catch(DuplicateRouteEntryException){return Results.Conflict(new{title="Operasyon kodu, rota sırası veya revizyondaki operasyon zaten mevcut.",code="duplicate_route_entry"});}
        catch(RouteReferenceNotFoundException){return Results.NotFound(new{title="Model revizyonu veya operasyon bulunamadı.",code="route_reference_not_found"});}
        catch(RouteRevisionLockedException){return Results.Conflict(new{title="Yalnız taslak revizyonun rotası değiştirilebilir.",code="route_revision_locked"});}
    }
}
