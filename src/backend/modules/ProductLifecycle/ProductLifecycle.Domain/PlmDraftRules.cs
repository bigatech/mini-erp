namespace ProductLifecycle.Domain;

public static class PlmDraftRules
{
    public static string Required(string? value, string field)
    {
        var text=value?.Trim();
        if(string.IsNullOrWhiteSpace(text)||text.Contains('\0')) throw new ArgumentException("Zorunlu alan boş olamaz veya geçersiz karakter içeremez.",field);
        return text;
    }
    public static string Option(string? kind,decimal quantity)
    {
        var result=Required(kind,"kind");
        if(result is not ("variant" or "size" or "set-part")) throw new ArgumentException("Seçenek türü varyant, beden veya set parçası olmalıdır.","kind");
        if(quantity<=0) throw new ArgumentException("Set parça miktarı pozitif olmalıdır.","quantity");
        return result;
    }
    public static void Bom(decimal quantity, decimal wasteRate)
    {
        if(quantity<=0) throw new ArgumentException("Miktar pozitif olmalıdır.",nameof(quantity));
        if(wasteRate<0||wasteRate>1) throw new ArgumentException("Fire oranı 0 ile 1 arasında olmalıdır.",nameof(wasteRate));
    }
}
