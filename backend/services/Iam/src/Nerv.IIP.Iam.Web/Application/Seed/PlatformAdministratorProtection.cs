using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Seed;

/// <summary>
/// 引导出来的最高权限管理员（<c>Iam:Seed:AdminUserId</c>）与平台管理员角色（<c>Iam:Seed:AdminRoleId</c>）
/// 不可被停用/设过期，角色不可被收窄，也不可在默认组织环境里被拿掉平台管理员角色。IAM 没有删除用户的写入口；
/// 能让该管理员失去访问或权限的写入口只有停用、改资料（启用/过期）、改角色权限、改角色数据范围、改成员角色五个，
/// 各自的命令处理器在调用应用服务前经这里把关，两种持久化实现共用。
/// 拒绝文案直接写中文：它经平台网关按 400 原样透传到控制台，是给管理员看的业务提示。
/// </summary>
public static class PlatformAdministratorProtection
{
    public static void EnsureCanDisable(IamSeedOptions seed, string userId)
    {
        if (IsAdministrator(seed, userId))
        {
            throw new KnownException("平台管理员账号不能停用。");
        }
    }

    public static void EnsureCanUpdate(IamSeedOptions seed, string userId, bool enabled, DateTimeOffset? accountExpiresAtUtc)
    {
        if (IsAdministrator(seed, userId) && (!enabled || accountExpiresAtUtc is not null))
        {
            throw new KnownException("平台管理员账号不能停用，也不能设置账号过期时间。");
        }
    }

    public static void EnsureRolePermissionsNotReduced(IamSeedOptions seed, string roleId, IReadOnlyList<string> permissionCodes)
    {
        if (string.Equals(roleId, seed.AdminRoleId, StringComparison.Ordinal)
            && !NervIipSeedPermissions.All.ToHashSet(StringComparer.Ordinal).IsSubsetOf(permissionCodes ?? []))
        {
            throw new KnownException("平台管理员角色的权限不能减少。");
        }
    }

    public static void EnsureRoleDataScopesNotReduced(IamSeedOptions seed, string roleId, IReadOnlyList<DataScopeBinding> normalizedDataScopes)
    {
        if (string.Equals(roleId, seed.AdminRoleId, StringComparison.Ordinal)
            && !normalizedDataScopes.Contains(new DataScopeBinding(DataScopeBinding.Organization, seed.OrganizationId)))
        {
            throw new KnownException("平台管理员角色必须保留默认组织的数据范围。");
        }
    }

    public static void EnsureMembershipKeepsAdministratorRole(
        IamSeedOptions seed,
        string userId,
        string organizationId,
        string environmentId,
        IReadOnlyCollection<string> roleIds)
    {
        if (IsAdministrator(seed, userId)
            && string.Equals(organizationId, seed.OrganizationId, StringComparison.Ordinal)
            && string.Equals(environmentId, seed.EnvironmentId, StringComparison.Ordinal)
            && !roleIds.Contains(seed.AdminRoleId, StringComparer.Ordinal))
        {
            throw new KnownException("平台管理员在默认组织环境中必须保留平台管理员角色。");
        }
    }

    private static bool IsAdministrator(IamSeedOptions seed, string userId) =>
        string.Equals(userId, seed.AdminUserId, StringComparison.Ordinal);
}
