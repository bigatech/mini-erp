using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel.Application;

namespace SharedKernel.Api;

public static class DefinitionEndpoints
{
    public sealed record CategoryRequest(string Code,string Name,string ModuleCode,string FeatureCode);
    public sealed record ItemRequest(string Code,string Name,bool IsActive=true);
    public sealed record NameRequest(string Name);
    public sealed record UpdateItemRequest(string Name,bool IsActive=true);

    public static IEndpointRouteBuilder MapDefinitionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/definitions").WithTags("SharedDefinitions");
        group.MapGet("/targets",() => Results.Ok(new {data=DefinitionTargets.All}));
        group.MapGet("/categories",async (IDefinitionRepository repository,CancellationToken ct) => Results.Ok(new {data=await repository.CategoriesAsync(ct)}));
        group.MapPut("/categories/{code}",async(string code,NameRequest r,IDefinitionRepository repository,CancellationToken ct)=>await Execute(async()=>Results.Ok(new{data=await repository.UpdateCategoryAsync(code,r.Name,ct)})));
        group.MapDelete("/categories/{code}",async(string code,IDefinitionRepository repository,CancellationToken ct)=>await Execute(async()=>{await repository.DeleteCategoryAsync(code,ct);return Results.NoContent();}));
        group.MapPut("/{category}/items/{code}",async(string category,string code,UpdateItemRequest r,IDefinitionRepository repository,CancellationToken ct)=>await Execute(async()=>Results.Ok(new{data=await repository.UpdateItemAsync(category,code,r.Name,r.IsActive,ct)})));
        group.MapDelete("/{category}/items/{code}",async(string category,string code,IDefinitionRepository repository,CancellationToken ct)=>await Execute(async()=>{await repository.DeleteItemAsync(category,code,ct);return Results.NoContent();}));
        group.MapPost("/categories",async (CategoryRequest request,IDefinitionRepository repository,CancellationToken ct) =>
            await Execute(async () => Results.Created("/api/v1/definitions/categories",new {data=await repository.CreateCategoryAsync(request.Code,request.Name,request.ModuleCode,request.FeatureCode,ct)})));
        group.MapGet("/{category}/items",async (string category,IDefinitionRepository repository,CancellationToken ct) =>
            await Execute(async () => Results.Ok(new {data=await repository.ItemsAsync(category,ct)})));
        group.MapPost("/{category}/items",async (string category,ItemRequest request,IDefinitionRepository repository,CancellationToken ct) =>
            await Execute(async () => Results.Created($"/api/v1/definitions/{Uri.EscapeDataString(category)}/items",new {data=await repository.CreateItemAsync(category,request.Code,request.Name,request.IsActive,ct)})));
        return endpoints;
    }
    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try {return await action();}
        catch(ArgumentException ex) {return Results.ValidationProblem(new Dictionary<string,string[]> {[ex.ParamName??"definition"]=[ex.Message]});}
        catch(DuplicateDefinitionException) {return Results.Conflict(new {title="Definition code or module/feature binding already exists",code="duplicate_definition_code"});}
        catch(UnknownDefinitionCategoryException) {return Results.NotFound(new {title="Definition category not found",code="definition_category_not_found"});}
        catch(DefinitionInUseException) {return Results.Conflict(new{title="Kullanılan tanım veya korunan kategori değiştirilemez/silinemez.",code="definition_in_use"});}
    }
}
