using ProductLifecycle.Application;
using SalesDrafts.Application;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;
public sealed class OrderDraftCardTests
{
    private static readonly Guid Revision=Guid.NewGuid(),Variant=Guid.NewGuid(),Size=Guid.NewGuid();
    private static OrderDraftDto Draft(string status="Draft",int version=1)=>new(Guid.NewGuid(),"SIP-1",Revision,status,[new(Variant,Size,2)],DateTimeOffset.UtcNow,version);
    private sealed class Configuration:IPlmConfigurationReader
    {
        public bool Allowed {get;init;}=true;
        public Task<bool> ExistsSelectionAsync(Guid r,Guid v,Guid s,CancellationToken ct)=>Task.FromResult(Allowed&&r==Revision&&v==Variant&&s==Size);
    }
    private sealed class Repository(OrderDraftDto? current):IOrderDraftRepository
    {
        public bool Replaced {get;private set;}
        public Task<OrderDraftDto?> GetAsync(Guid id,CancellationToken ct)=>Task.FromResult(current);
        public Task<OrderDraftDto> ReplaceAsync(Guid id,int version,IReadOnlyList<OrderDraftCell> lines,CancellationToken ct)
        {Replaced=true;return Task.FromResult(current! with{Lines=lines,Version=version+1});}
        public Task<IReadOnlyList<OrderDraftDto>> ListAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<OrderDraftDto> CreateAsync(string n,Guid r,IReadOnlyList<OrderDraftCell> l,CancellationToken ct)=>throw new NotSupportedException();
    }
    [Fact]public void Stale_version_is_rejected()=>Assert.Throws<OrderDraftVersionException>(()=>OrderDraftCardRules.Writable(Draft(version:2),1));
    [Fact]public void Cancelled_record_is_read_only()=>Assert.Throws<OrderDraftStateException>(()=>OrderDraftCardRules.Writable(Draft("Cancelled"),1));
    [Fact]public async Task Missing_draft_is_not_found()
    {await Assert.ThrowsAsync<OrderDraftNotFoundException>(()=>new UpdateOrderDraftHandler(new Repository(null),new Configuration()).Handle(Guid.NewGuid(),1,new([new(Variant,Size,4)]),default));}
    [Fact]public async Task Invalid_quantity_never_reaches_persistence()
    {var draft=Draft();var repo=new Repository(draft);await Assert.ThrowsAsync<ArgumentException>(()=>new UpdateOrderDraftHandler(repo,new Configuration()).Handle(draft.Id,1,new([new(Variant,Size,0)]),default));Assert.False(repo.Replaced);}
    [Fact]public async Task Wrong_revision_selection_never_reaches_persistence()
    {var draft=Draft();var repo=new Repository(draft);await Assert.ThrowsAsync<InvalidOrderDraftSelectionException>(()=>new UpdateOrderDraftHandler(repo,new Configuration{Allowed=false}).Handle(draft.Id,1,new([new(Variant,Size,4)]),default));Assert.False(repo.Replaced);}
    [Fact]public async Task Updating_quantity_retains_document_identity()
    {var draft=Draft();var result=await new UpdateOrderDraftHandler(new Repository(draft),new Configuration()).Handle(draft.Id,1,new([new(Variant,Size,4)]),default);Assert.Equal(draft.Number,result.Number);Assert.Equal(draft.RevisionId,result.RevisionId);Assert.Equal(draft.Id,result.Id);Assert.Equal(2,result.Version);Assert.Equal(4,result.Lines[0].Quantity);}
}
