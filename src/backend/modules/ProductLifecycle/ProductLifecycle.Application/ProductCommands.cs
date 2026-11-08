using ProductLifecycle.Domain;

namespace ProductLifecycle.Application;

public sealed record CreateProductCommand(string Code, string Name, string Type, string StockUnit,string? MaterialGroupCode=null,IReadOnlyDictionary<string,string>? Attributes=null);
public sealed record ProductDto(Guid Id, string Code, string Name, string Type, string StockUnit, bool IsActive,string? MaterialGroupCode=null,IReadOnlyDictionary<string,string>? Attributes=null);
public sealed class DuplicateProductCodeException : Exception { }
public sealed class ProductInUseException : Exception { }

public interface IProductRepository
{
    Task<Product> CreateAsync(Product product, CancellationToken cancellationToken);
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken);
    Task<Product?> UpdateAsync(Guid id, Product product, CancellationToken cancellationToken) => throw new NotSupportedException();
    Task<Product?> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken) => throw new NotSupportedException();
}

public sealed class CreateProductHandler(IProductRepository repository)
{
    public async Task<ProductDto> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var product = Product.Create(command.Code, command.Name, command.Type, command.StockUnit,command.MaterialGroupCode,command.Attributes);
        var created = await repository.CreateAsync(product, cancellationToken);
        return ToDto(created);
    }

    internal static ProductDto ToDto(Product product) => new(product.Id, product.Code, product.Name, product.Type, product.StockUnit, product.IsActive,product.MaterialGroupCode,product.Attributes);
}

public sealed class ListProductsHandler(IProductRepository repository)
{
    public async Task<IReadOnlyList<ProductDto>> Handle(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).Select(CreateProductHandler.ToDto).ToArray();
}

public sealed class UpdateProductHandler(IProductRepository repository)
{
    public async Task<ProductDto?> Handle(Guid id, CreateProductCommand command, CancellationToken ct)
    {
        var validated = Product.Create(command.Code,command.Name,command.Type,command.StockUnit,command.MaterialGroupCode,command.Attributes);
        var updated = await repository.UpdateAsync(id,validated,ct);
        return updated is null ? null : CreateProductHandler.ToDto(updated);
    }
}

public sealed class SetProductStatusHandler(IProductRepository repository)
{
    public async Task<ProductDto?> Handle(Guid id, bool isActive, CancellationToken ct)
    {
        var product = await repository.SetActiveAsync(id, isActive, ct);
        return product is null ? null : CreateProductHandler.ToDto(product);
    }
}
