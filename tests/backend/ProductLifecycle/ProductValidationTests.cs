using ProductLifecycle.Application;
using ProductLifecycle.Domain;
using Xunit;

namespace MiniErp.ProductLifecycle.Tests;

public sealed class ProductValidationTests
{
    private sealed class Repository : IProductRepository
    {
        public Product? Saved { get; private set; }
        public Task<Product> CreateAsync(Product product, CancellationToken ct)
        {
            Saved = product;
            return Task.FromResult(product);
        }
        public Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Product>>(Saved is null ? [] : [Saved]);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("name")]
    [InlineData("type")]
    [InlineData("stockUnit")]
    public async Task Null_character_in_any_required_field_is_rejected_before_storage(string field)
    {
        var fields = new Dictionary<string, string>
        {
            ["code"] = "P-01", ["name"] = "Tişört", ["type"] = "Mamul", ["stockUnit"] = "Adet"
        };
        fields[field] = "bad\0value";
        var repository = new Repository();
        var handler = new CreateProductHandler(repository);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new(fields["code"], fields["name"], fields["type"], fields["stockUnit"]), default));

        Assert.Equal(field, error.ParamName);
        Assert.Null(repository.Saved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public async Task Missing_code_does_not_reach_storage(string? code)
    {
        var repository = new Repository();
        var error = await Assert.ThrowsAsync<ArgumentException>(() => new CreateProductHandler(repository)
            .Handle(new(code!, "Tişört", "Mamul", "Adet"), default));
        Assert.Equal("code", error.ParamName);
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task Valid_Turkish_fields_are_trimmed_and_returned_consistently_from_create_and_list()
    {
        var repository = new Repository();
        var created = await new CreateProductHandler(repository).Handle(
            new(" P-01 ", " Gömlek – örme ", " Mamul ", " Adet "), default);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("P-01", created.Code);
        Assert.Equal("Gömlek – örme", created.Name);
        Assert.Equal("Mamul", created.Type);
        Assert.Equal("Adet", created.StockUnit);
        Assert.True(created.IsActive);
        Assert.Equal(created, Assert.Single(await new ListProductsHandler(repository).Handle(default)));
    }

}
