namespace SharedKernel.Domain;

public sealed class Currency
{
    private Currency(string code, string name)
    {
        Code = code;
        Name = name;
    }

    public string Code { get; }
    public string Name { get; }

    public static Currency Create(string code, string name)
    {
        var normalizedCode = code?.Trim();
        // Validate ASCII before casing: Unicode lookalikes must not turn into a
        // different accepted identifier. This checks format, not ISO membership.
        if (normalizedCode is not { Length: 3 } ||
            !normalizedCode.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            throw new ArgumentException("Currency code must contain exactly three ASCII letters.", nameof(code));

        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Currency name is required.", nameof(name));
        if (normalizedName.Contains('\0'))
            throw new ArgumentException("Currency name cannot contain a null character.", nameof(name));

        return new Currency(normalizedCode.ToUpperInvariant(), normalizedName);
    }
}
