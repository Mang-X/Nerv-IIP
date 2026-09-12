using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.Contracts.BarcodeLabel;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed class BusinessGatewayTemplateAssetRetirementProofOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SecretBase64 { get; set; } = "";
}

public sealed class TemplateAssetRetirementProofSigner(
    IOptions<BusinessGatewayTemplateAssetRetirementProofOptions> options, TimeProvider clock)
{
    public RetireTemplateAssetRequest Sign(RetireTemplateAssetRequest request, string subject)
    {
        var settings = options.Value;
        byte[] key;
        try { key = Convert.FromBase64String(settings.SecretBase64); }
        catch (FormatException) { throw InvalidConfiguration(); }
        if (key.Length < 32 || string.IsNullOrWhiteSpace(settings.Issuer)
            || string.IsNullOrWhiteSpace(settings.Audience))
            throw InvalidConfiguration();

        var issued = clock.GetUtcNow().ToUnixTimeSeconds();
        var payload = TemplateAssetRetirementProofV1.EncodePayload(settings.Issuer, settings.Audience,
            issued, issued + 300, subject, BusinessGatewayPermissions.BarcodeTemplateAssetsRetire,
            TemplateAssetRetirementProofV1.RequestDigest(request));
        return request with
        {
            Proof = TemplateAssetRetirementProofV1.Base64Url(payload) + "."
                + TemplateAssetRetirementProofV1.Base64Url(HMACSHA256.HashData(key, payload)),
        };
    }

    private static InvalidOperationException InvalidConfiguration() =>
        new("TemplateAssetRetirementProof requires issuer, audience and a Base64 secret of at least 32 bytes.");
}
