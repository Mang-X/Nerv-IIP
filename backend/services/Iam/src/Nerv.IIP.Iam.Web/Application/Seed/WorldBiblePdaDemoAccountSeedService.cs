using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Application.Seed;

/// <summary>
/// 领导演示的 PDA 现场账号开通：从设定集 58 人中选取 4 名代表（机加操作工 / 装配操作工 /
/// 库管 / 检验员），赋统一演示口令并绑定最小权限 PDA 角色。
///
/// 安全边界：仅当 <see cref="IamSeedOptions.DemoWorkerPassword"/> 非空（来自当前进程
/// 环境变量注入）时才执行；口令不落仓库。其余 54 个账号保持不可登录（见
/// <see cref="WorldBibleWorkerSeedService"/>）。
///
/// 幂等：仅为新角色/成员写入基线授权；已存在授权保持不变。旧 seed 基线中仍为空的
/// scope 由一次性、可识别的 backfill 补齐；口令仅在与演示口令不一致时覆写
/// （覆写同时清除 PasswordChangeRequired，保证 PDA 直接可登录）。
/// </summary>
public sealed class WorldBiblePdaDemoAccountSeedService(
    IServiceProvider serviceProvider,
    IOptions<IamSeedOptions> options,
    IamPasswordService passwordService)
{
    /// <summary>PDA 产线操作工角色：派工/工序执行/报工/领料/完工入库/SOP/报警/报修。</summary>
    public const string OperatorRoleId = "role-pda-operator";

    /// <summary>PDA 仓储库管角色：收货/上架/拣货/出库/盘点 + 库存联动字段。</summary>
    public const string WarehouseRoleId = "role-pda-warehouse";

    /// <summary>PDA 质量检验员角色：检验任务执行与记录/NCR 查看。</summary>
    public const string InspectorRoleId = "role-pda-inspector";

    public static readonly (string RoleId, string RoleName, string[] PermissionCodes)[] Roles =
    [
        (OperatorRoleId, "产线操作工（PDA）",
        [
            "business.mes.work-orders.read",
            "business.mes.dispatch.read",
            "business.mes.operations.read",
            "business.mes.operations.manage",
            "business.mes.reporting.read",
            "business.mes.reporting.write",
            "business.mes.materials.read",
            "business.mes.materials.manage",
            "business.mes.receipts.read",
            "business.mes.receipts.manage",
            "business.engineering.documents.read",
            "business.iiot.alarms.read",
            "business.iiot.alarms.write",
            "business.maintenance.work-orders.read",
            "business.maintenance.work-orders.manage",
            "business.maintenance.plans.read",
            "business.masterdata.resources.read",
        ]),
        (WarehouseRoleId, "仓储库管（PDA）",
        [
            "business.wms.receipts.read",
            "business.wms.receipts.manage",
            "business.wms.shipments.read",
            "business.wms.shipments.manage",
            "business.inventory.ledger.read",
            "business.inventory.counts.manage",
            "business.inventory.movements.create",
            "business.masterdata.resources.read",
        ]),
        (InspectorRoleId, "质量检验员（PDA）",
        [
            "business.quality.inspection-records.read",
            "business.quality.inspection-records.create",
            "business.mes.work-orders.read",
            "business.masterdata.resources.read",
        ]),
    ];

    /// <summary>
    /// 4 名演示账号（userId 必须落在 <see cref="WorldBibleWorkerSpec.Workers"/> 的 58 人内）：
    /// EMP-010 机加车间早班组操作工、EMP-012 装配车间早班组操作工、
    /// EMP-034 检验员、EMP-049 库管。班组归属见 MasterData 侧 WorldBibleSpec（操作工按 6 班组轮转）。
    /// </summary>
    public static readonly (string UserId, string RoleId)[] Accounts =
    [
        ("user-emp-010", OperatorRoleId),
        ("user-emp-012", OperatorRoleId),
        ("user-emp-034", InspectorRoleId),
        ("user-emp-049", WarehouseRoleId),
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
        var now = DateTimeOffset.UtcNow;
        var rolesById = new Dictionary<string, Role>(StringComparer.Ordinal);

        foreach (var (roleId, roleName, permissionCodes) in Roles)
        {
            var typedRoleId = new RoleId(roleId);
            var role = await dbContext.Roles
                .Include(x => x.Permissions)
                .SingleOrDefaultAsync(x => x.Id == typedRoleId, cancellationToken);
            if (role is null)
            {
                role = new Role(typedRoleId, roleName, permissionCodes);
                dbContext.Roles.Add(role);
            }

            rolesById.Add(roleId, role);
        }

        foreach (var (userId, roleId) in Accounts)
        {
            var typedUserId = new UserId(userId);
            var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == typedUserId, cancellationToken);
            if (user is null)
            {
                // 58 人名录尚未落库（WorldBibleWorkerSeedService 未启用）时跳过，不凭空造人。
                continue;
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
}
