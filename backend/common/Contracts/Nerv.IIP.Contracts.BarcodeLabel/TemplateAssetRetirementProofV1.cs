using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Contracts.BarcodeLabel;

/// <summary>
/// 第一跳退役 proof v1：九字段 payload 的 UTF-8 字节长度前缀编码，无 BOM，字段间 LF。
/// wire 为 Base64URL(payload).Base64URL(HMAC-SHA256(payload))，均无 padding。
/// 字段顺序或编码改变必须升级版本；不做 Unicode、大小写或空白归一化。
/// </summary>
public static class TemplateAssetRetirementProofV1
{
    public const int Version = 1;
    public const string Algorithm = "HMAC-SHA256";
    public const string Action = "retire-template-asset";
    public const string Route = "/api/business/v1/barcodes/template-assets/retire";

    public static byte[] EncodePayload(
        string issuer, string audience, long issuedAt, long expiresAt,
        string subject, string permission, string requestDigest) =>
        EncodeFields(Version.ToString(CultureInfo.InvariantCulture), Algorithm, issuer, audience,
            issuedAt.ToString(CultureInfo.InvariantCulture), expiresAt.ToString(CultureInfo.InvariantCulture),
            subject, permission, requestDigest);

    public static string RequestDigest(RetireTemplateAssetRequest request) =>
        Base64Url(SHA256.HashData(EncodeFields(Action, request.OrganizationId, request.EnvironmentId,
            request.TemplateId.ToString("D"), request.FileId, request.Checksum, request.Reason, request.IdempotencyKey)));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] EncodeFields(params string[] values) => Encoding.UTF8.GetBytes(
        string.Join("\n", values.Select(value =>
            Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + ":" + value)));
}

public sealed record RetireTemplateAssetRequest(
    string OrganizationId,
    string EnvironmentId,
    Guid TemplateId,
    string FileId,
    string Checksum,
    string Reason,
    string IdempotencyKey,
    string Proof);

public sealed record RetireTemplateAssetResponse(Guid DecisionId);

public sealed record TemplateAssetRetirementProofError(string Code, string Message);
