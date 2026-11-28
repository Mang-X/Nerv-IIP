using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain;
using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Seed;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class WalkthroughPdaAccountSeedTests
{
    private static readonly (string UserId, string RoleId)[] ExpectedAccounts =
    [
        ("user-emp-010", "role-pda-operator"),
        ("user-emp-012", "role-pda-operator"),
        ("user-emp-034", "role-pda-inspector"),
        ("user-emp-049", "role-pda-warehouse"),
    ];

    [Fact]
    public void Demo_role_permission_codes_exist_in_the_permission_catalog()
    {
        var catalog = NervIipSeedPermissions.All.ToHashSet(StringComparer.Ordinal);

        foreach (var role in WalkthroughPdaAccountSeedService.Roles)
        {
            Assert.NotEmpty(role.PermissionCodes);
            foreach (var code in role.PermissionCodes)
            {
                Assert.True(catalog.Contains(code), $"角色 {role.RoleId} 引用了权限目录中不存在的码：{code}");
            }
        }
    }

    [Fact]
    public void Operator_demo_role_retains_downtime_reason_vocabulary_access_after_the_rebind()
    {
        // ADR 0029 决策 5：换绑是权限收窄，旧码 business.maintenance.work-orders.read 的
        // 默认角色持有者必须在同一变更内补授新码，否则是功能回退。role-pda-operator 正是
        // MES 停机的消费方角色（唯一需要手工补授的默认角色；role-platform-admin 持全集
        // 自动跟随，无需手工）。
        var operatorRole = Assert.Single(
            WalkthroughPdaAccountSeedService.Roles,
            role => role.RoleId == WalkthroughPdaAccountSeedService.OperatorRoleId);

        Assert.Contains(
            "business.maintenance.downtime-reasons.read",
            operatorRole.PermissionCodes);
    }

    [Fact]
    public void Warehouse_demo_role_grants_sku_read_without_sku_manage()
    {
        var warehouseRole = Assert.Single(
            WalkthroughPdaAccountSeedService.Roles,
            role => role.RoleId == WalkthroughPdaAccountSeedService.WarehouseRoleId);

        Assert.Contains(
            "business.masterdata.products.read",
            warehouseRole.PermissionCodes);
        Assert.DoesNotContain(
            "business.masterdata.products.manage",
            warehouseRole.PermissionCodes);
    }

    [Fact]
    public async Task Demo_account_seed_persists_an_explicit_self_scope_for_each_membership()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"pda-demo-account-seed-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(dbOptions, new NoopMediator());
        var passwordService = new IamPasswordService();
        using var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();
        var seed = new WalkthroughPdaAccountSeedService(
            services,
            Options.Create(new IamSeedOptions
            {
                OrganizationId = "org-001",
                EnvironmentId = "env-dev",
                DemoWorkerPassword = "Worker-Demo-Test-2026!",
            }),
            passwordService);

        await seed.SeedAsync();

        Assert.Equal(ExpectedAccounts.Length, await dbContext.Users.CountAsync());
        var warehouseUser = await dbContext.Users.SingleAsync(user => user.LoginName == "emp049");
        Assert.Equal("user-emp-049", warehouseUser.Id.Id);
        Assert.True(passwordService.Verify(warehouseUser, "Worker-Demo-Test-2026!"));
        Assert.False(warehouseUser.PasswordChangeRequired);

        foreach (var account in ExpectedAccounts)
        {
            var membershipId = $"{account.UserId}:org-001:env-dev";
            var membership = await dbContext.Memberships
                .Include(x => x.Roles)
                .Include(x => x.DataScopes)
                .SingleAsync(x => x.Id.Id == membershipId);
            var role = Assert.Single(membership.Roles);
            var scope = Assert.Single(membership.DataScopes);

            Assert.Equal(account.RoleId, role.RoleId.Id);
            Assert.Equal("self", scope.ScopeType);
            Assert.Equal(account.UserId, scope.ScopeCode);
        }

        var seededRoles = await dbContext.Roles
            .Include(x => x.DataScopes)
            .Include(x => x.Permissions)
            .Where(x => WalkthroughPdaAccountSeedService.Roles
                .Select(role => role.RoleId)
                .Contains(x.Id.Id))
            .ToArrayAsync();
        var warehouseRole = Assert.Single(
            seededRoles,
            x => x.Id.Id == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        var warehouseSite = Assert.Single(warehouseRole.DataScopes);
        Assert.Equal(DataScopeBinding.Site, warehouseSite.ScopeType);
        Assert.Equal("SITE-001", warehouseSite.ScopeCode);
        Assert.Contains(
            warehouseRole.Permissions,
            permission => permission.PermissionCode == "business.wms.counts.read");
        Assert.All(
            seededRoles.Where(x => x.Id.Id != WalkthroughPdaAccountSeedService.WarehouseRoleId),
            role => Assert.DoesNotContain(
                role.DataScopes,
                scope => scope.ScopeType == DataScopeBinding.Site));
        Assert.NotNull(await dbContext.SeedManifests.FindAsync(
            new SeedManifestId("iam-pda-warehouse-site-scope:v2")));

        var customized = ExpectedAccounts[0];
        var customizedRole = await dbContext.Roles
            .Include(x => x.Permissions)
            .SingleAsync(x => x.Id.Id == customized.RoleId);
        var retainedPermission = "business.masterdata.resources.manage";
        customizedRole.ReplacePermissions(customizedRole.Permissions
            .Select(x => x.PermissionCode)
            .Append(retainedPermission)
            .Distinct(StringComparer.Ordinal));
        var retainedRoleId = new RoleId("role-pda-retained-custom");
        dbContext.Roles.Add(new Role(retainedRoleId, "Retained custom role", [retainedPermission]));
        var customizedMembership = await dbContext.Memberships
            .Include(x => x.Roles)
            .Include(x => x.DataScopes)
            .SingleAsync(x => x.Id.Id == $"{customized.UserId}:org-001:env-dev");
        customizedMembership.ReplaceRoles([
            .. customizedMembership.Roles.Select(x => x.RoleId),
            retainedRoleId,
        ]);
        customizedMembership.ReplaceDataScopes([
            .. customizedMembership.DataScopes.Select(x => new DataScopeBinding(x.ScopeType, x.ScopeCode)),
            new DataScopeBinding(DataScopeBinding.Workshop, "WS-RETAINED"),
        ]);
        await dbContext.SaveChangesAsync();

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        var preservedRole = await dbContext.Roles
            .Include(x => x.Permissions)
            .SingleAsync(x => x.Id.Id == customized.RoleId);
        Assert.Contains(preservedRole.Permissions, x => x.PermissionCode == retainedPermission);
        var preservedMembership = await dbContext.Memberships
            .Include(x => x.Roles)
            .Include(x => x.DataScopes)
            .SingleAsync(x => x.Id.Id == $"{customized.UserId}:org-001:env-dev");
        Assert.Contains(preservedMembership.Roles, x => x.RoleId == retainedRoleId);
        Assert.Contains(preservedMembership.DataScopes, x =>
            x.ScopeType == DataScopeBinding.Workshop
            && x.ScopeCode == "WS-RETAINED");
    }

    [Fact]
    public async Task Demo_account_seed_backfills_counts_read_only_for_the_legacy_warehouse_role()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"pda-demo-account-counts-read-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(dbOptions, new NoopMediator());
        var passwordService = new IamPasswordService();
        var warehouseBaseline = Assert.Single(
            WalkthroughPdaAccountSeedService.Roles,
            role => role.RoleId == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        var warehouseRoleBeforeCountsRead = new Role(
            new RoleId(warehouseBaseline.RoleId),
            warehouseBaseline.RoleName,
            warehouseBaseline.PermissionCodes.Where(permissionCode =>
                permissionCode != "business.wms.counts.read"));
        warehouseRoleBeforeCountsRead.ReplaceDataScopes([
            new DataScopeBinding(DataScopeBinding.Site, "SITE-001"),
        ]);
        dbContext.Roles.Add(warehouseRoleBeforeCountsRead);
        dbContext.SeedManifests.Add(new SeedManifest(
            new SeedManifestId("iam-pda-warehouse-site-scope:v2"),
            "iam-pda-warehouse-site-scope",
            "v2",
            "iam",
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        using var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();
        var seed = new WalkthroughPdaAccountSeedService(
            services,
            Options.Create(new IamSeedOptions
            {
                OrganizationId = "org-001",
                EnvironmentId = "env-dev",
                DemoWorkerPassword = "Worker-Demo-Test-2026!",
            }),
            passwordService);

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        var warehouseRole = await dbContext.Roles
            .Include(role => role.Permissions)
            .SingleAsync(role =>
                role.Id.Id == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        Assert.Contains(
            warehouseRole.Permissions,
            permission => permission.PermissionCode == "business.wms.counts.read");
        Assert.NotNull(await dbContext.SeedManifests.FindAsync(
            new SeedManifestId("iam-pda-warehouse-counts-read-permission:v1")));
    }

    [Fact]
    public async Task Demo_account_seed_backfills_sku_read_only_for_the_legacy_warehouse_role()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"pda-demo-account-products-read-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(dbOptions, new NoopMediator());
        var passwordService = new IamPasswordService();
        var warehouseBaseline = Assert.Single(
            WalkthroughPdaAccountSeedService.Roles,
            role => role.RoleId == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        var warehouseRoleBeforeProductsRead = new Role(
            new RoleId(warehouseBaseline.RoleId),
            warehouseBaseline.RoleName,
            warehouseBaseline.PermissionCodes.Where(permissionCode =>
                permissionCode != "business.masterdata.products.read"));
        warehouseRoleBeforeProductsRead.ReplaceDataScopes([
            new DataScopeBinding(DataScopeBinding.Site, "SITE-001"),
        ]);
        dbContext.Roles.Add(warehouseRoleBeforeProductsRead);
        dbContext.SeedManifests.Add(new SeedManifest(
            new SeedManifestId("iam-pda-warehouse-site-scope:v2"),
            "iam-pda-warehouse-site-scope",
            "v2",
            "iam",
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        using var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();
        var seed = new WalkthroughPdaAccountSeedService(
            services,
            Options.Create(new IamSeedOptions
            {
                OrganizationId = "org-001",
                EnvironmentId = "env-dev",
                DemoWorkerPassword = "Worker-Demo-Test-2026!",
            }),
            passwordService);

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        var warehouseRole = await dbContext.Roles
            .Include(role => role.Permissions)
            .Include(role => role.DataScopes)
            .SingleAsync(role =>
                role.Id.Id == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        Assert.Contains(
            warehouseRole.Permissions,
            permission => permission.PermissionCode == "business.masterdata.products.read");
        Assert.DoesNotContain(
            warehouseRole.Permissions,
            permission => permission.PermissionCode == "business.masterdata.products.manage");
        Assert.NotNull(await dbContext.SeedManifests.FindAsync(
            new SeedManifestId("iam-pda-warehouse-products-read-permission:v1")));

        var firstPermissionCodes = warehouseRole.Permissions
            .Select(permission => permission.PermissionCode)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var firstDataScopes = warehouseRole.DataScopes
            .Select(scope => new DataScopeBinding(scope.ScopeType, scope.ScopeCode))
            .OrderBy(scope => scope.ScopeType, StringComparer.Ordinal)
            .ThenBy(scope => scope.ScopeCode, StringComparer.Ordinal)
            .ToArray();

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        warehouseRole = await dbContext.Roles
            .Include(role => role.Permissions)
            .Include(role => role.DataScopes)
            .SingleAsync(role =>
                role.Id.Id == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        Assert.Equal(
            firstPermissionCodes,
            warehouseRole.Permissions
                .Select(permission => permission.PermissionCode)
                .Order(StringComparer.Ordinal));
        Assert.Equal(firstDataScopes, warehouseRole.DataScopes
            .Select(scope => new DataScopeBinding(scope.ScopeType, scope.ScopeCode))
            .OrderBy(scope => scope.ScopeType, StringComparer.Ordinal)
            .ThenBy(scope => scope.ScopeCode, StringComparer.Ordinal));
        Assert.Equal(
            1,
            await dbContext.SeedManifests.CountAsync(manifest =>
                manifest.Id == new SeedManifestId("iam-pda-warehouse-products-read-permission:v1")));
    }

    [Theory]
    [InlineData(DataScopeBinding.Site, "SITE-CUSTOM")]
    [InlineData(DataScopeBinding.Workshop, "WS-CUSTOM")]
    [InlineData(DataScopeBinding.Organization, "org-custom")]
    public async Task Demo_account_seed_does_not_expand_custom_warehouse_role_scopes_with_sku_read(
        string scopeType,
        string scopeCode)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(
                $"pda-demo-account-products-read-custom-{scopeType}-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(
            dbOptions,
            new NoopMediator());
        var passwordService = new IamPasswordService();
        var warehouseBaseline = Assert.Single(
            WalkthroughPdaAccountSeedService.Roles,
            role => role.RoleId
                == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        var warehouseRole = new Role(
            new RoleId(warehouseBaseline.RoleId),
            warehouseBaseline.RoleName,
            warehouseBaseline.PermissionCodes.Where(permissionCode =>
                permissionCode != "business.masterdata.products.read"));
        warehouseRole.ReplaceDataScopes([new DataScopeBinding(scopeType, scopeCode)]);
        dbContext.Roles.Add(warehouseRole);
        await dbContext.SaveChangesAsync();

        using var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();
        var seed = new WalkthroughPdaAccountSeedService(
            services,
            Options.Create(new IamSeedOptions
            {
                OrganizationId = "org-001",
                EnvironmentId = "env-dev",
                DemoWorkerPassword = "Worker-Demo-Test-2026!",
            }),
            passwordService);

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        var preserved = await dbContext.Roles
            .Include(role => role.Permissions)
            .Include(role => role.DataScopes)
            .SingleAsync(role =>
                role.Id.Id == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        Assert.DoesNotContain(
            preserved.Permissions,
            permission => permission.PermissionCode == "business.masterdata.products.read");
        Assert.DoesNotContain(
            preserved.Permissions,
            permission => permission.PermissionCode == "business.masterdata.products.manage");
        var preservedScope = Assert.Single(preserved.DataScopes);
        Assert.Equal(scopeType, preservedScope.ScopeType);
        Assert.Equal(scopeCode, preservedScope.ScopeCode);

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        preserved = await dbContext.Roles
            .Include(role => role.Permissions)
            .Include(role => role.DataScopes)
            .SingleAsync(role =>
                role.Id.Id == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        Assert.DoesNotContain(
            preserved.Permissions,
            permission => permission.PermissionCode == "business.masterdata.products.read");
        preservedScope = Assert.Single(preserved.DataScopes);
        Assert.Equal(scopeType, preservedScope.ScopeType);
        Assert.Equal(scopeCode, preservedScope.ScopeCode);
    }

    [Theory]
    [InlineData(DataScopeBinding.Site, "SITE-CUSTOM")]
    [InlineData(DataScopeBinding.Workshop, "WS-CUSTOM")]
    [InlineData(DataScopeBinding.Organization, "org-custom")]
    public async Task Demo_account_seed_does_not_expand_custom_warehouse_role_scopes_with_counts_read(
        string scopeType,
        string scopeCode)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(
                $"pda-demo-account-counts-read-custom-{scopeType}-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(
            dbOptions,
            new NoopMediator());
        var passwordService = new IamPasswordService();
        var warehouseBaseline = Assert.Single(
            WalkthroughPdaAccountSeedService.Roles,
            role => role.RoleId
                == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        var warehouseRole = new Role(
            new RoleId(warehouseBaseline.RoleId),
            warehouseBaseline.RoleName,
            warehouseBaseline.PermissionCodes.Where(permissionCode =>
                permissionCode != "business.wms.counts.read"));
        warehouseRole.ReplaceDataScopes([new DataScopeBinding(scopeType, scopeCode)]);
        dbContext.Roles.Add(warehouseRole);
        dbContext.SeedManifests.Add(new SeedManifest(
            new SeedManifestId("iam-pda-warehouse-site-scope:v2"),
            "iam-pda-warehouse-site-scope",
            "v2",
            "iam",
            DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();

        using var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();
        var seed = new WalkthroughPdaAccountSeedService(
            services,
            Options.Create(new IamSeedOptions
            {
                OrganizationId = "org-001",
                EnvironmentId = "env-dev",
                DemoWorkerPassword = "Worker-Demo-Test-2026!",
            }),
            passwordService);

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        var preserved = await dbContext.Roles
            .Include(role => role.Permissions)
            .Include(role => role.DataScopes)
            .SingleAsync(role =>
                role.Id.Id
                == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        Assert.DoesNotContain(
            preserved.Permissions,
            permission =>
                permission.PermissionCode == "business.wms.counts.read");
        var scope = Assert.Single(preserved.DataScopes);
        Assert.Equal(scopeType, scope.ScopeType);
        Assert.Equal(scopeCode, scope.ScopeCode);
        Assert.NotNull(await dbContext.SeedManifests.FindAsync(
            new SeedManifestId("iam-pda-warehouse-counts-read-permission:v1")));
    }

    [Fact]
    public async Task Demo_account_seed_backfills_only_legacy_baseline_memberships_with_empty_scopes()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"pda-demo-account-scope-backfill-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(dbOptions, new NoopMediator());
        var passwordService = new IamPasswordService();
        var organizationId = new OrganizationId("org-001");
        var environmentId = new IamEnvironmentId("env-dev");
        dbContext.Organizations.Add(new Organization(organizationId, "Nerv IIP", "active"));
        dbContext.Environments.Add(new IamEnvironment(environmentId, organizationId, "Development", "active"));

        foreach (var (roleId, roleName, permissionCodes) in WalkthroughPdaAccountSeedService.Roles)
        {
            var role = new Role(new RoleId(roleId), roleName, permissionCodes);
            if (roleId == WalkthroughPdaAccountSeedService.WarehouseRoleId)
            {
                role.ReplaceDataScopes([
                    new DataScopeBinding(DataScopeBinding.Workshop, "WS-RETAINED"),
                ]);
            }

            dbContext.Roles.Add(role);
        }

        var customRoleId = new RoleId("role-pda-customized");
        dbContext.Roles.Add(new Role(customRoleId, "Customized PDA role", ["business.masterdata.resources.read"]));
        for (var index = 0; index < ExpectedAccounts.Length; index++)
        {
            var account = ExpectedAccounts[index];
            var userId = new UserId(account.UserId);
            dbContext.Users.Add(new User(
                userId,
                $"legacy-{account.UserId}",
                $"{account.UserId}@example.test",
                passwordService.Hash(Guid.CreateVersion7().ToString("N")),
                true,
                Guid.CreateVersion7().ToString("N"),
                1,
                passwordChangeRequired: true));
            var membershipRoles = index == ExpectedAccounts.Length - 1
                ? new[] { new RoleId(account.RoleId), customRoleId }
                : [new RoleId(account.RoleId)];
            dbContext.Memberships.Add(new Membership(
                new MembershipId($"{account.UserId}:org-001:env-dev"),
                userId,
                organizationId,
                environmentId,
                membershipRoles));
        }

        await dbContext.SaveChangesAsync();
        using var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();
        var seed = new WalkthroughPdaAccountSeedService(
            services,
            Options.Create(new IamSeedOptions
            {
                OrganizationId = "org-001",
                EnvironmentId = "env-dev",
                DemoWorkerPassword = "Worker-Demo-Test-2026!",
            }),
            passwordService);

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        foreach (var account in ExpectedAccounts[..^1])
        {
            var membership = await dbContext.Memberships
                .Include(x => x.DataScopes)
                .SingleAsync(x => x.Id.Id == $"{account.UserId}:org-001:env-dev");
            var scope = Assert.Single(membership.DataScopes);
            Assert.Equal(DataScopeBinding.Self, scope.ScopeType);
            Assert.Equal(account.UserId, scope.ScopeCode);
        }

        var customizedAccount = ExpectedAccounts[^1];
        var customizedMembership = await dbContext.Memberships
            .Include(x => x.DataScopes)
            .SingleAsync(x => x.Id.Id == $"{customizedAccount.UserId}:org-001:env-dev");
        Assert.Empty(customizedMembership.DataScopes);
        var warehouseRole = await dbContext.Roles
            .Include(x => x.DataScopes)
            .SingleAsync(x => x.Id.Id == WalkthroughPdaAccountSeedService.WarehouseRoleId);
        Assert.Contains(warehouseRole.DataScopes, x =>
            x.ScopeType == DataScopeBinding.Workshop
            && x.ScopeCode == "WS-RETAINED");
        Assert.DoesNotContain(warehouseRole.DataScopes, x =>
            x.ScopeType == DataScopeBinding.Site
            && x.ScopeCode == "SITE-001");
        var nonWarehouseRoles = await dbContext.Roles
            .Include(x => x.DataScopes)
            .Where(x => WalkthroughPdaAccountSeedService.Roles
                .Select(role => role.RoleId)
                .Contains(x.Id.Id)
                && x.Id.Id != WalkthroughPdaAccountSeedService.WarehouseRoleId)
            .ToArrayAsync();
        Assert.All(
            nonWarehouseRoles,
            role => Assert.DoesNotContain(
                role.DataScopes,
                scope => scope.ScopeType == DataScopeBinding.Site));
        Assert.NotNull(await dbContext.SeedManifests.FindAsync(
            new SeedManifestId("iam-pda-principal-scope-backfill:v1")));
        Assert.NotNull(await dbContext.SeedManifests.FindAsync(
            new SeedManifestId("iam-pda-warehouse-site-scope:v2")));
    }

    [Fact]
    public async Task Default_seed_backfills_the_legacy_admin_role_with_an_organization_scope()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"iam-default-scope-backfill-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(dbOptions, new NoopMediator());
        var organizationId = new OrganizationId("org-001");
        var environmentId = new IamEnvironmentId("env-dev");
        var adminUserId = new UserId("user-admin");
        var adminRoleId = new RoleId("role-platform-admin");
        dbContext.Organizations.Add(new Organization(organizationId, "Nerv IIP", "active"));
        dbContext.Environments.Add(new IamEnvironment(environmentId, organizationId, "Development", "active"));
        dbContext.Roles.Add(new Role(adminRoleId, "Platform Administrator", NervIipSeedPermissions.All));
        dbContext.Users.Add(new User(
            adminUserId,
            "admin",
            "admin@nerv-iip.local",
            new IamPasswordService().Hash("Admin-Seed-Test-2026!"),
            true,
            Guid.CreateVersion7().ToString("N"),
            1));
        dbContext.Memberships.Add(new Membership(
            new MembershipId("user-admin:org-001:env-dev"),
            adminUserId,
            organizationId,
            environmentId,
            [adminRoleId]));
        dbContext.SeedManifests.Add(new SeedManifest(
            new SeedManifestId("iam-default-seed:v1"),
            "iam-default-seed",
            "v1",
            "iam",
            DateTimeOffset.UtcNow.AddDays(-1)));
        await dbContext.SaveChangesAsync();

        using var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();
        var seedOptions = Options.Create(new IamSeedOptions
        {
            Enabled = true,
            OrganizationId = "org-001",
            EnvironmentId = "env-dev",
            AdminPassword = "Admin-Seed-Test-2026!",
            ConnectorHostSecret = "connector-secret-test",
        });
        var seed = new IamSeedService(
            services,
            seedOptions,
            new IamPasswordService(),
            new IamTokenService(new ConfigurationBuilder().Build(), new TestWebHostEnvironment()));

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        var adminRole = await dbContext.Roles
            .Include(x => x.DataScopes)
            .SingleAsync(x => x.Id == adminRoleId);
        var scope = Assert.Single(adminRole.DataScopes);
        Assert.Equal(DataScopeBinding.Organization, scope.ScopeType);
        Assert.Equal("org-001", scope.ScopeCode);
        Assert.NotNull(await dbContext.SeedManifests.FindAsync(
            new SeedManifestId("iam-admin-principal-scope-backfill:v1")));
    }

    [Fact]
    public async Task Default_seed_synchronizes_admin_permissions_and_preserves_data_scopes_and_memberships()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"iam-default-seed-preserve-{Guid.CreateVersion7():N}")
            .Options;
        await using var dbContext = new ApplicationDbContext(dbOptions, new NoopMediator());
        using var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .BuildServiceProvider();
        var seedOptions = Options.Create(new IamSeedOptions
        {
            Enabled = true,
            OrganizationId = "org-001",
            EnvironmentId = "env-dev",
            AdminPassword = "Admin-Seed-Test-2026!",
            ConnectorHostSecret = "connector-secret-test",
        });
        var passwordService = new IamPasswordService();
        var seed = new IamSeedService(
            services,
            seedOptions,
            passwordService,
            new IamTokenService(new ConfigurationBuilder().Build(), new TestWebHostEnvironment()));

        await seed.SeedAsync();

        var adminRole = await dbContext.Roles
            .Include(x => x.Permissions)
            .Include(x => x.DataScopes)
            .SingleAsync(x => x.Id.Id == "role-platform-admin");
        adminRole.ReplacePermissions(["business.masterdata.resources.read"]);
        adminRole.ReplaceDataScopes([
            new DataScopeBinding(DataScopeBinding.Organization, "org-001"),
            new DataScopeBinding(DataScopeBinding.Workshop, "WS-ADMIN-RETAINED"),
        ]);
        var retainedRoleId = new RoleId("role-admin-retained-custom");
        dbContext.Roles.Add(new Role(retainedRoleId, "Retained admin role", ["business.masterdata.resources.read"]));
        var adminMembership = await dbContext.Memberships
            .Include(x => x.Roles)
            .SingleAsync(x => x.Id.Id == "user-admin:org-001:env-dev");
        adminMembership.ReplaceRoles([new RoleId("role-platform-admin"), retainedRoleId]);
        await dbContext.SaveChangesAsync();

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        var preservedRole = await dbContext.Roles
            .Include(x => x.Permissions)
            .Include(x => x.DataScopes)
            .SingleAsync(x => x.Id.Id == "role-platform-admin");
        Assert.Equal(NervIipSeedPermissions.All.Order(StringComparer.Ordinal),
            preservedRole.Permissions.Select(x => x.PermissionCode).Order(StringComparer.Ordinal));
        Assert.Contains(preservedRole.DataScopes, x =>
            x.ScopeType == DataScopeBinding.Workshop
            && x.ScopeCode == "WS-ADMIN-RETAINED");
        var preservedMembership = await dbContext.Memberships
            .Include(x => x.Roles)
            .SingleAsync(x => x.Id.Id == "user-admin:org-001:env-dev");
        Assert.Contains(preservedMembership.Roles, x => x.RoleId == retainedRoleId);
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
