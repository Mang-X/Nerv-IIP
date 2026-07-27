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

    /// <summary>
    /// 领导演示 PDA 工人账号的统一登录口令。为空 = 不开通（默认）；只允许来自当前进程
    /// 环境变量注入（<c>Iam__Seed__DemoWorkerPassword</c>），禁止写入仓库或配置文件。
    /// </summary>
    public string DemoWorkerPassword { get; init; } = string.Empty;
    public string AdminRoleId { get; init; } = "role-platform-admin";
    public string ConnectorHostCredentialId { get; init; } = "credential-connector-host-001";
    public string ConnectorHostId { get; init; } = "connector-host-001";
    public string ConnectorHostSecret { get; init; } = string.Empty;
    public string ExternalClientId { get; init; } = "external-client-demo";
    public string ExternalClientDisplayName { get; init; } = "Demo External Client";
    public string ExternalClientSecret { get; init; } = string.Empty;
    public string[] ExternalClientPermissionCodes { get; init; } = ["ops.tasks.create"];
}
