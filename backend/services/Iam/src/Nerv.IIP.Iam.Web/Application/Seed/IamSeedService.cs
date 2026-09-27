using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.ConnectorHostCredentialAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.ExternalClientAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Application.Seed;

public sealed class IamSeedService(
    IServiceProvider serviceProvider,
    IOptions<IamSeedOptions> options,
    IamPasswordService passwordService,
    IamTokenService tokenService)
{
    private const string ErpFinanceRoleId = "role-erp-finance";

    // #3838 之前平台管理员的默认角色名。只用来判断「运营没改过名」，角色本身按角色码查找。
    private const string LegacyPlatformAdministratorRoleName = "Platform Administrator";
    private static readonly SeedManifestId AdminRoleNameManifestId = new("iam-platform-admin-role-name-zh:v1");

    // #3827 之前财务专员的默认权限；只有仍等于这一版的存量角色才补维修工单只读。
    private static readonly string[] ErpFinanceDefaultPermissionsBeforeMaintenanceRead =
    [
        NervIipPermissionCodes.MasterDataResourcesRead,
        NervIipPermissionCodes.ErpProcurementRead,
        NervIipPermissionCodes.ErpSalesRead,
        NervIipPermissionCodes.ErpFinanceRead,
        NervIipPermissionCodes.ErpFinanceManage,
    ];

    /// <summary>
    /// 非 Development 启动时的平台引导：只补缺最高权限管理员及其默认组织/环境、平台管理员角色与成员关系，
    /// 不覆盖已存在的行。组织/环境/管理员/角色 id 读 <c>Iam:Seed:*</c>（与产品基线 seed 同源）。
    /// 新建管理员时初始口令只来自部署配置 <c>Iam:Seed:AdminPassword</c>，须满足口令策略，并标记首次登录须改密。
    /// 连接器凭据、外部客户端、ERP 岗位角色与演示账号不在此列。
    /// </summary>
    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var seed = options.Value;
        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var passwordPolicy = serviceProvider.GetRequiredService<IamPasswordPolicy>();
        await EnsurePlatformAdministratorAsync(dbContext, seed, () =>
        {
            if (string.IsNullOrWhiteSpace(seed.AdminPassword))
            {
                throw new InvalidOperationException(
                    "Iam:Seed:AdminPassword is required to create the initial platform administrator.");
            }

            passwordPolicy.ValidateComplexity(seed.AdminPassword);
            var now = DateTimeOffset.UtcNow;
            return NewAdministrator(
                seed,
                passwordChangedAtUtc: now,
                passwordExpiresAtUtc: passwordPolicy.GetPasswordExpiresAtUtc(now),
                passwordChangeRequired: true);
        }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seed = options.Value;
        if (!seed.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(seed.AdminPassword))
        {
            throw new InvalidOperationException("Iam:Seed:AdminPassword is required when IAM seed is enabled.");
        }

        if (string.IsNullOrWhiteSpace(seed.ConnectorHostSecret))
        {
            throw new InvalidOperationException("Iam:Seed:ConnectorHostSecret is required when IAM seed is enabled.");
        }

        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var organizationId = new OrganizationId(seed.OrganizationId);
        var environmentId = new IamEnvironmentId(seed.EnvironmentId);
        var credentialId = new ConnectorHostCredentialId(seed.ConnectorHostCredentialId);
        var manifestId = new SeedManifestId("iam-default-seed:v1");
        var principalScopeBackfillManifestId = new SeedManifestId("iam-admin-principal-scope-backfill:v1");
        var seedAlreadyApplied = await dbContext.SeedManifests.FindAsync([manifestId], cancellationToken) is not null;
        var principalScopeBackfillApplied = await dbContext.SeedManifests
            .FindAsync([principalScopeBackfillManifestId], cancellationToken) is not null;
        var financeMaintenanceReadManifestId = new SeedManifestId("iam-erp-finance-maintenance-work-orders-read:v1");
        var financeMaintenanceReadApplied = await dbContext.SeedManifests
            .FindAsync([financeMaintenanceReadManifestId], cancellationToken) is not null;
        var now = DateTimeOffset.UtcNow;

        foreach (var seedRole in NervIipSeedRoles.ErpJobRoles)
        {
            var roleId = new RoleId(seedRole.RoleId);
            var existingRole = await dbContext.Roles
                .Include(x => x.Permissions)
                .SingleOrDefaultAsync(x => x.Id == roleId, cancellationToken);
            if (existingRole is not null)
            {
                // 已有环境的财务专员补维修工单只读（#3827）。只补仍是上一版默认权限的角色，
                // 运营改过的不动；补一次后记 manifest，之后运营再撤掉也不会被补回。
                if (!financeMaintenanceReadApplied
                    && seedRole.RoleId == ErpFinanceRoleId
                    && existingRole.RoleName == seedRole.RoleName
                    && SetEquals(
                        existingRole.Permissions.Select(x => x.PermissionCode),
                        ErpFinanceDefaultPermissionsBeforeMaintenanceRead))
                {
                    existingRole.ReplacePermissions([
                        .. ErpFinanceDefaultPermissionsBeforeMaintenanceRead,
                        NervIipPermissionCodes.MaintenanceWorkOrdersRead,
                    ]);
                }

                continue;
            }

            var erpRole = new Role(roleId, seedRole.RoleName, seedRole.PermissionCodes);
            erpRole.ReplaceDataScopes([
                new DataScopeBinding(DataScopeBinding.Organization, seed.OrganizationId),
            ]);
            dbContext.Roles.Add(erpRole);
        }

        var (role, user) = await EnsurePlatformAdministratorAsync(
            dbContext,
            seed,
            () => NewAdministrator(seed, null, null, passwordChangeRequired: false),
            cancellationToken);
        if (!principalScopeBackfillApplied
            && seedAlreadyApplied
            && role.DataScopes.Count == 0
            && SetEquals(role.Permissions.Select(x => x.PermissionCode), NervIipSeedPermissions.All))
        {
            role.ReplaceDataScopes([new DataScopeBinding(DataScopeBinding.Organization, seed.OrganizationId)]);
        }

        if (!seedAlreadyApplied && !passwordService.Verify(user, seed.AdminPassword))
        {
            user.UpdatePasswordHash(passwordService.Hash(seed.AdminPassword), now, now.AddDays(90), false, 5);
        }

        var connectorCapabilities = NervIipSeedPermissions.All
            .Where(x => x.StartsWith("connectors.", StringComparison.Ordinal))
            .ToArray();
        var connectorSecretHash = tokenService.HashSecret(seed.ConnectorHostSecret);
        var credential = await dbContext.ConnectorHostCredentials
            .Include(x => x.Capabilities)
            .SingleOrDefaultAsync(x => x.Id == credentialId || x.ConnectorHostId == seed.ConnectorHostId, cancellationToken);
        if (credential is null)
        {
            dbContext.ConnectorHostCredentials.Add(new ConnectorHostCredential(
                credentialId,
                seed.ConnectorHostId,
                organizationId,
                environmentId,
                connectorSecretHash,
                now.AddDays(-1),
                null,
                connectorCapabilities));
        }
        else
        {
            if (!tokenService.IsCurrentSecretHash(credential.SecretHash)
                || (!seedAlreadyApplied && !tokenService.VerifySecret(seed.ConnectorHostSecret, credential.SecretHash)))
            {
                credential.ReplaceSecretHash(connectorSecretHash);
            }

            if (!SetEquals(credential.Capabilities.Select(x => x.CapabilityCode), connectorCapabilities))
            {
                credential.ReplaceCapabilities(connectorCapabilities);
            }
        }

        if (!string.IsNullOrWhiteSpace(seed.ExternalClientSecret))
        {
            var externalClientSecretHash = tokenService.HashSecret(seed.ExternalClientSecret);
            var externalClient = await dbContext.ExternalClients
                .SingleOrDefaultAsync(x => x.ClientId == seed.ExternalClientId, cancellationToken);
            if (externalClient is null)
            {
                dbContext.ExternalClients.Add(new ExternalClient(
                    new ExternalClientId(seed.ExternalClientId),
                    seed.ExternalClientId,
                    seed.ExternalClientDisplayName,
                    organizationId,
                    environmentId,
                    externalClientSecretHash,
                    true,
                    1,
                    now.AddDays(-1),
                    null));
            }
            else if (!tokenService.IsCurrentSecretHash(externalClient.SecretHash)
                || (!seedAlreadyApplied && !tokenService.VerifySecret(seed.ExternalClientSecret, externalClient.SecretHash)))
            {
                externalClient.ReplaceSecretHash(externalClientSecretHash);
            }

            foreach (var permissionCode in seed.ExternalClientPermissionCodes.Distinct(StringComparer.Ordinal))
            {
                var grantId = new AuthorizationGrantId($"external-client:{seed.ExternalClientId}:{seed.OrganizationId}:{seed.EnvironmentId}:{permissionCode}");
                if (await dbContext.AuthorizationGrants.FindAsync([grantId], cancellationToken) is null)
                {
                    dbContext.AuthorizationGrants.Add(new AuthorizationGrant(
                        grantId,
                        "external-client",
                        seed.ExternalClientId,
                        organizationId,
                        environmentId,
                        permissionCode,
                        "*",
                        "*",
                        now.AddDays(-1),
                        null));
                }
            }
        }

        if (!seedAlreadyApplied)
        {
            dbContext.SeedManifests.Add(new SeedManifest(manifestId, "iam-default-seed", "v1", "iam", now));
        }

        if (!financeMaintenanceReadApplied)
        {
            dbContext.SeedManifests.Add(new SeedManifest(
                financeMaintenanceReadManifestId,
                "iam-erp-finance-maintenance-work-orders-read",
                "v1",
                "iam",
                now));
        }

        if (!principalScopeBackfillApplied)
        {
            dbContext.SeedManifests.Add(new SeedManifest(
                principalScopeBackfillManifestId,
                "iam-admin-principal-scope-backfill",
                "v1",
                "iam",
                now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<(Role Role, User User)> EnsurePlatformAdministratorAsync(
        ApplicationDbContext dbContext,
        IamSeedOptions seed,
        Func<User> createAdministrator,
        CancellationToken cancellationToken)
    {
        var organizationId = new OrganizationId(seed.OrganizationId);
        var environmentId = new IamEnvironmentId(seed.EnvironmentId);
        var adminUserId = new UserId(seed.AdminUserId);
        var adminRoleId = new RoleId(seed.AdminRoleId);
        var membershipId = new MembershipId($"{seed.AdminUserId}:{seed.OrganizationId}:{seed.EnvironmentId}");

        if (await dbContext.Organizations.FindAsync([organizationId], cancellationToken) is null)
        {
            dbContext.Organizations.Add(new Organization(organizationId, seed.OrganizationName, "active"));
        }

        if (await dbContext.Environments.FindAsync([environmentId], cancellationToken) is null)
        {
            dbContext.Environments.Add(new IamEnvironment(environmentId, organizationId, seed.EnvironmentName, "active"));
        }

        var role = await dbContext.Roles
            .Include(x => x.Permissions)
            .Include(x => x.DataScopes)
            .SingleOrDefaultAsync(x => x.Id == adminRoleId, cancellationToken);
        if (role is null)
        {
            role = new Role(adminRoleId, NervIipSeedRoles.PlatformAdministratorRoleName, NervIipSeedPermissions.All);
            role.ReplaceDataScopes([new DataScopeBinding(DataScopeBinding.Organization, seed.OrganizationId)]);
            dbContext.Roles.Add(role);
        }

        await RenameLegacyAdministratorRoleAsync(dbContext, role, cancellationToken);

        var user = await dbContext.Users.FindAsync([adminUserId], cancellationToken);
        if (user is null)
        {
            user = createAdministrator();
            dbContext.Users.Add(user);
        }

        if (!await dbContext.Memberships.AnyAsync(x => x.Id == membershipId, cancellationToken))
        {
            dbContext.Memberships.Add(new Membership(membershipId, adminUserId, organizationId, environmentId, [adminRoleId]));
        }

        return (role, user);
    }

    /// <summary>
    /// 已有环境里平台管理员的角色名原是英文（#3838），引导与基线 seed 两条路径都在这里改成中文。
    /// 只改仍是旧默认名的角色，运营改过名的不动；中文名已被运营自建角色占用时也不改（角色名唯一）。改一次后记 manifest。
    /// </summary>
    private static async Task RenameLegacyAdministratorRoleAsync(
        ApplicationDbContext dbContext,
        Role role,
        CancellationToken cancellationToken)
    {
        if (await dbContext.SeedManifests.FindAsync([AdminRoleNameManifestId], cancellationToken) is not null)
        {
            return;
        }

        var chineseName = Role.NormalizeName(NervIipSeedRoles.PlatformAdministratorRoleName);
        if (role.RoleName == LegacyPlatformAdministratorRoleName
            && !await dbContext.Roles.AnyAsync(x => x.NormalizedRoleName == chineseName, cancellationToken))
        {
            role.Rename(NervIipSeedRoles.PlatformAdministratorRoleName);
        }

        dbContext.SeedManifests.Add(new SeedManifest(
            AdminRoleNameManifestId,
            "iam-platform-admin-role-name-zh",
            "v1",
            "iam",
            DateTimeOffset.UtcNow));
    }

    private User NewAdministrator(
        IamSeedOptions seed,
        DateTimeOffset? passwordChangedAtUtc,
        DateTimeOffset? passwordExpiresAtUtc,
        bool passwordChangeRequired)
    {
        return new User(
            new UserId(seed.AdminUserId),
            seed.AdminLoginName,
            seed.AdminEmail,
            passwordService.Hash(seed.AdminPassword),
            true,
            Guid.NewGuid().ToString("n"),
            1,
            passwordChangedAtUtc: passwordChangedAtUtc,
            passwordExpiresAtUtc: passwordExpiresAtUtc,
            passwordChangeRequired: passwordChangeRequired);
    }

    private static bool SetEquals(IEnumerable<string> current, IEnumerable<string> desired)
    {
        return current.ToHashSet(StringComparer.Ordinal).SetEquals(desired);
    }
}
