namespace Nerv.IIP.Iam.Infrastructure;

public interface IInMemoryIamAccessTokenIssuer
{
    string CreateAccessToken(InMemoryIamAccessTokenIssue issue);

    DateTimeOffset GetAccessTokenExpiresAtUtc(DateTimeOffset issuedAtUtc);
}

public sealed record InMemoryIamAccessTokenIssue(
    string UserId,
    string SessionId,
    string SecurityStamp,
    int PermissionVersion,
    string? LoginName,
    string? Email,
    string? OrganizationId,
    string? EnvironmentId);

internal sealed class UnconfiguredInMemoryIamAccessTokenIssuer : IInMemoryIamAccessTokenIssuer
{
    public string CreateAccessToken(InMemoryIamAccessTokenIssue issue)
    {
        throw new InvalidOperationException("InMemory IAM access token issuance must be configured with the IAM token service.");
    }

    public DateTimeOffset GetAccessTokenExpiresAtUtc(DateTimeOffset issuedAtUtc)
    {
        throw new InvalidOperationException("InMemory IAM access token issuance must be configured with the IAM token service.");
    }
}
