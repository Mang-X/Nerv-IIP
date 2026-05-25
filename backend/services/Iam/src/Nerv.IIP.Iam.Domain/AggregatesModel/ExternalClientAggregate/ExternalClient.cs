using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.ExternalClientAggregate;

public partial record ExternalClientId : IStringStronglyTypedId;
public partial record AuthorizationGrantId : IStringStronglyTypedId;

public class ExternalClient : Entity<ExternalClientId>, IAggregateRoot
{
    private ExternalClient()
    {
        Id = new ExternalClientId(string.Empty);
        OrganizationId = new OrganizationId(string.Empty);
        EnvironmentId = new IamEnvironmentId(string.Empty);
    }

    public ExternalClient(
        ExternalClientId id,
        string clientId,
        string displayName,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        string secretHash,
        bool enabled,
        int permissionVersion,
        DateTimeOffset validFromUtc,
        DateTimeOffset? validToUtc)
    {
        Id = id;
        ClientId = clientId;
        DisplayName = displayName;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        SecretHash = secretHash;
        Enabled = enabled;
        PermissionVersion = permissionVersion;
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
    }

    public string ClientId { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public OrganizationId OrganizationId { get; private set; }
    public IamEnvironmentId EnvironmentId { get; private set; }
    public string SecretHash { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public int PermissionVersion { get; private set; }
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset? ValidToUtc { get; private set; }

    public bool CanAuthenticate(string secretHash, DateTimeOffset now)
    {
        return Enabled
            && string.Equals(SecretHash, secretHash, StringComparison.Ordinal)
            && ValidFromUtc <= now
            && (ValidToUtc is null || ValidToUtc > now);
    }

    public void ReplaceSecretHash(string secretHash)
    {
        SecretHash = secretHash;
        PermissionVersion += 1;
    }
}

public class AuthorizationGrant : Entity<AuthorizationGrantId>, IAggregateRoot
{
    private AuthorizationGrant()
    {
        Id = new AuthorizationGrantId(string.Empty);
        OrganizationId = new OrganizationId(string.Empty);
        EnvironmentId = new IamEnvironmentId(string.Empty);
    }

    public AuthorizationGrant(
        AuthorizationGrantId id,
        string principalType,
        string principalId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        string permissionCode,
        DateTimeOffset validFromUtc,
        DateTimeOffset? validToUtc)
    {
        Id = id;
        PrincipalType = principalType;
        PrincipalId = principalId;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        PermissionCode = permissionCode;
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
    }

    public string PrincipalType { get; private set; } = string.Empty;
    public string PrincipalId { get; private set; } = string.Empty;
    public OrganizationId OrganizationId { get; private set; }
    public IamEnvironmentId EnvironmentId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;
    public DateTimeOffset ValidFromUtc { get; private set; }
    public DateTimeOffset? ValidToUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActiveAt(DateTimeOffset now)
    {
        return ValidFromUtc <= now
            && (ValidToUtc is null || ValidToUtc > now)
            && RevokedAtUtc is null;
    }
}
