using System.Text.RegularExpressions;

namespace Nerv.IIP.Testing;

public static partial class TestDiagnostic
{
    private const string Redacted = "[REDACTED]";

    public static string Sanitize(
        string? diagnostic,
        IReadOnlyCollection<string?>? sensitiveValues = null)
    {
        var sanitized = diagnostic ?? string.Empty;

        if (sensitiveValues is not null)
        {
            foreach (var sensitiveValue in sensitiveValues)
            {
                if (!string.IsNullOrEmpty(sensitiveValue))
                {
                    sanitized = sanitized.Replace(sensitiveValue, Redacted, StringComparison.Ordinal);
                }
            }
        }

        sanitized = ConnectionStringRegex().Replace(
            sanitized,
            match => $"{match.Groups["key"].Value}{match.Groups["separator"].Value}{Redacted}");
        sanitized = RequestMaterialRegex().Replace(
            sanitized,
            match => $"{match.Groups["key"].Value}{match.Groups["separator"].Value}{Redacted}");

        return SensitiveKeyRegex().Replace(
            sanitized,
            match => $"{match.Groups["key"].Value}{match.Groups["separator"].Value}{Redacted}");
    }

    [GeneratedRegex(
        @"(?<key>connectionstring)(?<separator>\s*[:=]\s*)(?<value>[^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringRegex();

    [GeneratedRegex(
        @"(?<key>headers?|requestbody|body)(?<separator>\s*:\s*)(?<value>[^;\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequestMaterialRegex();

    [GeneratedRegex(
        @"(?<key>password|secret|token|credential|apikey|api_key)(?<separator>\s*[:=]\s*)(?<value>""[^""]*""|'[^']*'|[^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKeyRegex();
}
