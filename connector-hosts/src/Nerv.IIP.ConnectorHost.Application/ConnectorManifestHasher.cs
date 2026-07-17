using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Application;

public static class ConnectorManifestHasher
{
    private static readonly JavaScriptEncoder CanonicalJsonEncoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin);
    private static readonly JsonWriterOptions CanonicalJsonWriterOptions = new()
    {
        Encoder = CanonicalJsonEncoder,
        Indented = false,
        SkipValidation = false,
    };

    public static string Compute(string sourceSystem, IReadOnlyCollection<ConnectorTagManifestEntrySnapshot> entries)
    {
        return Convert.ToHexString(SHA256.HashData(ComputeCanonicalUtf8Bytes(sourceSystem, entries))).ToLowerInvariant();
    }

    public static byte[] ComputeCanonicalUtf8Bytes(
        string sourceSystem,
        IReadOnlyCollection<ConnectorTagManifestEntrySnapshot> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var normalizedSourceSystem = RequiredLower(sourceSystem, nameof(sourceSystem));
        var normalizedEntries = entries
            .Select(entry =>
            {
                ArgumentNullException.ThrowIfNull(entry);
                return new CanonicalEntry(
                    Required(entry.DeviceAssetId, nameof(entry.DeviceAssetId)),
                    RequiredLower(entry.TagKey, nameof(entry.TagKey)),
                    entry.Enabled,
                    OptionalSanitized(entry.ProtocolAddress, 500));
            })
            .OrderBy(entry => entry.DeviceAssetId, StringComparer.Ordinal)
            .ThenBy(entry => entry.TagKey, StringComparer.Ordinal)
            .ToArray();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, CanonicalJsonWriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("sourceSystem", normalizedSourceSystem);
            writer.WritePropertyName("entries");
            writer.WriteStartArray();
            foreach (var entry in normalizedEntries)
            {
                writer.WriteStartObject();
                writer.WriteString("deviceAssetId", entry.DeviceAssetId);
                writer.WriteString("tagKey", entry.TagKey);
                writer.WriteBoolean("enabled", entry.Enabled);
                if (entry.ProtocolAddress is null)
                {
                    writer.WriteNull("protocolAddress");
                }
                else
                {
                    writer.WriteString("protocolAddress", entry.ProtocolAddress);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    internal static ConnectorTagManifestEntrySnapshot Normalize(ConnectorTagManifestEntrySnapshot entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var activationStatus = RequiredLower(entry.ActivationStatus, nameof(entry.ActivationStatus));
        return new ConnectorTagManifestEntrySnapshot(
            Required(entry.DeviceAssetId, nameof(entry.DeviceAssetId)),
            RequiredLower(entry.TagKey, nameof(entry.TagKey)),
            entry.Enabled,
            OptionalSanitized(entry.ProtocolAddress, 500),
            activationStatus,
            entry.ActivationObservedAtUtc.ToUniversalTime(),
            activationStatus == "error" ? OptionalSanitized(entry.ActivationErrorCode, 128) : null,
            activationStatus == "error" ? OptionalSanitized(entry.ActivationErrorMessage, 500) : null);
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();

    private static string RequiredLower(string value, string parameterName) =>
        Required(value, parameterName).ToLowerInvariant();

    private static string? OptionalSanitized(string? value, int maximumLength)
    {
        var optional = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (optional is null)
        {
            return null;
        }

        var sanitized = new string(optional.Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
        if (sanitized.Length == 0)
        {
            return null;
        }

        return sanitized.Length <= maximumLength ? sanitized : sanitized[..maximumLength];
    }

    private sealed record CanonicalEntry(
        string DeviceAssetId,
        string TagKey,
        bool Enabled,
        string? ProtocolAddress);
}
