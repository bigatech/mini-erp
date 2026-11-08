using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ProductLifecycle.Application;
using SharedKernel.Application;
namespace ProductLifecycle.Api;
public static class PlmDraftEndpoints
{
    public sealed record ModelRequest(string Code,string Name);
    public sealed record RevisionRequest(string RevisionNumber);
    public sealed record BomRequest(Guid ComponentProductId,decimal Quantity,decimal WasteRate,Guid? ParentLineId=null,Guid? VariantOptionId=null,Guid? SizeOptionId=null,Guid? SetPartOptionId=null,string? Label=null,string? MaterialGroupCode=null,IReadOnlyDictionary<string,string>? Attributes=null);
    public sealed record OptionRequest(string Kind,string Code,string Name,decimal Quantity=1);
    public static IEndpointRouteBuilder MapPlmDraftEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/plm").WithTags("ProductLifecycle");
        group.MapGet("/models",async(IPlmDraftRepository repository,CancellationToken ct)=>Results.Ok(new{data=await repository.ModelsAsync(ct)}));
        group.MapPost("/models",async(ModelRequest r,PlmDraftHandler handler,CancellationToken ct)=>await Run(async()=>Results.Created("/api/v1/plm/models",await handler.CreateModel(r.Code,r.Name,ct))));
        group.MapGet("/models/{id:guid}/revisions",async(Guid id,IPlmDraftRepository repository,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await repository.RevisionsAsync(id,ct)})));
        group.MapPost("/models/{id:guid}/revisions",async(Guid id,RevisionRequest r,PlmDraftHandler handler,CancellationToken ct)=>await Run(async()=>Results.Created($"/api/v1/plm/models/{id}/revisions",await handler.CreateRevision(id,r.RevisionNumber,ct))));
        group.MapGet("/revisions/{id:guid}/bom",async(Guid id,IPlmDraftRepository repository,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await repository.BomAsync(id,ct)})));
        group.MapPost("/revisions/{id:guid}/bom",async(Guid id,BomRequest r,PlmDraftHandler handler,IMaterialAttributeCatalog catalog,CancellationToken ct)=>await Run(async()=>{await catalog.ValidateAsync(r.MaterialGroupCode,r.Attributes,ct);return Results.Created($"/api/v1/plm/revisions/{id}/bom",await handler.AddBom(id,r.ComponentProductId,r.Quantity,r.WasteRate,ct,r.ParentLineId,r.VariantOptionId,r.SizeOptionId,r.SetPartOptionId,r.Label,r.MaterialGroupCode,r.Attributes));}));
        group.MapGet("/models/{id:guid}/options",async(Guid id,IPlmDraftRepository repository,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await repository.OptionsAsync(id,ct)})));
        group.MapPost("/models/{id:guid}/options",async(Guid id,OptionRequest r,IPlmDraftRepository repository,IDefinitionRepository definitions,CancellationToken ct)=>await Run(async()=>
        {
            var kind=r.Kind?.Trim();
            var code=r.Code;var name=r.Name;
            if(kind is "variant" or "size")
            {
                var selected=await DefinitionSelection.ResolveAsync(definitions,"product-lifecycle",kind=="variant"?"model-variant":"model-size",code,ct);
                code=selected.Code;name=selected.Name;
            }
            return Results.Created($"/api/v1/plm/models/{id}/options",await repository.CreateOptionAsync(id,kind!,code,name,r.Quantity,ct));
        }));
        return endpoints;
    }
    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try{return await action();}
        catch(ArgumentException ex){return Results.ValidationProblem(new Dictionary<string,string[]>{[ex.ParamName??"plm"]=[ex.Message]});}
        catch(PlmReferenceNotFoundException){return Results.NotFound(new{title="Model, revizyon veya aktif bileşen ürün bulunamadı.",code="plm_reference_not_found"});}
        catch(PlmDuplicateException){return Results.Conflict(new{title="Model kodu veya revizyon numarası zaten mevcut.",code="plm_duplicate"});}
        catch(InvalidOperationException){return Results.Conflict(new{title="Yalnız taslak revizyon düzenlenebilir.",code="plm_revision_locked"});}
    }
}
