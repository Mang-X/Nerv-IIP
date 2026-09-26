using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Seed;

/// <summary>
/// 引导出来的最高权限管理员（<c>Iam:Seed:AdminUserId</c>）与平台管理员角色（<c>Iam:Seed:AdminRoleId</c>）
/// 不可被停用/设过期，角色不可被收窄。IAM 没有删除用户或改成员角色的写入口；
/// 能让该管理员失去访问或权限的写入口只有停用、改资料（启用/过期）、改角色权限、改角色数据范围四个，
/// 各自的命令处理器在调用应用服务前经这里把关，两种持久化实现共用。
/// </summary>
public static class PlatformAdministratorProtection
{
    public static void EnsureCanDisable(IamSeedOptions seed, string userId)
    {
        if (IsAdministrator(seed, userId))
        {
            throw new KnownException("The platform administrator cannot be disabled.");
        }
    }

    public static void EnsureCanUpdate(IamSeedOptions seed, string userId, bool enabled, DateTimeOffset? accountExpiresAtUtc)
    {
        if (IsAdministrator(seed, userId) && (!enabled || accountExpiresAtUtc is not null))
        {
            throw new KnownException("The platform administrator cannot be disabled or given an account expiry.");
        }
    }

    public static void EnsureRolePermissionsNotReduced(IamSeedOptions seed, string roleId, IReadOnlyList<string> permissionCodes)
    {
        if (string.Equals(roleId, seed.AdminRoleId, StringComparison.Ordinal)
            && !NervIipSeedPermissions.All.ToHashSet(StringComparer.Ordinal).IsSubsetOf(permissionCodes ?? []))
        {
            throw new KnownException("Permissions cannot be removed from the platform administrator role.");
        }
    }

    public static void EnsureRoleDataScopesNotReduced(IamSeedOptions seed, string roleId, IReadOnlyList<DataScopeBinding> normalizedDataScopes)
    {
        if (string.Equals(roleId, seed.AdminRoleId, StringComparison.Ordinal)
            && !normalizedDataScopes.Contains(new DataScopeBinding(DataScopeBinding.Organization, seed.OrganizationId)))
        {
            throw new KnownException("The platform administrator role must keep its default organization data scope.");
        }
    }

    private static bool IsAdministrator(IamSeedOptions seed, string userId) =>
        string.Equals(userId, seed.AdminUserId, StringComparison.Ordinal);
}
