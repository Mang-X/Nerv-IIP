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
    public async Task Template_asset_retirement_is_in_the_catalog_and_only_the_default_platform_administrator()
    {
        const string permission = "business.barcodes.template-assets.retire";
        await using var dbContext = CreateDbContext();
        await CreateSeed(dbContext).SeedAsync();
        var roles = await dbContext.Roles.Include(role => role.Permissions).ToListAsync();
        var granted = Assert.Single(roles, role => role.Permissions.Any(p => p.PermissionCode == permission));
        Assert.Equal("Platform Administrator", granted.RoleName);
        Assert.Contains(permission, Nerv.IIP.Iam.Domain.NervIipSeedPermissions.All);
        var catalog = Nerv.IIP.Iam.Web.Application.Permissions.IamPermissionCatalog.List();
        Assert.True(Assert.Single(catalog.Items, item => item.Code == permission).Seeded);
        var memoryRole = Assert.Single(new InMemoryIamStore().Roles, role => role.PermissionCodes.Contains(permission));
        Assert.Equal("Platform Administrator", memoryRole.RoleName);
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
