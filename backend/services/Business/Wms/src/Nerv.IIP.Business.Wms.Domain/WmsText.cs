namespace Nerv.IIP.Business.Wms.Domain;

public static class WmsText
{
    public static string Required(string value, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName ?? "Value"} is required.", parameterName);
        }

        return value.Trim();
    }

    public static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static decimal Positive(decimal value, string parameterName)
    {
        return value <= 0 ? throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be positive.") : value;
    }

    public static decimal NonZero(decimal value, string parameterName)
    {
        return value == 0 ? throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} cannot be zero.") : value;
    }

    public static string LineIdempotencyKey(string idempotencyKey, string lineNo)
    {
        var candidate = $"{Required(idempotencyKey, nameof(idempotencyKey))}:{Required(lineNo, nameof(lineNo))}";
        if (candidate.Length <= 128)
        {
            return candidate;
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidate))).ToLowerInvariant();
        return $"wms-line:{hash}";
    }

    public static string StableOperationalCode(string prefix, params string[] parts)
    {
        var normalizedPrefix = Required(prefix, nameof(prefix)).ToUpperInvariant();
        var normalizedParts = parts.Select((part, index) => Required(part, $"parts[{index}]")).ToArray();
        var candidate = $"{normalizedPrefix}-{string.Join('-', normalizedParts)}";
        if (candidate.Length <= 100)
        {
            return candidate;
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidate))).ToLowerInvariant();
        return $"{normalizedPrefix}-{hash}";
    }
}
