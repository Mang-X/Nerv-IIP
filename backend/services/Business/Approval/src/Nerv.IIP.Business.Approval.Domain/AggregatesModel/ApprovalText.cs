namespace Nerv.IIP.Business.Approval.Domain.AggregatesModel;

internal static class ApprovalText
{
    public static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be blank.", nameof(value))
            : value.Trim();
    }

    public static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string RequiredLower(string value)
    {
        return Required(value).ToLowerInvariant();
    }

    public static string Supported(string value, IReadOnlySet<string> supportedValues, string parameterName)
    {
        var normalized = RequiredLower(value);
        return supportedValues.Contains(normalized)
            ? normalized
            : throw new ArgumentException($"Unsupported value '{value}'.", parameterName);
    }
}
