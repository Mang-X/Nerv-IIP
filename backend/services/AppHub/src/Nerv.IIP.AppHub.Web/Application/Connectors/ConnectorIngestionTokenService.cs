using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Web.Application.Connectors;

public sealed record ConnectorIngestionIdentity(
    string RegistrationId,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string InstanceKey)
{
    public ConnectorRequestContext Bind(ConnectorRequestContext context) =>
        context with
        {
            OrganizationId = OrganizationId,
            EnvironmentId = EnvironmentId,
            ConnectorHostId = ConnectorHostId
        };

    public bool Matches(ConnectorRequestContext context, string instanceKey) =>
        string.Equals(OrganizationId, context.OrganizationId, StringComparison.Ordinal)
        && string.Equals(EnvironmentId, context.EnvironmentId, StringComparison.Ordinal)
        && string.Equals(ConnectorHostId, context.ConnectorHostId, StringComparison.Ordinal)
        && string.Equals(InstanceKey, instanceKey, StringComparison.Ordinal);
}

public interface IConnectorIngestionTokenService
{
    string CreateToken(ApplicationRegistration registration, string registrationId);
    bool TryValidateToken(string token, out ConnectorIngestionIdentity identity);
}

public sealed class ConnectorIngestionTokenService : IConnectorIngestionTokenService
{
    private const string Version = "v1";
    private const string DevelopmentSigningKey = "local-apphub-connector-ingestion-token-signing-key";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _signingKey;

    public ConnectorIngestionTokenService(IConfiguration configuration, IHostEnvironment environment)
    {
        var signingKey = configuration["ConnectorIngestionToken:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("ConnectorIngestionToken:SigningKey is required outside Development.");
            }

            signingKey = DevelopmentSigningKey;
        }

        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException("ConnectorIngestionToken:SigningKey must be at least 32 bytes.");
        }

        _signingKey = Encoding.UTF8.GetBytes(signingKey);
    }

    public string CreateToken(ApplicationRegistration registration, string registrationId)
    {
        var identity = new ConnectorIngestionIdentity(
            registrationId,
            registration.Context.OrganizationId,
            registration.Context.EnvironmentId,
            registration.Context.ConnectorHostId,
            registration.InstanceKey);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(identity, SerializerOptions);
        var payload = Base64UrlEncode(payloadBytes);
        var signature = Base64UrlEncode(Sign(Encoding.UTF8.GetBytes($"{Version}.{payload}")));

        return $"{Version}.{payload}.{signature}";
    }

    public bool TryValidateToken(string token, out ConnectorIngestionIdentity identity)
    {
        identity = null!;
        var parts = token.Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
        {
            return false;
        }

        var signedBytes = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var expectedSignature = Sign(signedBytes);
        byte[] actualSignature;
        try
        {
            actualSignature = Base64UrlDecode(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (actualSignature.Length != expectedSignature.Length
            || !CryptographicOperations.FixedTimeEquals(actualSignature, expectedSignature))
        {
            return false;
        }

        try
        {
            identity = JsonSerializer.Deserialize<ConnectorIngestionIdentity>(Base64UrlDecode(parts[1]), SerializerOptions)!;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(identity.RegistrationId)
            && !string.IsNullOrWhiteSpace(identity.OrganizationId)
            && !string.IsNullOrWhiteSpace(identity.EnvironmentId)
            && !string.IsNullOrWhiteSpace(identity.ConnectorHostId)
            && !string.IsNullOrWhiteSpace(identity.InstanceKey);
    }

    private byte[] Sign(byte[] data)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return hmac.ComputeHash(data);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
