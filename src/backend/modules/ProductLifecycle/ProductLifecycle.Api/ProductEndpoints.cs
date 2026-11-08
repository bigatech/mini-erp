using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ProductLifecycle.Application;
using SharedKernel.Application;

namespace ProductLifecycle.Api;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var products = endpoints.MapGroup("/api/v1/plm/products").WithTags("ProductLifecycle");
        products.MapGet("", async (ListProductsHandler handler, CancellationToken ct) =>
            Results.Ok(new { data = await handler.Handle(ct), meta = new { } }));
        products.MapPost("", async (CreateProductRequest request, CreateProductHandler handler, IDefinitionRepository definitions, IMaterialAttributeCatalog materialAttributes, CancellationToken ct) =>
        {
            try
            {
                var errors = new Dictionary<string, string[]>();
                var types = await definitions.ItemsAsync("product-type", ct);
                var units = await definitions.ItemsAsync("stock-unit", ct);
                if (!types.Any(item => item.IsActive && string.Equals(item.Name, request.Type?.Trim(), StringComparison.Ordinal)))
                    errors["type"] = ["Select an active configured product type."];
                if (!units.Any(item => item.IsActive && string.Equals(item.Name, request.StockUnit?.Trim(), StringComparison.Ordinal)))
                    errors["stockUnit"] = ["Select an active configured stock unit."];
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                await materialAttributes.ValidateAsync(request.MaterialGroupCode,request.Attributes,ct);
                var product = await handler.Handle(new(request.Code, request.Name, request.Type, request.StockUnit,request.MaterialGroupCode,request.Attributes), ct);
                return Results.Created($"/api/v1/plm/products/{product.Id}", product);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [exception.ParamName ?? "product"] = [exception.Message] });
            }
            catch (DuplicateProductCodeException)
            {
                return Results.Conflict(new { title = "Product code already exists", code = "duplicate_product_code" });
            }
        }).Produces<ProductDto>(StatusCodes.Status201Created).ProducesValidationProblem();
        products.MapPut("/{id:guid}",async (Guid id,CreateProductRequest request,UpdateProductHandler handler,IDefinitionRepository definitions,IMaterialAttributeCatalog materialAttributes,CancellationToken ct) =>
        {
            try
            {
                var errors=new Dictionary<string,string[]>();
                var types=await definitions.ItemsAsync("product-type",ct);
                var units=await definitions.ItemsAsync("stock-unit",ct);
                if(!types.Any(item=>item.IsActive && string.Equals(item.Name,request.Type?.Trim(),StringComparison.Ordinal)))
                    errors["type"]=["Select an active configured product type."];
                if(!units.Any(item=>item.IsActive && string.Equals(item.Name,request.StockUnit?.Trim(),StringComparison.Ordinal)))
                    errors["stockUnit"]=["Select an active configured stock unit."];
                if(errors.Count>0) return Results.ValidationProblem(errors);
                await materialAttributes.ValidateAsync(request.MaterialGroupCode,request.Attributes,ct);
                var product=await handler.Handle(id,new(request.Code,request.Name,request.Type,request.StockUnit,request.MaterialGroupCode,request.Attributes),ct);
                return product is null ? Results.NotFound() : Results.Ok(product);
            }
            catch(ArgumentException ex) {return Results.ValidationProblem(new Dictionary<string,string[]> {[ex.ParamName??"product"]=[ex.Message]});}
            catch(DuplicateProductCodeException) {return Results.Conflict(new {title="Product code already exists",code="duplicate_product_code"});}
            catch(ProductInUseException) {return Results.Conflict(new {title="Product is referenced by existing records and cannot be changed",code="product_in_use"});}
        }).Produces<ProductDto>().ProducesValidationProblem();
        products.MapPatch("/{id:guid}/status", async (Guid id, ProductStatusRequest request, SetProductStatusHandler handler, CancellationToken ct) =>
        {
            if (request.IsActive is null)
                return Results.ValidationProblem(new Dictionary<string,string[]> { ["isActive"] = ["isActive is required."] });
            try
            {
                var product = await handler.Handle(id, request.IsActive.Value, ct);
                return product is null ? Results.NotFound() : Results.Ok(product);
            }
            catch (ProductInUseException)
            {
                return Results.Conflict(new { title = "Product is referenced by existing records and cannot be changed", code = "product_in_use" });
            }
        }).Produces<ProductDto>().ProducesValidationProblem();
        return endpoints;
    }

    public sealed record CreateProductRequest(string Code, string Name, string Type, string StockUnit,string? MaterialGroupCode=null,IReadOnlyDictionary<string,string>? Attributes=null);
    public sealed record ProductStatusRequest(bool? IsActive);
}
