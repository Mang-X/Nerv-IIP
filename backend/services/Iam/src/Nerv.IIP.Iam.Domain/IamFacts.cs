namespace Nerv.IIP.Iam.Domain;

public sealed record OrganizationFact(string OrganizationId, string Name, string Status);
public sealed record IamEnvironmentFact(string EnvironmentId, string OrganizationId, string Name, string Status);
public sealed record UserFact(string UserId, string LoginName, string Email, string PasswordHash, bool Enabled, string SecurityStamp, int PermissionVersion);
public sealed record RoleFact(string RoleId, string RoleName, IReadOnlySet<string> PermissionCodes);
public sealed record MembershipFact(string UserId, string OrganizationId, string EnvironmentId, IReadOnlySet<string> RoleIds);
public sealed record UserSessionFact(string SessionId, string UserId, string RefreshTokenHash, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset? RevokedAtUtc, int PermissionVersion);
public sealed record ConnectorHostCredentialFact(string ConnectorHostId, string OrganizationId, string EnvironmentId, IReadOnlySet<string> CapabilityScope, string SecretHash, DateTimeOffset ValidFromUtc, DateTimeOffset? ValidToUtc);

public static class NervIipSeedPermissions
{
    public static readonly string[] All =
    [
        "iam.users.read",
        "iam.users.manage",
        "iam.roles.read",
        "iam.roles.manage",
        "iam.sessions.read",
        "iam.sessions.revoke",
        "connectors.registrations.write",
        "connectors.heartbeats.write",
        "connectors.state-snapshots.write",
        "apphub.instances.read",
        "files.upload",
        "files.read",
        "files.download-grants.create",
        "files.archive",
        "ops.tasks.create",
        "ops.tasks.read",
        "ops.results.write",
        "ops.audit.read",
        "business.masterdata.products.read",
        "business.masterdata.products.manage",
        "business.masterdata.partners.read",
        "business.masterdata.partners.manage",
        "business.masterdata.resources.read",
        "business.masterdata.resources.manage",
        "business.quality.inspection-plans.manage",
        "business.quality.inspection-records.create",
        "business.quality.inspection-records.read",
        "business.quality.ncr.read",
        "business.quality.ncr.manage",
        "notifications.intents.submit",
        "notifications.messages.read",
        "notifications.messages.mark-read",
        "notifications.tasks.read"
    ];
}
