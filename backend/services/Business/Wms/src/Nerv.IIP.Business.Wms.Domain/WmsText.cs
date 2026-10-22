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

    public static string IdempotencyKey(string idempotencyKey)
    {
        var normalized = Required(idempotencyKey, nameof(idempotencyKey));
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return $"wms-key-v2:{hash}";
    }

    public static IReadOnlyList<string> ReplayIdempotencyKeys(string idempotencyKey)
    {
        var normalized = Required(idempotencyKey, nameof(idempotencyKey));
        var current = IdempotencyKey(normalized);
        return string.Equals(current, normalized, StringComparison.Ordinal)
            ? [current]
            : [current, normalized];
    }

    public static IReadOnlyList<string> ReplayLineIdempotencyKeys(string idempotencyKey, string lineNo)
    {
        return ReplayIdempotencyKeys(idempotencyKey)
            .Select(key => LineIdempotencyKey(key, lineNo))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// 有界且确定性的运营单号构造：短取值原样可读，超界回落到 <c>{前缀}-{sha256}</c>，
    /// 同样的输入永远得到同样的单号（幂等重放据此复算）。
    /// </summary>
    /// <param name="kind">
    /// 单号种类。前缀与上界由 <see cref="WmsOperationalCodeKind"/> 绑成一个值，上界从该类单号的
    /// 承载列宽派生——本方法**收不到裸 <c>int</c>**，因此「前缀与上界配错」在类型上不可表达。
    /// </param>
    /// <param name="parts">参与构造的取值，按顺序以 <c>-</c> 连接。</param>
    public static string StableOperationalCode(WmsOperationalCodeKind kind, params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(kind);
        var normalizedParts = parts.Select((part, index) => Required(part, $"parts[{index}]")).ToArray();
        var candidate = $"{kind.Prefix}-{string.Join('-', normalizedParts)}";
        if (candidate.Length <= kind.MaxLength)
        {
            return candidate;
        }

        // 回落形态一定塞得下：WmsOperationalCodeKind 的构造函数已经拒绝了放不下它的承载列清单。
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidate))).ToLowerInvariant();
        return $"{kind.Prefix}-{hash}";
    }
}
