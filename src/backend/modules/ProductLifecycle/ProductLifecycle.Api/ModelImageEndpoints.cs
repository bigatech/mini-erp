using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ProductLifecycle.Application;
namespace ProductLifecycle.Api;
public static class ModelImageEndpoints
{
    public static IEndpointRouteBuilder MapModelImageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/plm/models/{modelId:guid}/images").WithTags("ProductLifecycle");
        group.MapGet("",async(Guid modelId,IModelImageService service,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await service.ListAsync(modelId,ct)})));
        group.MapPost("",async(Guid modelId,IFormFile file,IModelImageService service,CancellationToken ct)=>await Run(async()=>
        {
            if(file.Length<=0||file.Length>ModelImageRules.MaximumBytes)return Results.BadRequest(new{title="Görsel 1 bayt ile 5 MB arasında olmalıdır."});
            await using var stream=file.OpenReadStream();using var buffer=new MemoryStream();var chunk=new byte[65536];int count;
            while((count=await stream.ReadAsync(chunk,ct))>0){if(buffer.Length+count>ModelImageRules.MaximumBytes)return Results.BadRequest(new{title="Görsel en fazla 5 MB olabilir."});await buffer.WriteAsync(chunk.AsMemory(0,count),ct);}
            var created=await service.UploadAsync(modelId,file.FileName,buffer.ToArray(),ct);return Results.Created(created.Url,created);
        })).DisableAntiforgery();
        group.MapGet("/{imageId:guid}/content",async(Guid modelId,Guid imageId,IModelImageService service,HttpContext context,CancellationToken ct)=>await Run(async()=>
        {var image=await service.ContentAsync(modelId,imageId,ct);if(image is null)return Results.NotFound();context.Response.Headers["X-Content-Type-Options"]="nosniff";return Results.File(image.Bytes,image.ContentType);}));
        return endpoints;
    }
    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {try{return await action();}catch(ArgumentException ex){return Results.ValidationProblem(new Dictionary<string,string[]>{[ex.ParamName??"file"]=[ex.Message]});}catch(PlmReferenceNotFoundException){return Results.NotFound(new{title="Model bulunamadı."});}catch(ModelImageStorageException){return Results.Problem(statusCode:503,title:"Görsel depolama servisine erişilemiyor.");}}
}

