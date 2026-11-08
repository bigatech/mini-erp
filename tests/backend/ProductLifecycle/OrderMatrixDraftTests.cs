using SalesDrafts.Application;
using ProductLifecycle.Application;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;
public sealed class OrderMatrixDraftTests
{
    private static readonly Guid Revision=Guid.NewGuid(), Variant=Guid.NewGuid(), Size=Guid.NewGuid();
    [Fact] public void Empty_matrix_is_rejected()=>Assert.Throws<ArgumentException>(()=>OrderDraftRules.Validate(new("D1",Revision,[])));
    [Fact] public void Null_matrix_is_rejected()=>Assert.Throws<ArgumentException>(()=>OrderDraftRules.Validate(new("D1",Revision,null)));
    [Fact] public void Duplicate_variant_size_cell_is_rejected()=>Assert.Throws<ArgumentException>(()=>OrderDraftRules.Validate(new("D1",Revision,[new(Variant,Size,1),new(Variant,Size,2)])));
    [Fact] public void Non_positive_cell_is_rejected()=>Assert.Throws<ArgumentException>(()=>OrderDraftRules.Validate(new("D1",Revision,[new(Variant,Size,0)])));
    [Fact] public void Decimal_quantity_is_not_silently_rounded()=>Assert.Equal(string.Empty,OrderDraftRules.Validate(new(" D1 ",Revision,[new(Variant,Size,1.25m)])));
    [Fact] public void Same_variant_size_for_different_parts_is_allowed()
    {
        var draft=new CreateOrderDraft("D1",Revision,[new(Variant,Size,1,Guid.NewGuid()),new(Variant,Size,2,Guid.NewGuid())]);
        Assert.Equal(string.Empty,OrderDraftRules.Validate(draft));
    }
    [Fact] public void Empty_part_id_is_rejected()=>Assert.Throws<ArgumentException>(()=>OrderDraftRules.Validate(new("D1",Revision,[new(Variant,Size,1,Guid.Empty)])));
    [Fact] public async Task Set_model_without_piece_is_rejected_before_persistence()
    {
        var repository=new Repository();
        await Assert.ThrowsAsync<ArgumentException>(()=>new CreateOrderDraftHandler(repository,new SetSelection()).Handle(new("D1",Revision,[new(Variant,Size,1)]),default));
        Assert.False(repository.Created);
    }
    private sealed class SetSelection:IPlmConfigurationReader
    {
        public Task<bool> ExistsSelectionAsync(Guid revisionId,Guid variantOptionId,Guid sizeOptionId,CancellationToken ct)=>Task.FromResult(true);
        public Task<bool> HasSetPartsAsync(Guid revisionId,CancellationToken ct)=>Task.FromResult(true);
    }
    [Fact] public async Task Wrong_model_selection_is_rejected_without_persistence()
    {
        var repository=new Repository();
        await Assert.ThrowsAsync<InvalidOrderDraftSelectionException>(()=>new CreateOrderDraftHandler(repository,new Selection(false)).Handle(new("D1",Revision,[new(Variant,Size,1)]),default));
        Assert.False(repository.Created);
    }
    [Fact] public async Task Valid_matrix_preserves_exact_cells_and_revision()
    {
        var repository=new Repository();var cells=new OrderDraftCell[]{new(Variant,Size,1.25m)};
        var draft=await new CreateOrderDraftHandler(repository,new Selection(true)).Handle(new(" D1 ",Revision,cells),default);
        Assert.True(repository.Created);Assert.Equal("SIP-00000001",draft.Number);Assert.Equal(Revision,draft.RevisionId);Assert.Equal(1.25m,Assert.Single(draft.Lines).Quantity);Assert.Equal("Draft",draft.Status);
    }
    [Fact] public async Task Caller_number_is_ignored_and_not_passed_to_persistence()
    {
        var repository=new Repository();
        await new CreateOrderDraftHandler(repository,new Selection(true)).Handle(new("CALLER-CHOSEN",Revision,[new(Variant,Size,1)]),default);
        Assert.Equal(string.Empty,repository.ReceivedNumber);
    }
    [Fact] public async Task Missing_caller_number_does_not_prevent_creation()
    {
        var repository=new Repository();
        var result=await new CreateOrderDraftHandler(repository,new Selection(true)).Handle(new(null,Revision,[new(Variant,Size,1)]),default);
        Assert.True(repository.Created);Assert.StartsWith("SIP-",result.Number);
    }
    private sealed class Selection(bool valid):IPlmConfigurationReader
    {public Task<bool> ExistsSelectionAsync(Guid revisionId,Guid variantOptionId,Guid sizeOptionId,CancellationToken ct)=>Task.FromResult(valid);}
    private sealed class Repository:IOrderDraftRepository
    {
        public bool Created {get;private set;}
        public string? ReceivedNumber {get;private set;}
        public Task<IReadOnlyList<OrderDraftDto>> ListAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<OrderDraftDto> CreateAsync(string number,Guid revisionId,IReadOnlyList<OrderDraftCell> lines,CancellationToken ct)
        {Created=true;ReceivedNumber=number;return Task.FromResult(new OrderDraftDto(Guid.NewGuid(),"SIP-00000001",revisionId,"Draft",lines,DateTimeOffset.UtcNow));}
    }
}
