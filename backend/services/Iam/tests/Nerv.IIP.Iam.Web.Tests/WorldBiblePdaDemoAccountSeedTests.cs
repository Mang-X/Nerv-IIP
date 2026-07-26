using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Web.Application.Seed;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// PDA 演示账号 seed 的固定形状守卫：账号必须落在设定集 58 人名录内、
/// 角色引用闭合、权限码全部存在于权限目录（防手写码漂移成静默 403）。
/// </summary>
public sealed class WorldBiblePdaDemoAccountSeedTests
{
    [Fact]
    public void Demo_accounts_are_members_of_the_world_bible_roster()
    {
        var rosterUserIds = WorldBibleWorkerSpec.Workers.Select(x => x.UserId).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(4, WorldBiblePdaDemoAccountSeedService.Accounts.Length);
        Assert.All(WorldBiblePdaDemoAccountSeedService.Accounts, account =>
            Assert.Contains(account.UserId, rosterUserIds));
    }

    [Fact]
    public void Demo_account_roles_are_declared_by_the_seed()
    {
        var declaredRoleIds = WorldBiblePdaDemoAccountSeedService.Roles
            .Select(x => x.RoleId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(WorldBiblePdaDemoAccountSeedService.Accounts, account =>
            Assert.Contains(account.RoleId, declaredRoleIds));
    }

    [Fact]
    public void Demo_role_permission_codes_exist_in_the_permission_catalog()
    {
        var catalog = NervIipSeedPermissions.All.ToHashSet(StringComparer.Ordinal);

        foreach (var role in WorldBiblePdaDemoAccountSeedService.Roles)
        {
            Assert.NotEmpty(role.PermissionCodes);
            foreach (var code in role.PermissionCodes)
            {
                Assert.True(catalog.Contains(code), $"角色 {role.RoleId} 引用了权限目录中不存在的码：{code}");
            }
        }
    }
}
