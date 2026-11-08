using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharedKernel.Application;
namespace SharedKernel.Api;

public static class MaterialAttributeEndpoints
{
    public sealed record GroupRequest(string Code,string Name);
    public sealed record FieldRequest(string Code,string Name,string DataType,string? Unit=null);
    public sealed record NameRequest(string Name);
    public sealed record FieldUpdateRequest(string Name,string? Unit=null);
    public static IEndpointRouteBuilder MapMaterialAttributeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/definitions/material-groups").WithTags("SharedDefinitions");
        group.MapGet("",async(IMaterialAttributeCatalog catalog,CancellationToken ct)=>Results.Ok(new{data=await catalog.ListAsync(ct)}));
        group.MapPut("/{code}",async(string code,NameRequest r,IMaterialAttributeWriter writer,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await writer.UpdateGroupAsync(code,r.Name,ct)})));
        group.MapDelete("/{code}",async(string code,IMaterialAttributeWriter writer,CancellationToken ct)=>await Run(async()=>{await writer.DeleteGroupAsync(code,ct);return Results.NoContent();}));
        group.MapPut("/{groupCode}/fields/{code}",async(string groupCode,string code,FieldUpdateRequest r,IMaterialAttributeWriter writer,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await writer.UpdateFieldAsync(groupCode,code,r.Name,r.Unit,ct)})));
        group.MapDelete("/{groupCode}/fields/{code}",async(string groupCode,string code,IMaterialAttributeWriter writer,CancellationToken ct)=>await Run(async()=>{await writer.DeleteFieldAsync(groupCode,code,ct);return Results.NoContent();}));
        group.MapPost("",async(GroupRequest r,IMaterialAttributeWriter writer,CancellationToken ct)=>await Run(async()=>Results.Created("/api/v1/definitions/material-groups",new{data=await writer.CreateGroupAsync(r.Code,r.Name,ct)})));
        group.MapPost("/{groupCode}/fields",async(string groupCode,FieldRequest r,IMaterialAttributeWriter writer,CancellationToken ct)=>await Run(async()=>Results.Created("/api/v1/definitions/material-groups",new{data=await writer.CreateFieldAsync(groupCode,r.Code,r.Name,r.DataType,r.Unit,ct)})));
        return endpoints;
    }
    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {try{return await action();}catch(ArgumentException ex){return Results.ValidationProblem(new Dictionary<string,string[]>{[ex.ParamName??"material"]=[ex.Message]});}catch(DuplicateDefinitionException){return Results.Conflict(new{title="Grup veya öznitelik kodu zaten var."});}catch(UnknownDefinitionCategoryException){return Results.NotFound(new{title="Malzeme grubu veya alan bulunamadı."});}catch(DefinitionInUseException){return Results.Conflict(new{title="Ürün kartında veya BOM içinde kullanılan grup ya da öznitelik değiştirilemez/silinemez.",code="definition_in_use"});}}
}
