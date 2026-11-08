using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SalesDrafts.Application;
namespace SalesDrafts.Api;
public static class OrderDraftCardEndpoints
{
    public static IEndpointRouteBuilder MapOrderDraftCardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/sales/order-drafts").WithTags("SalesDrafts");
        group.MapGet("/{id:guid}",async(Guid id,IOrderDraftRepository repository,HttpContext context,CancellationToken ct)=>
        {var draft=await repository.GetAsync(id,ct);if(draft is null)return (IResult)Results.NotFound();context.Response.Headers.ETag=$"\"{draft.Version}\"";return Results.Ok(draft);});
        group.MapGet("/{id:guid}/events",async(Guid id,IOrderDraftRepository repository,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await repository.EventsAsync(id,ct)})));
        group.MapPut("/{id:guid}",async(Guid id,UpdateOrderDraft request,UpdateOrderDraftHandler handler,HttpContext context,CancellationToken ct)=>
        {
            if(!TryVersion(context,out var version,out var error))return error!;
            return await Run(async()=>{var draft=await handler.Handle(id,version,request,ct);context.Response.Headers.ETag=$"\"{draft.Version}\"";return Results.Ok(draft);});
        });
        group.MapPost("/{id:guid}/cancel",async(Guid id,IOrderDraftRepository repository,HttpContext context,CancellationToken ct)=>
        {
            if(!TryVersion(context,out var version,out var error))return error!;
            return await Run(async()=>{var draft=await repository.CancelAsync(id,version,ct);context.Response.Headers.ETag=$"\"{draft.Version}\"";return Results.Ok(draft);});
        });
        group.MapGet("/{id:guid}/approvals",async(Guid id,IOrderChangeApprovalRepository repository,CancellationToken ct)=>await Run(async()=>Results.Ok(new{data=await repository.ApprovalsAsync(id,ct)})));
        group.MapPost("/{id:guid}/approvals/{approvalId:guid}/decision",async(Guid id,Guid approvalId,DecideOrderChange request,IOrderChangeApprovalRepository repository,HttpContext context,CancellationToken ct)=>
        {
            if(context.User.Identity?.IsAuthenticated!=true)return Results.StatusCode(401);
            if(!context.User.HasClaim("permission","sales.orders.approve"))return Results.StatusCode(403);
            var actor=context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value??context.User.FindFirst("sub")?.Value;
            if(string.IsNullOrWhiteSpace(actor))return Results.StatusCode(403);
            if(!TryVersion(context,out var version,out var error))return error!;
            return await Run(async()=>{var draft=await repository.DecideAsync(id,approvalId,version,request.Approve,actor,ct);context.Response.Headers.ETag=$"\"{draft.Version}\"";return Results.Ok(draft);});
        });
        return endpoints;
    }
    private static bool TryVersion(HttpContext context,out int version,out IResult? error)
    {
        version=0;error=null;var header=context.Request.Headers.IfMatch.ToString();
        if(string.IsNullOrEmpty(header)){error=Results.Problem(statusCode:428,title:"If-Match sürümü gereklidir.");return false;}
        if(header.Length<3||header[0]!='"'||header[^1]!='"'||!int.TryParse(header[1..^1],out version)||version<=0){error=Results.BadRequest(new{title="If-Match tek bir geçerli sürüm olmalıdır."});return false;}
        return true;
    }
    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try{return await action();}
        catch(OrderDraftNotFoundException){return Results.NotFound(new{title="Sipariş taslağı bulunamadı."});}
        catch(OrderDraftVersionException){return Results.Problem(statusCode:412,title:"Kayıt başka bir işlemde değişti. Yeniden yükleyin.");}
        catch(OrderDraftPendingApprovalException){return Results.Conflict(new{title="Sipariş değişikliği onay bekliyor. Yeni işlem yapılamaz."});}
        catch(OrderApprovalNotFoundException){return Results.NotFound(new{title="Onay talebi bulunamadı."});}
        catch(OrderApprovalStateException){return Results.Conflict(new{title="Onay talebi sonuçlanmış veya sipariş sürümü değişmiş."});}
        catch(OrderDraftStateException){return Results.Conflict(new{title="İptal edilmiş taslak değiştirilemez."});}
        catch(InvalidOrderDraftSelectionException){return Results.NotFound(new{title="Bu revizyon için varyant/beden/parça bulunamadı."});}
        catch(ArgumentException ex){return Results.ValidationProblem(new Dictionary<string,string[]>{[ex.ParamName??"draft"]=[ex.Message]});}
    }
}
