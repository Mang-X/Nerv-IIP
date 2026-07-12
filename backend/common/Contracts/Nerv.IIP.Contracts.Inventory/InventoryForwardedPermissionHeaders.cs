using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Contracts.Inventory;

public static class InventoryForwardedPermissionHeaders
{
    public const string PermissionsHeaderName = "X-Nerv-IIP-Permissions";
    public const string IssuerHeaderName = "X-Nerv-IIP-Permissions-Issuer";
    public const string OrganizationHeaderName = "X-Nerv-IIP-Permissions-Organization";
    public const string EnvironmentHeaderName = "X-Nerv-IIP-Permissions-Environment";
    public const string RequestKeyHeaderName = "X-Nerv-IIP-Permissions-Request-Key";
    public const string IssuedAtHeaderName = "X-Nerv-IIP-Permissions-Issued-At";
    public const string SignatureHeaderName = "X-Nerv-IIP-Permissions-Signature";

    public static string CreateSignature(
        string signingKey,
        string issuer,
        string permissions,
        string organizationId,
        string environmentId,
        string requestKey,
        long issuedAtUnixSeconds)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var payload = Encoding.UTF8.GetBytes(
            $"{issuer}\n{permissions}\n{organizationId}\n{environmentId}\n{requestKey}\n{issuedAtUnixSeconds}");
        return Base64UrlEncode(hmac.ComputeHash(payload));
    }

    public static bool VerifySignature(
        string signingKey,
        string issuer,
        string permissions,
        string organizationId,
        string environmentId,
        string requestKey,
        long issuedAtUnixSeconds,
        string signature)
    {
        if (string.IsNullOrWhiteSpace(signingKey)
            || string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(permissions)
            || string.IsNullOrWhiteSpace(organizationId)
            || string.IsNullOrWhiteSpace(environmentId)
            || string.IsNullOrWhiteSpace(requestKey)
            || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = CreateSignature(
            signingKey,
            issuer,
            permissions,
            organizationId,
            environmentId,
            requestKey,
            issuedAtUnixSeconds);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var signatureBytes = Encoding.ASCII.GetBytes(signature);
        return expectedBytes.Length == signatureBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, signatureBytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
