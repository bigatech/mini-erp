using ProductLifecycle.Application;
namespace SalesDrafts.Application;
public sealed record OrderDraftEvent(Guid Id,string Type,DateTimeOffset At,string FromStatus,string ToStatus,int Version);
public sealed record UpdateOrderDraft(IReadOnlyList<OrderDraftCell>? Lines);
public sealed class OrderDraftNotFoundException:Exception;
public sealed class OrderDraftVersionException:Exception;
public sealed class OrderDraftStateException:Exception;
public static class OrderDraftCardRules
{
    public static void Writable(OrderDraftDto draft,int expectedVersion)
    {
        if(draft.Version!=expectedVersion)throw new OrderDraftVersionException();
        if(draft.PendingApproval is not null)throw new OrderDraftPendingApprovalException();
        if(draft.Status!="Draft")throw new OrderDraftStateException();
    }
}
public sealed class UpdateOrderDraftHandler(IOrderDraftRepository repository,IPlmConfigurationReader configuration)
{
    public async Task<OrderDraftDto> Handle(Guid id,int expectedVersion,UpdateOrderDraft request,CancellationToken ct)
    {
        var current=await repository.GetAsync(id,ct)??throw new OrderDraftNotFoundException();
        OrderDraftCardRules.Writable(current,expectedVersion);
        OrderDraftRules.Validate(new(null,current.RevisionId,request.Lines));
        var lines=request.Lines!.ToArray();
        if(lines.Any(l=>l.SetPartOptionId is null)&&await configuration.HasSetPartsAsync(current.RevisionId,ct))throw new ArgumentException("Setli model için parça seçin.","lines");
        foreach(var line in lines)
        {
            if(!await configuration.ExistsSelectionAsync(current.RevisionId,line.VariantOptionId,line.SizeOptionId,ct))throw new InvalidOrderDraftSelectionException();
            if(line.SetPartOptionId is {} part&&!await configuration.ExistsSetPartAsync(current.RevisionId,part,ct))throw new InvalidOrderDraftSelectionException();
        }
        return await repository.ReplaceAsync(id,expectedVersion,lines,ct);
    }
}
