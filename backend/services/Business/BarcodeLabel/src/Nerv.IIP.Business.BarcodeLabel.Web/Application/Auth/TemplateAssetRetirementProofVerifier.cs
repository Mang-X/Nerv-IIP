using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;
using Nerv.IIP.Contracts.BarcodeLabel;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Auth;

public sealed class TemplateAssetRetirementProofOptions
{
    public const string SectionName = "TemplateAssetRetirementProof";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SecretBase64 { get; set; } = "";
}

public sealed class TemplateAssetRetirementProofOptionsValidator : IValidateOptions<TemplateAssetRetirementProofOptions>
{
    public ValidateOptionsResult Validate(string? name, TemplateAssetRetirementProofOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
            return ValidateOptionsResult.Fail("TemplateAssetRetirementProof requires a fixed issuer and audience.");
        try
        {
            if (Convert.FromBase64String(options.SecretBase64).Length >= 32)
                return ValidateOptionsResult.Success;
        }
        catch (FormatException) { }
        return ValidateOptionsResult.Fail("TemplateAssetRetirementProof requires a Base64 secret of at least 32 bytes.");
    }
}

public sealed class TemplateAssetRetirementProofVerifier(
    IOptions<TemplateAssetRetirementProofOptions> options, TimeProvider timeProvider)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public string? Verify(RetireTemplateAssetRequest request)
    {
        var pieces = request.Proof.Split('.');
        if (pieces.Length != 2 || !TryDecode(pieces[0], out var payload)
            || !TryDecode(pieces[1], out var signature) || signature.Length != 32)
            return null;

        var settings = options.Value;
        var expected = HMACSHA256.HashData(Convert.FromBase64String(settings.SecretBase64), payload);
        if (!CryptographicOperations.FixedTimeEquals(signature, expected)
            || !TryReadFields(payload, out var fields))
            return null;

        if (fields[0] != TemplateAssetRetirementProofV1.Version.ToString(CultureInfo.InvariantCulture)
            || fields[1] != TemplateAssetRetirementProofV1.Algorithm
            || fields[2] != settings.Issuer || fields[3] != settings.Audience
            || !TryReadSeconds(fields[4], out var issued) || !TryReadSeconds(fields[5], out var expires)
            || string.IsNullOrWhiteSpace(fields[6]) || fields[6].Length > 200
            || fields[7] != TemplateAssetRetirementDecision.RequiredPermission
            || fields[8] != TemplateAssetRetirementProofV1.RequestDigest(request))
            return null;

        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        // decimal 防止攻击者提供 Int64 极值导致时间差溢出；边界在拒绝侧。
        if (expires <= issued || (decimal)expires - issued > 300
            || (decimal)issued >= (decimal)now + 300 || (decimal)expires <= (decimal)now - 300)
            return null;
        return fields[6];
    }

    private static bool TryReadSeconds(string value, out long seconds) =>
        long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out seconds)
        && value == seconds.ToString(CultureInfo.InvariantCulture);

    private static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = [];
        try
        {
            bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/')
                + new string('=', (4 - value.Length % 4) % 4));
            return value == TemplateAssetRetirementProofV1.Base64Url(bytes);
        }
        catch (FormatException) { return false; }
    }

    private static bool TryReadFields(byte[] payload, out string[] fields)
    {
        fields = new string[9];
        var offset = 0;
        try
        {
            for (var index = 0; index < fields.Length; index++)
            {
                var start = offset;
                while (offset < payload.Length && payload[offset] >= '0' && payload[offset] <= '9') offset++;
                if (offset == start || offset >= payload.Length || payload[offset] != ':'
                    || (offset - start > 1 && payload[start] == '0')
                    || !int.TryParse(Encoding.ASCII.GetString(payload, start, offset - start),
                        NumberStyles.None, CultureInfo.InvariantCulture, out var length)) return false;
                offset++;
                if (length > payload.Length - offset) return false;
                fields[index] = StrictUtf8.GetString(payload, offset, length);
                offset += length;
                if (index < fields.Length - 1 && (offset >= payload.Length || payload[offset++] != '\n')) return false;
            }
            return offset == payload.Length;
        }
        catch (DecoderFallbackException) { return false; }
    }
}
