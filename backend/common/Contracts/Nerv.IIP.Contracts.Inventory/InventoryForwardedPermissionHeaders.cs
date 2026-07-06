using System.Security.Cryptography;
using System.Text;

namespace Nerv.IIP.Contracts.Inventory;

public static class InventoryForwardedPermissionHeaders
{
    public const string PermissionsHeaderName = "X-Nerv-IIP-Permissions";
    public const string IssuerHeaderName = "X-Nerv-IIP-Permissions-Issuer";
    public const string SignatureHeaderName = "X-Nerv-IIP-Permissions-Signature";

    public static string CreateSignature(string signingKey, string issuer, string permissions)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var payload = Encoding.UTF8.GetBytes($"{issuer}\n{permissions}");
        return Base64UrlEncode(hmac.ComputeHash(payload));
    }

    public static bool VerifySignature(string signingKey, string issuer, string permissions, string signature)
    {
        if (string.IsNullOrWhiteSpace(signingKey)
            || string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(permissions)
            || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = CreateSignature(signingKey, issuer, permissions);
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
