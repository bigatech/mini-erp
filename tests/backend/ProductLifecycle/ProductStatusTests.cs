using ProductLifecycle.Application;
using ProductLifecycle.Domain;
using Xunit;

namespace MiniErp.ProductLifecycle.Tests;

public sealed class ProductStatusTests
{
    private sealed class Repository : IProductRepository
    {
        public bool Missing { get; init; }
        public bool InUse { get; init; }
        public Task<Product> CreateAsync(Product product, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<Product?> SetActiveAsync(Guid id, bool active, CancellationToken ct)
        {
            if (InUse) throw new ProductInUseException();
            return Task.FromResult(Missing ? null : Product.Rehydrate(id, "A", "Ürün", "Mamül", "Adet", active));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Status_change_preserves_product_identity_and_card_fields(bool active)
    {
        var id = Guid.NewGuid();
        var result = await new SetProductStatusHandler(new Repository()).Handle(id, active, default);
        Assert.NotNull(result);
        Assert.Equal(id, result.Id);
        Assert.Equal(active, result.IsActive);
        Assert.Equal("A", result.Code);
        Assert.Equal("Ürün", result.Name);
        Assert.Equal("Mamül", result.Type);
        Assert.Equal("Adet", result.StockUnit);
    }

    [Fact]
    public async Task Missing_product_status_change_returns_no_product() =>
        Assert.Null(await new SetProductStatusHandler(new Repository { Missing = true }).Handle(Guid.NewGuid(), false, default));

    [Fact]
    public async Task Status_change_preserves_in_use_error_for_api_conflict() =>
        await Assert.ThrowsAsync<ProductInUseException>(() => new SetProductStatusHandler(new Repository { InUse = true }).Handle(Guid.NewGuid(), false, default));
}
