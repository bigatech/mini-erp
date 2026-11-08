using System.Security.Claims;
using Cutting.Application;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
namespace Cutting.Api;

public static class CuttingEndpoints
{
    public static IEndpointRouteBuilder MapCuttingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/cutting").WithTags("Cutting");
        group.AddEndpointFilter(async(context,next)=>
        {
            if(context.HttpContext.User.Identity?.IsAuthenticated!=true)return Results.Unauthorized();
            var permission=HttpMethods.IsPut(context.HttpContext.Request.Method)?"cutting.write":"cutting.read";
            if(!context.HttpContext.User.HasClaim("permission",permission))return Results.StatusCode(403);
            return await next(context);
        });
        group.MapGet("/options",async(ICuttingStore store,CancellationToken ct)=>await Run(async()=>Results.Ok(await store.OptionsAsync(ct))));
        group.MapGet("/records",async(ICuttingStore store,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await store.ListAsync(ct)})));
        group.MapGet("/records/{id:guid}",async(Guid id,ICuttingStore store,HttpContext context,CancellationToken ct)=>await Run(async()=>
        {
            var detail=await store.GetAsync(id,ct);if(detail is null)return Results.NotFound();
            context.Response.Headers.ETag=$"\"{detail.Document.Version}\"";return Results.Ok(detail);
        }));
        group.MapGet("/records/{id:guid}/events",async(Guid id,ICuttingStore store,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await store.EventsAsync(id,ct)})));
        group.MapPut("/records/{id:guid}",async(Guid id,SaveCuttingDocument request,ICuttingStore store,HttpContext context,CancellationToken ct)=>
        {
            var create=context.Request.Headers.IfNoneMatch.ToString()=="*";
            int? version=null;
            if(!create)
            {
                var match=context.Request.Headers.IfMatch.ToString();
                if(string.IsNullOrEmpty(match))return Results.Problem(statusCode:428,title:"Yeni kayıt için If-None-Match: *, düzenleme için If-Match sürümü gereklidir.");
                if(match.Length<3||match[0]!='\"'||match[^1]!='\"'||!int.TryParse(match[1..^1],out var parsed)||parsed<=0)
                    return Results.BadRequest(new{title="If-Match tek bir pozitif sürüm olmalıdır."});
                version=parsed;
            }
            var actor=context.User.FindFirstValue(ClaimTypes.NameIdentifier)??context.User.FindFirstValue("sub");
            if(context.User.Identity?.IsAuthenticated==true&&string.IsNullOrWhiteSpace(actor))return Results.StatusCode(403);
            return await Run(async()=>
            {
                var detail=await store.SaveAsync(id,request,version,create,actor,ct);
                context.Response.Headers.ETag=$"\"{detail.Document.Version}\"";
                return Results.Ok(detail);
            });
        });
        group.MapGet("/report",async(Guid? orderId,Guid? materialId,string? status,DateOnly? from,DateOnly? to,
            Guid? variantOptionId,Guid? sizeOptionId,Guid? recordId,ICuttingStore store,CancellationToken ct)=>await Run(async()=>
                Results.Ok(await store.ReportAsync(new(orderId,materialId,status,from,to,variantOptionId,sizeOptionId),recordId,ct))));
        return endpoints;
    }
    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try{return await action();}
        catch(CuttingValidationException ex){return Results.ValidationProblem(ex.Errors.ToDictionary(p=>p.Key,p=>p.Value));}
        catch(CuttingStoreException ex){return Results.Problem(statusCode:ex.Status,title:ex.Message);}
        catch(OverflowException){return Results.ValidationProblem(new Dictionary<string,string[]>{["quantity"]=["Hesap sınırı aşıldı; miktarları kontrol edin."]});}
    }
}

