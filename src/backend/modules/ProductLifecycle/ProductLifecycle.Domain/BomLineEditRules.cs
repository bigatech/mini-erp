namespace ProductLifecycle.Domain;
public static class BomLineEditRules
{
    public static string? Label(string? label)
    {
        var value=label?.Trim();
        if(string.IsNullOrEmpty(value))return null;
        if(value.Length>200||value.Contains('\0'))throw new ArgumentException("Ayırt edici tanım en fazla 200 karakter olabilir; geçersiz karakter içeremez.","label");
        return value;
    }
}
