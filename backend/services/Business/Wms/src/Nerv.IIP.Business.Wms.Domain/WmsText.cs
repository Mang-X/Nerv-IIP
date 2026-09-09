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
    /// <param name="prefix">单号前缀，会被规范化为大写。</param>
    /// <param name="maxLength">
    /// 构造上界。**必须由 <see cref="WmsOperationalCodePolicy"/> 从承载列宽派生**，不得手抄数字：
    /// 该单号被写进哪几列，上界就是那几列宽度的最小值。
    /// </param>
    /// <param name="parts">参与构造的取值，按顺序以 <c>-</c> 连接。</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 连回落形态（前缀 + 64 位十六进制摘要）都塞不进 <paramref name="maxLength"/> 时就地拒绝，
    /// 而不是把越界值送进数据库换一个 22001。
    /// </exception>
    public static string StableOperationalCode(string prefix, int maxLength, params string[] parts)
    {
        var normalizedPrefix = Required(prefix, nameof(prefix)).ToUpperInvariant();
        var normalizedParts = parts.Select((part, index) => Required(part, $"parts[{index}]")).ToArray();
        var candidate = $"{normalizedPrefix}-{string.Join('-', normalizedParts)}";
        if (candidate.Length <= maxLength)
        {
            return candidate;
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidate))).ToLowerInvariant();
        var fallback = $"{normalizedPrefix}-{hash}";
        if (fallback.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLength),
                maxLength,
                $"前缀 '{normalizedPrefix}' 的稳定单号回落形态需要 {fallback.Length} 个字符，承载列只放得下 {maxLength} 个。");
        }

        return fallback;
    }
}
