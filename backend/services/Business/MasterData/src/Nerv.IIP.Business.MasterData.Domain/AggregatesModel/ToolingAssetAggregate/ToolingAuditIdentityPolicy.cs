namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

using System.Data.Common;
using System.Text.Json;

public static class ToolingAuditIdentityPolicy
{
    public const int MaxLength = 200;
    private const string UserActorPrefix = "user:";
    private static readonly string[] SensitiveMarkers =
    [
        "bearer",
        "password",
        "passwd",
        "secret",
        "authorization",
        "connection-string",
        "connectionstring",
    ];
    private static readonly HashSet<string> ConnectionEndpointKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "host",
        "server",
        "data source",
        "address",
        "addr",
        "network address",
    };
    private static readonly HashSet<string> ConnectionContextKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "database",
        "initial catalog",
        "username",
        "user id",
        "uid",
        "port",
        "integrated security",
        "trusted_connection",
        "ssl mode",
    };

    public static bool IsValidActor(
        string? value,
        IReadOnlyCollection<string>? forbiddenCredentials = null) =>
        value is not null &&
        value.Length > UserActorPrefix.Length &&
        value.Length <= MaxLength &&
        value.StartsWith(UserActorPrefix, StringComparison.Ordinal) &&
        IsCanonicalToken(value.AsSpan(UserActorPrefix.Length)) &&
        !ContainsSensitiveContent(value, forbiddenCredentials);

    public static bool IsValidOpaqueIdentity(
        string? value,
        IReadOnlyCollection<string>? forbiddenCredentials = null) =>
        value is not null &&
        value.Length is > 0 and <= MaxLength &&
        IsCanonicalToken(value.AsSpan()) &&
        !ContainsSensitiveContent(value, forbiddenCredentials);

    public static bool IsValidAuditText(
        string? value,
        IReadOnlyCollection<string>? forbiddenCredentials = null) =>
        !string.IsNullOrWhiteSpace(value) &&
        !ContainsSensitiveContent(value, forbiddenCredentials);

    private static bool IsCanonicalToken(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || !IsAsciiAlphaNumeric(value[0])) return false;
        foreach (var character in value)
        {
            if (IsAsciiAlphaNumeric(character) || character is '-' or '_' or '.' or '/') continue;
            return false;
        }

        return true;
    }

    private static bool IsAsciiAlphaNumeric(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool ContainsSensitiveContent(
        string value,
        IReadOnlyCollection<string>? forbiddenCredentials) =>
        SensitiveMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
        ContainsCompactJwt(value) ||
        ContainsConnectionString(value) ||
        (forbiddenCredentials?.Any(credential =>
            !string.IsNullOrEmpty(credential) &&
            value.Contains(credential, StringComparison.Ordinal)) == true);

    private static bool ContainsConnectionString(string value)
    {
        if (!value.Contains(';') || !value.Contains('=')) return false;

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = value };
            var keys = builder.Keys.Cast<string>().ToArray();
            return keys.Any(ConnectionEndpointKeys.Contains) &&
                keys.Any(ConnectionContextKeys.Contains);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool ContainsCompactJwt(string value)
    {
        char[] separators = [' ', '\t', '\r', '\n', ':', '=', '"', '\'', ',', ';', '(', ')', '[', ']', '<', '>'];
        return value.Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Any(IsCompactJwt);
    }

    private static bool IsCompactJwt(string candidate)
    {
        var segments = candidate.Split('.');
        if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty)) return false;
        try
        {
            var headerBytes = Convert.FromBase64String(ToBase64(segments[0]));
            using var header = JsonDocument.Parse(headerBytes);
            return header.RootElement.ValueKind == JsonValueKind.Object &&
                header.RootElement.TryGetProperty("alg", out _);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ToBase64(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
    }
}
