using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ProductLifecycle.Application;
using SharedKernel.Application;
namespace ProductLifecycle.Api;
public static class BomMutationEndpoints
{
    public static IEndpointRouteBuilder MapBomMutationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/plm/revisions/{revisionId:guid}/bom").WithTags("ProductLifecycle");
        group.MapPut("/{lineId:guid}",async(Guid revisionId,Guid lineId,UpdateBomLine request,UpdateBomLineHandler handler,IMaterialAttributeCatalog catalog,CancellationToken ct)=>await Run(async()=>{await catalog.ValidateAsync(request.MaterialGroupCode,request.Attributes,ct);return Results.Ok(await handler.Handle(revisionId,lineId,request,ct));}));
        group.MapDelete("/{lineId:guid}",async(Guid revisionId,Guid lineId,IPlmDraftRepository repository,CancellationToken ct)=>await Run(async()=>{await repository.DeleteBomAsync(revisionId,lineId,ct);return Results.NoContent();}));
        return endpoints;
    }
    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try{return await action();}
        catch(ArgumentException ex){return Results.ValidationProblem(new Dictionary<string,string[]>{[ex.ParamName??"bom"]=[ex.Message]});}
        catch(PlmReferenceNotFoundException){return Results.NotFound(new{title="Revizyon veya ürün ağacı kalemi bulunamadı."});}
        catch(BomHasChildrenException){return Results.Conflict(new{title="Alt malzemesi olan satır silinemez. Önce alt satırları kaldırın.",code="bom_has_children"});}
        catch(InvalidOperationException){return Results.Conflict(new{title="Yalnız taslak revizyon düzenlenebilir.",code="plm_revision_locked"});}
    }
}
