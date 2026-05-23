namespace Nerv.IIP.Business.IndustrialTelemetry.Domain;

public static class IndustrialTelemetryText
{
    public static string Required(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();
    }

    public static string RequiredLower(string value, string parameterName)
    {
        return Required(value, parameterName).ToLowerInvariant();
    }

    public static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
