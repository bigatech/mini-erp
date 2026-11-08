using SalesDrafts.Application;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;
public sealed class OrderApprovalTests
{
    private static readonly OrderChangeApproval Approval=new(Guid.NewGuid(),"Update",3,[new(Guid.NewGuid(),Guid.NewGuid(),9)],DateTimeOffset.UtcNow);
    private static OrderDraftDto Draft(int version=4,string status="Draft")=>new(Guid.NewGuid(),"SIP-1",Guid.NewGuid(),status,[new(Guid.NewGuid(),Guid.NewGuid(),2)],DateTimeOffset.UtcNow,version,Approval,true);
    [Fact]public void Pending_request_blocks_further_writes()=>Assert.Throws<OrderDraftPendingApprovalException>(()=>OrderDraftCardRules.Writable(Draft(),4));
    [Fact]public void Decision_requires_current_etag()=>Assert.Throws<OrderDraftVersionException>(()=>OrderApprovalRules.Decidable(Draft(),Approval,3));
    [Fact]public void Decision_rejects_changed_base()=>Assert.Throws<OrderApprovalStateException>(()=>OrderApprovalRules.Decidable(Draft(5),Approval,5));
    [Theory][InlineData("Approved")][InlineData("Rejected")]
    public void Decision_cannot_be_replayed(string status)=>Assert.Throws<OrderApprovalStateException>(()=>OrderApprovalRules.Decidable(Draft(),Approval with{Status=status},4));
    [Fact]public void Cancelled_order_cannot_be_approved()=>Assert.Throws<OrderApprovalStateException>(()=>OrderApprovalRules.Decidable(Draft(status:"Cancelled"),Approval,4));
    [Fact]public void Decision_accepts_pending_unchanged_order()=>OrderApprovalRules.Decidable(Draft(),Approval,4);
}
