using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.SeedAggregate;
using Nerv.IIP.Iam.Infrastructure;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Seed;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// Issue #1792 的默认 ERP 岗位角色合同：采购、销售、财务用户可以直接选择岗位角色，
/// 无需从完整权限目录逐项组装授权。
/// </summary>
public sealed class IamErpRoleSeedTests
{
    [Fact]
    public async Task Bootstrap_creates_planner_role_with_workbench_read_permissions()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.BootstrapAsync();

        var role = await dbContext.Roles
            .Include(candidate => candidate.Permissions)
            .Include(candidate => candidate.DataScopes)
            .SingleAsync(candidate => candidate.Id == new RoleId(Nerv.IIP.Iam.Domain.NervIipSeedRoles.ProductionPlannerRoleId));
        Assert.Equal("生产计划员", role.RoleName);
        Assert.Contains(role.Permissions, permission => permission.PermissionCode == "business.planning.mps.release");
        Assert.Contains(role.Permissions, permission => permission.PermissionCode == "business.scheduling.plans.manage");
        Assert.Contains(role.Permissions, permission => permission.PermissionCode == "business.mes.work-orders.read");
        Assert.Contains(role.Permissions, permission => permission.PermissionCode == "notifications.messages.read");
        Assert.Contains(role.Permissions, permission => permission.PermissionCode == "notifications.tasks.read");
        var scope = Assert.Single(role.DataScopes);
        Assert.Equal(DataScopeBinding.Organization, scope.ScopeType);
        Assert.Equal("org-001", scope.ScopeCode);
    }

    [Fact]
    public async Task Bootstrap_upgrades_only_the_fixed_planner_role_and_preserves_custom_permissions_and_members()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.BootstrapAsync();

        var plannerRoleId = new RoleId("role-production-planner");
        var planner = await dbContext.Roles
            .Include(role => role.Permissions)
            .Include(role => role.DataScopes)
            .SingleAsync(role => role.Id == plannerRoleId);
        planner.ReplacePermissions(["business.planning.mps.read", "business.masterdata.products.read"]);
        planner.ReplaceDataScopes([new DataScopeBinding(DataScopeBinding.Site, "SITE-CUSTOM")]);
        dbContext.Roles.Add(new Role(new RoleId("role-custom-planner"), "计划员自定义", ["business.planning.mps.read"]));
        var membership = await dbContext.Memberships.Include(item => item.Roles).SingleAsync();
        membership.ReplaceRoles([new RoleId("role-platform-admin"), plannerRoleId]);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await seed.BootstrapAsync();
        await seed.BootstrapAsync();
        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        planner = await dbContext.Roles
            .Include(role => role.Permissions)
            .Include(role => role.DataScopes)
            .SingleAsync(role => role.Id == plannerRoleId);
        Assert.Equal(
            ["business.masterdata.products.read", "business.mes.work-orders.read", "business.planning.mps.read", "notifications.messages.read", "notifications.tasks.read"],
            planner.Permissions.Select(permission => permission.PermissionCode).Order(StringComparer.Ordinal));
        var custom = await dbContext.Roles.Include(role => role.Permissions)
            .SingleAsync(role => role.Id == new RoleId("role-custom-planner"));
        Assert.Equal("计划员自定义", custom.RoleName);
        Assert.Equal(["business.planning.mps.read"], custom.Permissions.Select(permission => permission.PermissionCode));
        Assert.Equal([new DataScopeBinding(DataScopeBinding.Site, "SITE-CUSTOM")],
            planner.DataScopes.Select(scope => new DataScopeBinding(scope.ScopeType, scope.ScopeCode)));
        membership = await dbContext.Memberships.Include(item => item.Roles).SingleAsync();
        Assert.Equal(
            ["role-platform-admin", "role-production-planner"],
            membership.Roles.Select(role => role.RoleId.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Bootstrap_preserves_an_existing_same_name_role_and_creates_the_canonical_planner_role()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Roles.Add(new Role(new RoleId("role-operator-created"), "生产计划员", ["business.planning.mps.read"]));
        await dbContext.SaveChangesAsync();

        await CreateSeed(dbContext).BootstrapAsync();
        dbContext.ChangeTracker.Clear();

        var roles = await dbContext.Roles.Include(role => role.Permissions).ToListAsync();
        var existing = Assert.Single(roles, role => role.Id == new RoleId("role-operator-created"));
        Assert.Equal("生产计划员", existing.RoleName);
        Assert.Equal(["business.planning.mps.read"], existing.Permissions.Select(permission => permission.PermissionCode));

        var canonical = Assert.Single(roles, role => role.Id == new RoleId("role-production-planner"));
        Assert.Equal("生产计划员（系统预置）", canonical.RoleName);
        Assert.Contains(canonical.Permissions, permission => permission.PermissionCode == "business.planning.mps.release");
    }

    [Fact]
    public async Task Template_asset_retirement_is_in_the_catalog_and_only_the_default_platform_administrator()
    {
        const string permission = "business.barcodes.template-assets.retire";
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync();
        var roles = await dbContext.Roles.Include(role => role.Permissions).ToListAsync();
        var granted = Assert.Single(roles, role => role.Permissions.Any(p => p.PermissionCode == permission));
        Assert.Equal("平台管理员", granted.RoleName);
        Assert.Contains(permission, Nerv.IIP.Iam.Domain.NervIipSeedPermissions.All);
        var catalog = Nerv.IIP.Iam.Web.Application.Permissions.IamPermissionCatalog.List();
        Assert.True(Assert.Single(catalog.Items, item => item.Code == permission).Seeded);
        var memoryRole = Assert.Single(new InMemoryIamStore().Roles, role => role.PermissionCodes.Contains(permission));
        Assert.Equal("平台管理员", memoryRole.RoleName);
    }

    private static readonly IReadOnlyDictionary<string, (string RoleName, string[] PermissionCodes)> ExpectedRoles =
        new Dictionary<string, (string RoleName, string[] PermissionCodes)>(StringComparer.Ordinal)
        {
            ["role-erp-procurement"] =
            ("ERP 采购专员",
            [
                "business.masterdata.products.read",
                "business.masterdata.resources.read",
                "business.erp.procurement.read",
                "business.erp.procurement.manage",
            ]),
            ["role-erp-sales"] =
            ("ERP 销售专员",
            [
                "business.masterdata.products.read",
                "business.masterdata.resources.read",
                "business.erp.sales.read",
                "business.erp.sales.manage",
            ]),
            ["role-erp-finance"] =
            ("ERP 财务专员",
            [
                "business.masterdata.resources.read",
                "business.erp.procurement.read",
                "business.erp.sales.read",
                "business.erp.finance.read",
                "business.erp.finance.manage",
                "business.maintenance.work-orders.read",
            ]),
        };

    private static readonly string[] FinanceBaselineBeforeMaintenanceRead =
    [
        "business.masterdata.resources.read",
        "business.erp.procurement.read",
        "business.erp.sales.read",
        "business.erp.finance.read",
        "business.erp.finance.manage",
    ];

    [Fact]
    public async Task Default_seed_creates_three_organization_scoped_erp_job_roles()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);

        await seed.SeedAsync();

        var roles = await dbContext.Roles
            .Include(role => role.Permissions)
            .Include(role => role.DataScopes)
            .Where(role => ExpectedRoles.Keys.Contains(role.Id.Id))
            .ToDictionaryAsync(role => role.Id.Id, StringComparer.Ordinal);

        Assert.Equal(ExpectedRoles.Count, roles.Count);
        foreach (var (roleId, expected) in ExpectedRoles)
        {
            var role = roles[roleId];
            Assert.Equal(expected.RoleName, role.RoleName);
            Assert.Equal(
                expected.PermissionCodes.Order(StringComparer.Ordinal),
                role.Permissions.Select(permission => permission.PermissionCode).Order(StringComparer.Ordinal));
            var scope = Assert.Single(role.DataScopes);
            Assert.Equal(DataScopeBinding.Organization, scope.ScopeType);
            Assert.Equal("org-001", scope.ScopeCode);
        }
    }

    [Fact]
    public void In_memory_profile_exposes_the_same_three_erp_job_roles()
    {
        var store = new InMemoryIamStore();
        var roleDataScopesField = typeof(InMemoryIamStore)
            .GetField("_roleDataScopes", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(roleDataScopesField);
        var roleDataScopes = Assert.IsAssignableFrom<IReadOnlyDictionary<string, IReadOnlySet<DataScopeBinding>>>(
            roleDataScopesField.GetValue(store));

        var roles = store.Roles
            .Where(role => ExpectedRoles.ContainsKey(role.RoleId))
            .ToDictionary(role => role.RoleId, StringComparer.Ordinal);

        Assert.Equal(ExpectedRoles.Count, roles.Count);
        foreach (var (roleId, expected) in ExpectedRoles)
        {
            Assert.Equal(expected.RoleName, roles[roleId].RoleName);
            Assert.Equal(
                expected.PermissionCodes.Order(StringComparer.Ordinal),
                roles[roleId].PermissionCodes.Order(StringComparer.Ordinal));
            var scope = Assert.Single(roleDataScopes[roleId]);
            Assert.Equal(DataScopeBinding.Organization, scope.ScopeType);
            Assert.Equal("org-001", scope.ScopeCode);
        }
    }

    [Fact]
    public async Task Default_seed_does_not_overwrite_an_existing_erp_role_configuration()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.SeedAsync();

        var role = await dbContext.Roles
            .Include(candidate => candidate.Permissions)
            .Include(candidate => candidate.DataScopes)
            .SingleAsync(candidate => candidate.Id == new RoleId("role-erp-procurement"));
        role.ReplacePermissions(["business.masterdata.partners.read"]);
        role.ReplaceDataScopes([
            new DataScopeBinding(DataScopeBinding.Site, "SITE-CUSTOM"),
        ]);
        await dbContext.SaveChangesAsync();

        await seed.SeedAsync();
        dbContext.ChangeTracker.Clear();

        var preserved = await dbContext.Roles
            .Include(candidate => candidate.Permissions)
            .Include(candidate => candidate.DataScopes)
            .SingleAsync(candidate => candidate.Id == new RoleId("role-erp-procurement"));
        Assert.Equal(
            ["business.masterdata.partners.read"],
            preserved.Permissions.Select(permission => permission.PermissionCode));
        var scope = Assert.Single(preserved.DataScopes);
        Assert.Equal(DataScopeBinding.Site, scope.ScopeType);
        Assert.Equal("SITE-CUSTOM", scope.ScopeCode);
    }

    // #3827：存量环境里的财务专员是按上一版默认权限建的，重启后要补上维修工单只读。
    [Fact]
    public async Task Reseed_adds_maintenance_work_order_read_to_a_finance_role_still_on_the_previous_default()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.SeedAsync();
        await RewindToBeforeMaintenanceReadBackfill(dbContext, FinanceBaselineBeforeMaintenanceRead);

        await seed.SeedAsync();

        Assert.Equal(
            ExpectedRoles["role-erp-finance"].PermissionCodes.Order(StringComparer.Ordinal),
            await FinancePermissionCodes(dbContext));
    }

    [Fact]
    public async Task Reseed_leaves_an_operator_adjusted_finance_role_untouched()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.SeedAsync();
        await RewindToBeforeMaintenanceReadBackfill(dbContext, ["business.erp.finance.read"]);

        await seed.SeedAsync();

        Assert.Equal(["business.erp.finance.read"], await FinancePermissionCodes(dbContext));
    }

    [Fact]
    public async Task Reseed_does_not_restore_maintenance_read_after_an_operator_revoked_it()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.SeedAsync();
        var role = await dbContext.Roles
            .Include(candidate => candidate.Permissions)
            .SingleAsync(candidate => candidate.Id == new RoleId("role-erp-finance"));
        role.ReplacePermissions(FinanceBaselineBeforeMaintenanceRead);
        await dbContext.SaveChangesAsync();

        await seed.SeedAsync();

        Assert.Equal(
            FinanceBaselineBeforeMaintenanceRead.Order(StringComparer.Ordinal),
            await FinancePermissionCodes(dbContext));
    }

    // #3838：存量环境里平台管理员的角色名是英文，重启后改成中文；运营改过名的不动。
    [Fact]
    public async Task Reseed_renames_the_platform_administrator_still_on_the_english_default()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.SeedAsync();
        await RewindAdminRoleName(dbContext, "Platform Administrator");

        await seed.SeedAsync();

        Assert.Equal("平台管理员", await AdminRoleName(dbContext));
    }

    // 生产环境只跑启动引导不跑基线 seed，存量英文名也要在引导时改掉。
    [Fact]
    public async Task Bootstrap_renames_the_platform_administrator_still_on_the_english_default()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.SeedAsync();
        await RewindAdminRoleName(dbContext, "Platform Administrator");

        await seed.BootstrapAsync();

        Assert.Equal("平台管理员", await AdminRoleName(dbContext));
    }

    [Fact]
    public async Task Reseed_keeps_an_operator_renamed_platform_administrator()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.SeedAsync();
        await RewindAdminRoleName(dbContext, "超级管理员");

        await seed.SeedAsync();

        Assert.Equal("超级管理员", await AdminRoleName(dbContext));
    }

    // 中文名已被运营自建角色占用时不改：角色名唯一，硬改会让每次启动都在 SaveChanges 上失败。
    [Fact]
    public async Task Reseed_keeps_the_english_name_when_an_operator_role_already_uses_the_chinese_name()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.SeedAsync();
        await RewindAdminRoleName(dbContext, "Platform Administrator");
        dbContext.Roles.Add(new Role(new RoleId("role-operator-zh"), "平台管理员", ["iam.users.read"]));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await seed.SeedAsync();

        Assert.Equal("Platform Administrator", await AdminRoleName(dbContext));
    }

    // 改过一次（manifest 已记录）之后，运营再改回英文名也不会被再次改掉。
    [Fact]
    public async Task Reseed_does_not_rename_again_once_the_manifest_is_recorded()
    {
        await using var dbContext = CreateDbContext();
        var seed = CreateSeed(dbContext);
        await seed.SeedAsync();
        await RewindAdminRoleName(dbContext, "Platform Administrator", removeManifest: false);

        await seed.SeedAsync();

        Assert.Equal("Platform Administrator", await AdminRoleName(dbContext));
    }

    private static async Task RewindAdminRoleName(
        ApplicationDbContext dbContext,
        string roleName,
        bool removeManifest = true)
    {
        var role = await dbContext.Roles.SingleAsync(candidate => candidate.Id == new RoleId("role-platform-admin"));
        role.Rename(roleName);
        if (removeManifest)
        {
            dbContext.SeedManifests.Remove(await dbContext.SeedManifests.SingleAsync(
                manifest => manifest.Id == new SeedManifestId("iam-platform-admin-role-name-zh:v1")));
        }

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static async Task<string> AdminRoleName(ApplicationDbContext dbContext)
    {
        dbContext.ChangeTracker.Clear();
        return (await dbContext.Roles.SingleAsync(candidate => candidate.Id == new RoleId("role-platform-admin"))).RoleName;
    }

    private static async Task RewindToBeforeMaintenanceReadBackfill(
        ApplicationDbContext dbContext,
        IEnumerable<string> financePermissionCodes)
    {
        var role = await dbContext.Roles
            .Include(candidate => candidate.Permissions)
            .SingleAsync(candidate => candidate.Id == new RoleId("role-erp-finance"));
        role.ReplacePermissions(financePermissionCodes);
        dbContext.SeedManifests.Remove(await dbContext.SeedManifests.SingleAsync(
            manifest => manifest.Id == new SeedManifestId("iam-erp-finance-maintenance-work-orders-read:v1")));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static async Task<string[]> FinancePermissionCodes(ApplicationDbContext dbContext)
    {
        dbContext.ChangeTracker.Clear();
        var role = await dbContext.Roles
            .Include(candidate => candidate.Permissions)
            .SingleAsync(candidate => candidate.Id == new RoleId("role-erp-finance"));
        return role.Permissions
            .Select(permission => permission.PermissionCode)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"iam-erp-role-seed-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(dbOptions, new NoopMediator());
    }

    private static IamSeedService CreateSeed(ApplicationDbContext dbContext)
    {
        var services = new ServiceCollection()
            .AddSingleton(dbContext)
            .AddSingleton(Options.Create(new IamPasswordPolicyOptions()))
            .AddSingleton<IamPasswordService>()
            .AddSingleton<IamPasswordPolicy>()
            .BuildServiceProvider();
        return new IamSeedService(
            services,
            Options.Create(new IamSeedOptions
            {
                Enabled = true,
                OrganizationId = "org-001",
                EnvironmentId = "env-dev",
                AdminPassword = "Admin-Seed-Test-2026!",
                ConnectorHostSecret = "connector-secret-test",
            }),
            new IamPasswordService(),
            new IamTokenService(new ConfigurationBuilder().Build(), new TestWebHostEnvironment()));
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
