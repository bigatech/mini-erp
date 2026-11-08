namespace ProductLifecycle.Domain;

public sealed class Product
{
    private Product(Guid id, string code, string name, string type, string stockUnit,string? materialGroupCode=null,IReadOnlyDictionary<string,string>? attributes=null)
    {
        Id = id;
        Code = code;
        Name = name;
        Type = type;
        StockUnit = stockUnit;
        MaterialGroupCode=string.IsNullOrWhiteSpace(materialGroupCode)?null:Required(materialGroupCode,nameof(materialGroupCode));
        var copy=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var pair in attributes??new Dictionary<string,string>())
        {
            var key=Required(pair.Key,"attributes");var value=pair.Value??string.Empty;
            if(value.Contains('\0')||value.Length>2000)throw new ArgumentException("Öznitelik değeri geçersiz.","attributes");
            copy.Add(key,value);
        }
        if(MaterialGroupCode is null&&copy.Any(p=>!string.IsNullOrWhiteSpace(p.Value)))throw new ArgumentException("Öznitelikler için malzeme grubu gereklidir.",nameof(materialGroupCode));
        Attributes=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(copy);
        IsActive = true;
    }

    public Guid Id { get; }
    public string Code { get; }
    public string Name { get; }
    public string Type { get; }
    public string StockUnit { get; }
    public string? MaterialGroupCode { get; }
    public IReadOnlyDictionary<string,string> Attributes { get; }
    public bool IsActive { get; private set; }

    public static Product Create(string code, string name, string type, string stockUnit,string? materialGroupCode=null,IReadOnlyDictionary<string,string>? attributes=null)
    {
        code = Required(code, nameof(code));
        name = Required(name, nameof(name));
        type = Required(type, nameof(type));
        stockUnit = Required(stockUnit, nameof(stockUnit));
        return new Product(Guid.NewGuid(), code, name, type, stockUnit,materialGroupCode,attributes);
    }

    public static Product Rehydrate(Guid id, string code, string name, string type, string stockUnit, bool isActive = true,string? materialGroupCode=null,IReadOnlyDictionary<string,string>? attributes=null)
    {
        var product = new Product(id, code, name, type, stockUnit,materialGroupCode,attributes);
        product.IsActive = isActive;
        return product;
    }

    private static string Required(string value, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException($"{field} is required.", field);
        // PostgreSQL text values cannot contain U+0000. Reject it before the
        // repository so a valid JSON string cannot turn into a storage error.
        if (normalized.Contains('\0'))
            throw new ArgumentException($"{field} cannot contain a null character.", field);
        return normalized;
    }
}
