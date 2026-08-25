namespace Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

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

    public static bool IsValidActor(string? value) =>
        value is not null &&
        value.Length > UserActorPrefix.Length &&
        value.Length <= MaxLength &&
        value.StartsWith(UserActorPrefix, StringComparison.Ordinal) &&
        IsCanonicalToken(value.AsSpan(UserActorPrefix.Length)) &&
        !ContainsSensitiveMarker(value);

    public static bool IsValidOpaqueIdentity(string? value) =>
        value is not null &&
        value.Length is > 0 and <= MaxLength &&
        IsCanonicalToken(value.AsSpan()) &&
        !ContainsSensitiveMarker(value);

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

    private static bool ContainsSensitiveMarker(string value) => SensitiveMarkers.Any(marker =>
        value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
