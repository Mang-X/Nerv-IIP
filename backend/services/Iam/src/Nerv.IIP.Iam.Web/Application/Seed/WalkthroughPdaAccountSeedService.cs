using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Application.Seed;

/// <summary>为本地演示补齐 PDA 与 PC 岗位账号和授权。</summary>
public sealed class WalkthroughPdaAccountSeedService(
    IServiceProvider serviceProvider,
    IOptions<IamSeedOptions> options,
    IamPasswordService passwordService)
{
    /// <summary>PDA 产线操作工角色：派工/工序执行/报工/领料/完工入库/SOP/报警/报修。</summary>
    public const string OperatorRoleId = "role-pda-operator";

    /// <summary>PDA 仓储库管角色：收货/上架/拣货/出库/盘点、SKU 查询 + 库存联动字段。</summary>
    public const string WarehouseRoleId = "role-pda-warehouse";

    /// <summary>PDA 质量检验员角色：检验任务执行与记录/NCR 查看。</summary>
    public const string InspectorRoleId = "role-pda-inspector";

    private const string WarehouseProductsReadPermission = NervIipPermissionCodes.MasterDataProductsRead;
    private const string WarehouseCountsReadPermission = NervIipPermissionCodes.WmsCountsRead;

    public static readonly (string RoleId, string RoleName, string[] PermissionCodes)[] Roles =
    [
        (OperatorRoleId, "产线操作工（PDA）",
        [
            NervIipPermissionCodes.MesWorkOrdersRead,
            NervIipPermissionCodes.MesDispatchRead,
            NervIipPermissionCodes.MesOperationsRead,
            NervIipPermissionCodes.MesOperationsManage,
            NervIipPermissionCodes.MesReportingRead,
            NervIipPermissionCodes.MesReportingWrite,
            NervIipPermissionCodes.MesMaterialsRead,
            NervIipPermissionCodes.MesMaterialsManage,
            NervIipPermissionCodes.MesReceiptsRead,
            NervIipPermissionCodes.MesReceiptsManage,
            NervIipPermissionCodes.EngineeringDocumentsRead,
            NervIipPermissionCodes.IiotAlarmsRead,
            NervIipPermissionCodes.IiotAlarmsWrite,
            NervIipPermissionCodes.MaintenanceWorkOrdersRead,
            NervIipPermissionCodes.MaintenanceWorkOrdersManage,
            NervIipPermissionCodes.MaintenancePlansRead,
            NervIipPermissionCodes.MaintenanceDowntimeReasonsRead,
            NervIipPermissionCodes.MasterDataResourcesRead,
        ]),
        (WarehouseRoleId, "仓储库管（PDA）",
        [
            NervIipPermissionCodes.WmsReceiptsRead,
            NervIipPermissionCodes.WmsReceiptsManage,
            NervIipPermissionCodes.WmsShipmentsRead,
            NervIipPermissionCodes.WmsShipmentsManage,
            WarehouseProductsReadPermission,
            WarehouseCountsReadPermission,
            NervIipPermissionCodes.InventoryLedgerRead,
            NervIipPermissionCodes.InventoryCountsManage,
            NervIipPermissionCodes.InventoryMovementsCreate,
            NervIipPermissionCodes.MasterDataResourcesRead,
        ]),
        (InspectorRoleId, "质量检验员（PDA）",
        [
            NervIipPermissionCodes.QualityInspectionRecordsRead,
            NervIipPermissionCodes.QualityInspectionRecordsCreate,
            NervIipPermissionCodes.MesWorkOrdersRead,
            NervIipPermissionCodes.MasterDataResourcesRead,
        ]),
    ];

    private static readonly (string UserId, string LoginName, string EmployeeNo, string Name, string Department, string RoleId)[] Accounts =
    [
        ("user-emp-010", "emp010", "EMP-010", "吴桂芳", "生产部", OperatorRoleId),
        ("user-emp-012", "emp012", "EMP-012", "孙明辉", "生产部", OperatorRoleId),
        ("user-emp-034", "emp034", "EMP-034", "朱立新", "质量部", InspectorRoleId),
        ("user-emp-049", "emp049", "EMP-049", "周文斌", "仓储部", WarehouseRoleId),
    ];

    private static readonly (string LoginName, string DisplayName, string RoleId)[] PcAccounts =
    [
        ("demo-buyer", "采购专员", "role-erp-procurement"),
        ("demo-sales", "销售专员", "role-erp-sales"),
        ("demo-finance", "财务专员", "role-erp-finance"),
        ("demo-planner", "生产计划员", NervIipSeedRoles.ProductionPlannerRoleId),
        ("demo-lead", "停机班组长", NervIipSeedRoles.MesDowntimeTeamLeader.RoleId),
        ("demo-maintainer", "维保技师", NervIipSeedRoles.MaintenanceTechnician.RoleId),
        ("demo-qa-manager", "质检主管", NervIipSeedRoles.QualityManager.RoleId),
        ("demo-masterdata", "主数据管理员", NervIipSeedRoles.MasterDataAdministrator.RoleId),
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seed = options.Value;
        if (string.IsNullOrWhiteSpace(seed.DemoWorkerPassword))
        {
            return;
        }

        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var organizationId = new OrganizationId(seed.OrganizationId);
        var environmentId = new IamEnvironmentId(seed.EnvironmentId);
        var principalScopeBackfillManifestId = new SeedManifestId("iam-pda-principal-scope-backfill:v1");
        var principalScopeBackfillApplied = await dbContext.SeedManifests
            .FindAsync([principalScopeBackfillManifestId], cancellationToken) is not null;
        var warehouseSiteScopeManifestId = new SeedManifestId("iam-pda-warehouse-site-scope:v2");
        var warehouseSiteScopeApplied = await dbContext.SeedManifests
            .FindAsync([warehouseSiteScopeManifestId], cancellationToken) is not null;
        var warehouseCountsReadManifestId =
            new SeedManifestId("iam-pda-warehouse-counts-read-permission:v1");
        var warehouseCountsReadApplied = await dbContext.SeedManifests
            .FindAsync([warehouseCountsReadManifestId], cancellationToken) is not null;
        var warehouseProductsReadManifestId =
            new SeedManifestId("iam-pda-warehouse-products-read-permission:v1");
        var warehouseProductsReadApplied = await dbContext.SeedManifests
            .FindAsync([warehouseProductsReadManifestId], cancellationToken) is not null;
        var now = DateTimeOffset.UtcNow;
        var rolesById = new Dictionary<string, Role>(StringComparer.Ordinal);

        foreach (var (roleId, roleName, permissionCodes) in Roles)
        {
            var typedRoleId = new RoleId(roleId);
            var role = await dbContext.Roles
                .Include(x => x.Permissions)
                .Include(x => x.DataScopes)
                .SingleOrDefaultAsync(x => x.Id == typedRoleId, cancellationToken);
            if (role is null)
            {
                role = new Role(typedRoleId, roleName, permissionCodes);
                dbContext.Roles.Add(role);
            }

            rolesById.Add(roleId, role);
        }

        if (!warehouseCountsReadApplied)
        {
            var warehouseRole = rolesById[WarehouseRoleId];
            if (IsLegacyWarehouseRoleBeforeCountsRead(
                    warehouseRole,
                    warehouseSiteScopeApplied))
            {
                warehouseRole.ReplacePermissions([
                    .. warehouseRole.Permissions.Select(permission => permission.PermissionCode),
                    WarehouseCountsReadPermission,
                ]);
            }

            dbContext.SeedManifests.Add(new SeedManifest(
                warehouseCountsReadManifestId,
                "iam-pda-warehouse-counts-read-permission",
                "v1",
                "iam",
                now));
        }

        if (!warehouseProductsReadApplied)
        {
            var warehouseRole = rolesById[WarehouseRoleId];
            if (IsLegacyWarehouseRoleBeforeProductsRead(
                    warehouseRole,
                    warehouseSiteScopeApplied))
            {
                warehouseRole.ReplacePermissions([
                    .. warehouseRole.Permissions.Select(permission => permission.PermissionCode),
                    WarehouseProductsReadPermission,
                ]);
            }

            dbContext.SeedManifests.Add(new SeedManifest(
                warehouseProductsReadManifestId,
                "iam-pda-warehouse-products-read-permission",
                "v1",
                "iam",
                now));
        }

        if (!warehouseSiteScopeApplied)
        {
            var warehouseRole = rolesById[WarehouseRoleId];
            if (warehouseRole.DataScopes.Count == 0
                && IsLegacyBaselineRole(warehouseRole, WarehouseRoleId))
            {
                warehouseRole.ReplaceDataScopes([
                    new DataScopeBinding(DataScopeBinding.Site, "SITE-001"),
                ]);
            }

            dbContext.SeedManifests.Add(new SeedManifest(
                warehouseSiteScopeManifestId,
                "iam-pda-warehouse-site-scope",
                "v2",
                "iam",
                now));
        }

        foreach (var (userId, loginName, employeeNo, name, department, roleId) in Accounts)
        {
            var typedUserId = new UserId(userId);
            var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == typedUserId, cancellationToken);
            if (user is null)
            {
                user = CreateDemoUser(typedUserId, loginName, seed.DemoWorkerPassword, now, name, employeeNo, department);
                dbContext.Users.Add(user);
            }

            else
            {
                if (user.EmployeeNo is null && user.DisplayName is null)
                {
                    user.SetWorkerProfile(name, employeeNo, department);
                }

                if (user.PasswordChangeRequired || !passwordService.Verify(user, seed.DemoWorkerPassword))
                {
                    user.UpdatePasswordHash(
                        passwordService.Hash(seed.DemoWorkerPassword),
                        now,
                        passwordExpiresAtUtc: null,
                        passwordChangeRequired: false,
                        historyLimit: 0);
                }
            }

            var membershipId = new MembershipId($"{userId}:{seed.OrganizationId}:{seed.EnvironmentId}");
            var typedRoleId = new RoleId(roleId);
            var membership = await dbContext.Memberships
                .Include(x => x.Roles)
                .Include(x => x.DataScopes)
                .SingleOrDefaultAsync(x => x.Id == membershipId, cancellationToken);
            if (membership is null)
            {
                membership = new Membership(membershipId, typedUserId, organizationId, environmentId, [typedRoleId]);
                membership.ReplaceDataScopes([new DataScopeBinding(DataScopeBinding.Self, userId)]);
                dbContext.Memberships.Add(membership);
            }
            else if (!principalScopeBackfillApplied
                && membership.DataScopes.Count == 0
                && HasExactRole(membership, typedRoleId)
                && IsLegacyBaselineRole(rolesById[roleId], roleId))
            {
                membership.ReplaceDataScopes([new DataScopeBinding(DataScopeBinding.Self, userId)]);
            }
        }

        if (!principalScopeBackfillApplied)
        {
            dbContext.SeedManifests.Add(new SeedManifest(
                principalScopeBackfillManifestId,
                "iam-pda-principal-scope-backfill",
                "v1",
                "iam",
                now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SeedPcAccountsAsync(CancellationToken cancellationToken = default)
    {
        var seed = options.Value;
        if (!seed.Enabled || string.IsNullOrWhiteSpace(seed.DemoWorkerPassword)) return;

        var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var organizationId = new OrganizationId(seed.OrganizationId);
        var environmentId = new IamEnvironmentId(seed.EnvironmentId);
        var now = DateTimeOffset.UtcNow;

        foreach (var (loginName, displayName, roleId) in PcAccounts)
        {
            var userId = new UserId($"user-{loginName}");
            var user = await dbContext.Users.FindAsync([userId], cancellationToken);
            if (user is null)
            {
                user = CreateDemoUser(userId, loginName, seed.DemoWorkerPassword, now, displayName);
                dbContext.Users.Add(user);
            }
            else if (user.PasswordChangeRequired || !passwordService.Verify(user, seed.DemoWorkerPassword))
            {
                user.UpdatePasswordHash(passwordService.Hash(seed.DemoWorkerPassword), now, null, false, 0);
            }

            var membershipId = new MembershipId($"{userId.Id}:{seed.OrganizationId}:{seed.EnvironmentId}");
            if (await dbContext.Memberships.FindAsync([membershipId], cancellationToken) is null)
            {
                var membership = new Membership(membershipId, userId, organizationId, environmentId, [new RoleId(roleId)]);
                membership.ReplaceDataScopes([new DataScopeBinding(DataScopeBinding.Organization, seed.OrganizationId)]);
                dbContext.Memberships.Add(membership);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private User CreateDemoUser(
        UserId userId,
        string loginName,
        string password,
        DateTimeOffset now,
        string name,
        string? employeeNo = null,
        string? department = null) =>
        new(
            userId,
            loginName,
            $"{loginName}@ninghu-damper.local",
            passwordService.Hash(password),
            enabled: true,
            Guid.NewGuid().ToString("n"),
            permissionVersion: 1,
            accountExpiresAtUtc: null,
            passwordChangedAtUtc: now,
            passwordExpiresAtUtc: null,
            passwordChangeRequired: false,
            displayName: name,
            employeeNo: employeeNo,
            departmentName: department);

    private static bool HasExactRole(Membership membership, RoleId roleId) =>
        membership.Roles.Count == 1 && membership.Roles.Single().RoleId == roleId;

    private static bool IsLegacyBaselineRole(Role role, string roleId)
    {
        var baseline = Roles.Single(x => x.RoleId == roleId);
        return role.RoleName == baseline.RoleName
            && role.Permissions
                .Select(x => x.PermissionCode)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(baseline.PermissionCodes);
    }

    private static bool IsLegacyWarehouseRoleBeforeCountsRead(
        Role role,
        bool warehouseSiteScopeApplied)
    {
        var baseline = Roles.Single(x => x.RoleId == WarehouseRoleId);
        var previousPermissionCodes = baseline.PermissionCodes
            .Where(permissionCode =>
                !string.Equals(
                    permissionCode,
                    WarehouseCountsReadPermission,
                    StringComparison.Ordinal)
                && !string.Equals(
                    permissionCode,
                    WarehouseProductsReadPermission,
                    StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var permissionCodesWithProductsRead = baseline.PermissionCodes
            .Where(permissionCode =>
                !string.Equals(
                    permissionCode,
                    WarehouseCountsReadPermission,
                    StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        return role.RoleName == baseline.RoleName
            && (role.Permissions
                    .Select(permission => permission.PermissionCode)
                    .ToHashSet(StringComparer.Ordinal)
                    .SetEquals(previousPermissionCodes)
                || role.Permissions
                    .Select(permission => permission.PermissionCode)
                    .ToHashSet(StringComparer.Ordinal)
                    .SetEquals(permissionCodesWithProductsRead))
            && IsManagedLegacyWarehouseScope(
                role,
                warehouseSiteScopeApplied);
    }

    private static bool IsLegacyWarehouseRoleBeforeProductsRead(
        Role role,
        bool warehouseSiteScopeApplied)
    {
        var baseline = Roles.Single(x => x.RoleId == WarehouseRoleId);
        var previousPermissionCodes = baseline.PermissionCodes
            .Where(permissionCode =>
                !string.Equals(
                    permissionCode,
                    WarehouseProductsReadPermission,
                    StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        return role.RoleName == baseline.RoleName
            && role.Permissions
                .Select(permission => permission.PermissionCode)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(previousPermissionCodes)
            && IsManagedLegacyWarehouseScope(
                role,
                warehouseSiteScopeApplied);
    }

    private static bool IsManagedLegacyWarehouseScope(
        Role role,
        bool warehouseSiteScopeApplied)
    {
        if (role.DataScopes.Count == 0)
        {
            return true;
        }

        if (!warehouseSiteScopeApplied || role.DataScopes.Count != 1)
        {
            return false;
        }

        var scope = role.DataScopes.Single();
        return string.Equals(
                scope.ScopeType,
                DataScopeBinding.Site,
                StringComparison.Ordinal)
            && string.Equals(
                scope.ScopeCode,
                "SITE-001",
                StringComparison.Ordinal);
    }
}
