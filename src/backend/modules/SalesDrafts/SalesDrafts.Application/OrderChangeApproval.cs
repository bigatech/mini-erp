namespace SalesDrafts.Application;
public sealed record OrderChangeApproval(Guid Id,string Type,int BaseVersion,IReadOnlyList<OrderDraftCell> ProposedLines,DateTimeOffset RequestedAt,string Status="Pending",string? DecidedBy=null,DateTimeOffset? DecidedAt=null);
public sealed record DecideOrderChange(bool Approve);
public sealed class OrderDraftPendingApprovalException:Exception;
public sealed class OrderApprovalNotFoundException:Exception;
public sealed class OrderApprovalStateException:Exception;
public interface IOrderChangeApprovalRepository
{
    Task<IReadOnlyList<OrderChangeApproval>> ApprovalsAsync(Guid id,CancellationToken ct);
    Task<OrderDraftDto> DecideAsync(Guid id,Guid approvalId,int expectedVersion,bool approve,string actor,CancellationToken ct);
}
// Internal application port for the future material request module; deliberately no public write endpoint.
public interface IOrderMaterialRequestLink
{
    Task LinkAsync(Guid orderId,Guid materialRequestId,CancellationToken ct);
}
public static class OrderApprovalRules
{
    public static void Decidable(OrderDraftDto current,OrderChangeApproval approval,int expectedVersion)
    {
        if(current.Version!=expectedVersion)throw new OrderDraftVersionException();
        if(approval.Status!="Pending"||current.Status!="Draft"||current.Version!=approval.BaseVersion+1)throw new OrderApprovalStateException();
    }
}
