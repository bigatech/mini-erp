using System.Text.RegularExpressions;

namespace SharedKernel.Application;

public sealed record DefinitionCategory(string Code, string Name, bool IsActive = true, string ModuleCode = "shared-kernel", string FeatureCode = "general");
public sealed record DefinitionFeature(string Code, string Name);
public sealed record DefinitionModule(string ModuleCode, string ModuleName, IReadOnlyList<DefinitionFeature> Features);
public static class DefinitionTargets
{
    // Binding registry, not a declaration that every consuming workflow is implemented.
    // Product fields and variant master data are defined by ERP-07-024.
    public static IReadOnlyList<DefinitionModule> All { get; } =
    [new("product-lifecycle", "Ürün ve Yaşam Döngüsü",
        [new("type", "Ürün tipi"), new("stock-unit", "Stok birimi"),
         new("color", "Varyant rengi (planlı)"), new("size-set", "Beden seti (planlı)"),
         new("model-variant", "Model varyantı"), new("model-size", "Model bedeni")]),
     new("shared-kernel", "Ortak Tanımlar",
        [new("location-type", "Lokasyon tipi (planlı)"), new("party-role", "Taraf rolü (planlı)")])];

    public static (string ModuleCode, string FeatureCode) Validate(string? moduleCode, string? featureCode)
    {
        var module = DefinitionInput.Required(moduleCode, "moduleCode").ToLowerInvariant();
        var feature = DefinitionInput.Required(featureCode, "featureCode").ToLowerInvariant();
        var target = All.SingleOrDefault(m => m.ModuleCode == module)
            ?? throw new ArgumentException("Unknown definition module.", "moduleCode");
        if (!target.Features.Any(f => f.Code == feature))
            throw new ArgumentException("Feature is not a permitted definition target for this module.", "featureCode");
        return (module, feature);
    }
}
public sealed record DefinitionItem(string CategoryCode, string Code, string Name, bool IsActive);
public sealed class DuplicateDefinitionException : Exception { }
public sealed class UnknownDefinitionCategoryException : Exception { }

public static class DefinitionInput
{
    public static string Required(string? value, string field)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0'))
            throw new ArgumentException($"{field} is required and cannot contain a null character.", field);
        return text;
    }
    public static string Category(string? value)
    {
        var code = Required(value, "categoryCode").ToLowerInvariant();
        if (!Regex.IsMatch(code, "^[a-z0-9]+(?:[-_][a-z0-9]+)*$"))
            throw new ArgumentException("Category code must contain letters a-z, digits, hyphens or underscores.", "categoryCode");
        return code;
    }
}

public interface IDefinitionRepository
{
    Task<DefinitionCategory> UpdateCategoryAsync(string code,string name,CancellationToken ct)=>throw new NotSupportedException();
    Task DeleteCategoryAsync(string code,CancellationToken ct)=>throw new NotSupportedException();
    Task<DefinitionItem> UpdateItemAsync(string category,string code,string name,bool active,CancellationToken ct)=>throw new NotSupportedException();
    Task DeleteItemAsync(string category,string code,CancellationToken ct)=>throw new NotSupportedException();
    Task<IReadOnlyList<DefinitionCategory>> CategoriesAsync(CancellationToken ct);
    Task<DefinitionCategory> CreateCategoryAsync(string code, string name, string moduleCode, string featureCode, CancellationToken ct);
    Task<IReadOnlyList<DefinitionItem>> ItemsAsync(string category, CancellationToken ct);
    Task<DefinitionItem> CreateItemAsync(string category, string code, string name, bool active, CancellationToken ct);
}
public sealed class DefinitionInUseException:Exception {}
