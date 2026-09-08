using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Domain;

namespace Nerv.IIP.FileStorage.Web.Application.Files;

public sealed record TemplateAssetRetirementOptions(
    byte[] Key, string Issuer, string Audience, RetirementStorageInputs Storage)
{
    public const string Section = "FileStorage:TemplateAssetRetirement";

    public static TemplateAssetRetirementOptions Load(IConfiguration config)
    {
        try
        {
            var key = Convert.FromBase64String(config[$"{Section}:Secret"] ?? "");
            var issuer = config[$"{Section}:Issuer"];
            var audience = config[$"{Section}:Audience"];
            if (key.Length < 32 || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
                throw new ArgumentException();
            var storage = new RetirementStorageInputs(
                config.GetValue<long?>("FileStorage:GarbageCollection:PhysicalDeleteGraceSeconds") ?? 604800,
                config.GetValue<long?>("FileStorage:GarbageCollection:IntervalSeconds") ?? 300,
                config.GetValue<long?>($"{Section}:LeaseSeconds") ?? 300,
                config.GetValue<long?>($"{Section}:MaxBackoffSeconds") ?? 300);
            RetirementReplayPolicy.Resolve(RetirementReplayPolicy.DefaultClientWindowSeconds, 300, 300, storage);
            return new(key, issuer, audience, storage);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            // Configuration parsers may include the rejected value; do not retain an inner exception with secrets.
            throw new InvalidOperationException($"{Section} requires a Base64 secret of at least 32 bytes, issuer/audience, and valid retirement replay inputs.");
        }
    }
}

public sealed class TemplateAssetRetirementProof(TemplateAssetRetirementOptions options, TimeProvider clock)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public RetirementCapability? Verify(RetireTemplateAssetRequest request)
    {
        if (!TryDecodeBase64Url(request.Payload, 8192, out var payload)
            || !TryDecodeBase64Url(request.Signature, 43, out var signature)
            || signature.Length != 32
            || !CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(options.Key, payload), signature))
            return null;

        var fields = ReadFields(payload);
        if (fields is null || fields[0] != "1" || fields[1] != "HMAC-SHA-256"
            || fields[2] != options.Issuer || fields[3] != options.Audience
            || !Integer(fields[4], out var issued) || !Integer(fields[5], out var expires)
            || !Guid.TryParseExact(fields[6], "D", out var decision) || decision == Guid.Empty
            || decision.ToString("D") != fields[6]
            || !Integer(fields[7], out var version) || version != RetirementReplayPolicy.Version
            || !Integer(fields[8], out var window) || window <= 0
            || !Integer(fields[9], out var lease) || lease <= 0
            || !Integer(fields[10], out var backoff) || backoff <= 0
            || (decimal)lease + 2m * backoff > RetirementReplayPolicy.MaximumSeconds
            || !Text(fields[11], 128) || !Text(fields[12], 128) || !Text(fields[13], 64)
            || !Regex.IsMatch(fields[14], "\\Asha256:[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)
            || fields[15] != "business-barcode-label" || fields[16] != "label-template"
            || !Text(fields[17], 128) || fields[18] != "barcode-label-template")
            return null;

        var now = (clock.GetUtcNow() - DateTimeOffset.UnixEpoch).Ticks / (decimal)TimeSpan.TicksPerSecond;
        if ((decimal)expires - issued is <= 0 or > 300 || issued >= now + 300 || expires <= now - 300)
            return null;
        return new(fields[6], fields[11], fields[12], fields[13], fields[14], fields[15], fields[16],
            fields[17], fields[18], version, window, lease, backoff);
    }

    private static bool Text(string value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max;

    private static bool Integer(string value, out long number) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number)
        && number.ToString(CultureInfo.InvariantCulture) == value;

    private static bool TryDecodeBase64Url(string? value, int maxLength, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(value) || value.Length > maxLength
            || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) || value.Length % 4 == 1)
            return false;
        bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
        // Reject alternate encodings with nonzero unused bits.
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') == value;
    }

    private static string[]? ReadFields(byte[] bytes)
    {
        var fields = new string[19];
        var offset = 0;
        try
        {
            for (var field = 0; field < fields.Length; field++)
            {
                var start = offset;
                while (offset < bytes.Length && bytes[offset] is >= (byte)'0' and <= (byte)'9') offset++;
                if (offset == start || offset - start > 4 || offset >= bytes.Length || bytes[offset] != ':'
                    || (offset - start > 1 && bytes[start] == '0')) return null;
                var length = int.Parse(Encoding.ASCII.GetString(bytes, start, offset - start), CultureInfo.InvariantCulture);
                offset++;
                if (length > bytes.Length - offset) return null;
                fields[field] = StrictUtf8.GetString(bytes, offset, length);
                offset += length;
                if (field < fields.Length - 1 && (offset >= bytes.Length || bytes[offset++] != '\n')) return null;
            }
            return offset == bytes.Length ? fields : null;
        }
        catch (DecoderFallbackException) { return null; }
    }
}
