namespace ProductLifecycle.Domain;

public static class ProductionRouteRules
{
    public static string Duration(decimal duration, string? unit)
    {
        if (duration < 0) throw new ArgumentException("Standart süre negatif olamaz.", "standardDuration");
        var value = PlmDraftRules.Required(unit, "durationUnit").ToLowerInvariant();
        if (value is not ("minute" or "second")) throw new ArgumentException("Süre birimi dakika veya saniye olmalıdır.", "durationUnit");
        return value;
    }
    public static void Sequence(int sequence)
    {
        if (sequence <= 0) throw new ArgumentException("Operasyon sıra numarası pozitif olmalıdır.", "sequence");
    }
}
