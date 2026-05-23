namespace Nerv.IIP.Business.BarcodeLabel.Domain;

public static class BarcodeLabelText
{
    public static string Required(string value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} cannot be blank.", name)
            : value.Trim();
    }

    public static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string Supported(string value, HashSet<string> supportedValues, string name)
    {
        var normalized = Required(value, name).ToLowerInvariant();
        return supportedValues.Contains(normalized)
            ? normalized
            : throw new ArgumentException($"Unsupported {name} '{value}'.", name);
    }

    public static string CodeToken(string value)
    {
        return new string(Required(value, nameof(value))
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }
}
