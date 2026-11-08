using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SalesDrafts.Application;
namespace SalesDrafts.Api;

public static class OrderDraftEndpoints
{
    public static IEndpointRouteBuilder MapOrderDraftEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/sales/order-drafts").WithTags("SalesDrafts");
        group.MapGet("",async(IOrderDraftRepository repository,CancellationToken ct)=>Results.Ok(new{data=await repository.ListAsync(ct)}));
        group.MapPost("",async(CreateOrderDraft request,CreateOrderDraftHandler handler,CancellationToken ct)=>
        {
            try {var draft=await handler.Handle(request,ct);return Results.Created($"/api/v1/sales/order-drafts/{draft.Id}",draft);}
            catch(ArgumentException ex){return Results.ValidationProblem(new Dictionary<string,string[]> {[ex.ParamName??"draft"]=[ex.Message]});}
            catch(InvalidOrderDraftSelectionException){return Results.NotFound(new{title="Model revizyonuna ait varyant/beden seçimi bulunamadı.",code="draft_selection_not_found"});}
            catch(DuplicateOrderDraftException){return Results.Conflict(new{title="Sipariş taslak numarası zaten mevcut.",code="duplicate_order_draft"});}
        });
        return endpoints;
    }
}
