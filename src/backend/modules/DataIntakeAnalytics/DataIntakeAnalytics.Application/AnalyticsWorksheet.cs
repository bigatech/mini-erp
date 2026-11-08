using System.Text.Json;

namespace DataIntakeAnalytics.Application;

public sealed record WorksheetInputs(decimal? ActualProduced, decimal? QualityChecked, decimal? Defects,
    decimal? Shipped, decimal? WorkedHours, decimal? FabricUsedKg, decimal? UnitCost, string? Currency, string? Notes,
    IReadOnlyDictionary<string, string>? FieldValues = null)
{
    public string? Validate()
    {
        decimal?[] values = [ActualProduced, QualityChecked, Defects, Shipped, WorkedHours, FabricUsedKg, UnitCost];
        if (values.Any(v => v is < 0)) return "Değerler negatif olamaz; bilinmeyen alanları boş bırakın.";
        decimal?[] counts = [ActualProduced, QualityChecked, Defects, Shipped];
        if (counts.Any(v => v.HasValue && decimal.Truncate(v.Value) != v.Value)) return "Adet alanları tam sayı olmalıdır.";
        if ((Notes?.Length ?? 0) > 4000) return "Not en çok 4000 karakter olabilir.";
        if (FieldValues is { Count: > 200 } || FieldValues?.Any(pair => pair.Key.Length > 200 || pair.Value is null || pair.Value.Length > 1000) == true)
            return "En çok 200 belge alanı, alan başına en çok 1000 karakter girilebilir.";
        if (!string.IsNullOrEmpty(Currency) && (Currency.Length != 3 || Currency.Any(c => c is < 'A' or > 'Z')))
            return "Para birimini üç büyük harfle girin (örneğin TRY).";
        return null;
    }
}
public sealed record AnalyticsWorksheet(long Version, JsonElement Source, WorksheetInputs Inputs, DateTimeOffset? UpdatedAt);
public sealed record SaveWorksheet(long Version, WorksheetInputs? Inputs);
public sealed record WorksheetAsset(byte[] Content, string MediaType, string FileName);
public interface IAnalyticsWorksheetStore
{
    Task<AnalyticsWorksheet?> GetAsync(CancellationToken ct);
    Task<AnalyticsWorksheet?> SaveAsync(long version, WorksheetInputs inputs, CancellationToken ct);
    Task<WorksheetAsset?> GetAssetAsync(bool excel, CancellationToken ct);
}
