using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
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
/// 幂等：角色权限集与成员资格按期望值收敛；口令仅在与演示口令不一致时覆写
/// （覆写同时清除 PasswordChangeRequired，保证 PDA 直接可登录）。
/// </summary>
public sealed class WorldBiblePdaDemoAccountSeedService(
    IServiceProvider serviceProvider,
    IOptions<IamSeedOptions> options,
    IamPasswordService passwordService)
{
    /// <summary>PDA 产线操作工角色：派工/工序执行/报工/领料/完工入库/SOP/报警/报修。</summary>
    internal const string OperatorRoleId = "role-pda-operator";

    /// <summary>PDA 仓储库管角色：收货/上架/拣货/出库/盘点 + 库存联动字段。</summary>
    internal const string WarehouseRoleId = "role-pda-warehouse";

    /// <summary>PDA 质量检验员角色：检验任务执行与记录/NCR 查看。</summary>
    internal const string InspectorRoleId = "role-pda-inspector";

    internal static readonly (string RoleId, string RoleName, string[] PermissionCodes)[] Roles =
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
        ]),
        (InspectorRoleId, "质量检验员（PDA）",
        [
            "business.quality.inspection-records.read",
            "business.quality.inspection-records.create",
            "business.mes.work-orders.read",
        ]),
    ];

    /// <summary>
    /// 4 名演示账号（userId 必须落在 <see cref="WorldBibleWorkerSpec.Workers"/> 的 58 人内）：
    /// EMP-010 机加车间早班组操作工、EMP-012 装配车间早班组操作工、
    /// EMP-034 检验员、EMP-049 库管。班组归属见 MasterData 侧 WorldBibleSpec（操作工按 6 班组轮转）。
    /// </summary>
    internal static readonly (string UserId, string RoleId)[] Accounts =
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
        var now = DateTimeOffset.UtcNow;

        foreach (var (roleId, roleName, permissionCodes) in Roles)
        {
            var typedRoleId = new RoleId(roleId);
            var role = await dbContext.Roles
                .Include(x => x.Permissions)
                .SingleOrDefaultAsync(x => x.Id == typedRoleId, cancellationToken);
            if (role is null)
            {
                dbContext.Roles.Add(new Role(typedRoleId, roleName, permissionCodes));
            }
            else if (!SetEquals(role.Permissions.Select(x => x.PermissionCode), permissionCodes))
            {
                role.ReplacePermissions(permissionCodes);
            }
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
                .SingleOrDefaultAsync(x => x.Id == membershipId, cancellationToken);
            if (membership is null)
            {
                dbContext.Memberships.Add(new Membership(membershipId, typedUserId, organizationId, environmentId, [typedRoleId]));
            }
            else if (!SetEquals(membership.Roles.Select(x => x.RoleId.Id), [typedRoleId.Id]))
            {
                membership.ReplaceRoles([typedRoleId]);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool SetEquals(IEnumerable<string> current, IEnumerable<string> desired)
    {
        return current.ToHashSet(StringComparer.Ordinal).SetEquals(desired);
    }
}
