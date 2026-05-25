namespace Nerv.IIP.Iam.Web.Application.Seed;

public sealed class IamSeedOptions
{
    public bool Enabled { get; init; }
    public string OrganizationId { get; init; } = "org-001";
    public string OrganizationName { get; init; } = "Nerv IIP";
    public string EnvironmentId { get; init; } = "env-dev";
    public string EnvironmentName { get; init; } = "Development";
    public string AdminUserId { get; init; } = "user-admin";
    public string AdminLoginName { get; init; } = "admin";
    public string AdminEmail { get; init; } = "admin@nerv-iip.local";
    public string AdminPassword { get; init; } = string.Empty;
    public string AdminRoleId { get; init; } = "role-platform-admin";
    public string ConnectorHostCredentialId { get; init; } = "credential-connector-host-001";
    public string ConnectorHostId { get; init; } = "connector-host-001";
    public string ConnectorHostSecret { get; init; } = string.Empty;
    public string ExternalClientId { get; init; } = "external-client-demo";
    public string ExternalClientDisplayName { get; init; } = "Demo External Client";
    public string ExternalClientSecret { get; init; } = string.Empty;
    public string[] ExternalClientPermissionCodes { get; init; } = ["ops.tasks.create"];
}
