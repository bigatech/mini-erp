using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ProductLifecycle.Application;
namespace ProductLifecycle.Api;
public static class BomRequirementsEndpoints
{
    public static IEndpointRouteBuilder MapBomRequirementsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/plm/revisions/{id:guid}/bom/requirements",async(Guid id,BomRequirementsRequest request,BomRequirementsHandler handler,CancellationToken ct)=>
        {
            try{return Results.Ok(await handler.Handle(id,request,ct));}
            catch(ArgumentException ex){return Results.ValidationProblem(new Dictionary<string,string[]>{[ex.ParamName??"bom"]=[ex.Message]});}
            catch(PlmReferenceNotFoundException){return Results.NotFound(new{title="Model revizyonu bulunamadı."});}
        }).WithTags("ProductLifecycle");return endpoints;
    }
}
