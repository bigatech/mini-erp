using ProductLifecycle.Application;
using ProductLifecycle.Domain;
using Xunit;

namespace MiniErp.ProductLifecycle.Tests;

public sealed class ProductUpdateTests
{
    private sealed class Repository : IProductRepository
    {
        public bool UpdateCalled {get;private set;}
        public bool Missing {get;init;}
        public bool InUse {get;init;}
        public Task<Product> CreateAsync(Product product,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<Product?> UpdateAsync(Guid id,Product product,CancellationToken ct)
        {
            UpdateCalled=true;
            if(InUse) throw new ProductInUseException();
            return Task.FromResult(Missing ? null : Product.Rehydrate(id,product.Code,product.Name,product.Type,product.StockUnit,false));
        }
    }
    [Fact] public async Task Invalid_edit_is_rejected_before_repository_update()
    {
        var repository=new Repository();
        await Assert.ThrowsAsync<ArgumentException>(()=>new UpdateProductHandler(repository).Handle(Guid.NewGuid(),new("bad\0code","Name","Type","Unit"),default));
        Assert.False(repository.UpdateCalled);
    }
    [Fact] public async Task Edit_keeps_existing_identity_and_inactive_state()
    {
        var id=Guid.NewGuid();
        var result=await new UpdateProductHandler(new Repository()).Handle(id,new(" NEW "," Ad "," Tip "," Birim "),default);
        Assert.NotNull(result); Assert.Equal(id,result.Id); Assert.False(result.IsActive);
        Assert.Equal("NEW",result.Code); Assert.Equal("Ad",result.Name);
    }
    [Fact] public async Task Missing_product_returns_no_product()
    {
        Assert.Null(await new UpdateProductHandler(new Repository {Missing=true}).Handle(Guid.NewGuid(),new("A","Ad","Tip","Birim"),default));
    }
    [Fact] public async Task Referenced_product_error_is_preserved_for_api_conflict_response()
    {
        await Assert.ThrowsAsync<ProductInUseException>(()=>new UpdateProductHandler(new Repository {InUse=true}).Handle(Guid.NewGuid(),new("A","Ad","Tip","Birim"),default));
    }
}
