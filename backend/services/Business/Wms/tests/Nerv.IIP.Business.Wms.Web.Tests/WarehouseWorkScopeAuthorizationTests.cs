using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Business.Wms.Web.Application.Queries;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WarehouseWorkScopeAuthorizationTests
{
    private static readonly DateTime Now =
        new(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Self_pool_and_site_scopes_are_the_intersection_of_exact_sites_and_memberships()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedPool(
            dbContext,
            "POOL-RECEIVING",
            "收货上架组",
            "SITE-001",
            "user-emp-049",
            Now.AddDays(-1),
            Now.AddDays(1));
        SeedPool(
            dbContext,
            "POOL-SHIPPING",
            "拣货复核组",
            "SITE-001",
            "user-emp-049",
            Now.AddDays(-1),
            Now.AddDays(1));
        SeedPool(
            dbContext,
            "POOL-OTHER-SITE",
            "外站作业组",
            "SITE-002",
            "user-emp-049",
            Now.AddDays(-1),
            Now.AddDays(1));
        await dbContext.SaveChangesAsync();
        var authorizer = new WarehouseWorkScopeAuthorizer(
            dbContext,
            new StaticTimeProvider(Now));

        var self = await authorizer.ResolveAsync(
            WorkScope(
                "self",
                "user-emp-049",
                authorizedSites: ["SITE-001"]),
            CancellationToken.None);
        var pool = await authorizer.ResolveAsync(
            WorkScope(
                "work-pool",
                "POOL-RECEIVING",
                authorizedSites: ["SITE-001"]),
            CancellationToken.None);
        var site = await authorizer.ResolveAsync(
            WorkScope(
                "site",
                "SITE-001",
                authorizedSites: ["SITE-001"]),
            CancellationToken.None);

        Assert.Equal("user-emp-049", self.AssignedOperatorUserId);
        Assert.Equal(["POOL-RECEIVING"], pool.PoolCodes);
        Assert.Equal(["POOL-RECEIVING", "POOL-SHIPPING"], site.PoolCodes);
        Assert.Equal(["SITE-001"], site.SiteCodes);
        Assert.DoesNotContain("POOL-OTHER-SITE", site.PoolCodes);
    }

    [Fact]
    public async Task Missing_exact_site_non_member_cross_site_and_expired_member_all_fail_closed()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedPool(
            dbContext,
            "POOL-ACTIVE",
            "有效组",
            "SITE-001",
            "user-active",
            Now.AddDays(-1),
            Now.AddDays(1));
        SeedPool(
            dbContext,
            "POOL-EXPIRED",
            "过期组",
            "SITE-001",
            "user-expired",
            Now.AddDays(-2),
            Now.AddDays(-1));
        await dbContext.SaveChangesAsync();
        var authorizer = new WarehouseWorkScopeAuthorizer(
            dbContext,
            new StaticTimeProvider(Now));

        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            authorizer.ResolveAsync(
                WorkScope("self", "user-active", authorizedSites: []),
                CancellationToken.None));
        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            authorizer.ResolveAsync(
                WorkScope(
                    "work-pool",
                    "POOL-ACTIVE",
                    actor: "user-other",
                    authorizedSites: ["SITE-001"]),
                CancellationToken.None));
        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            authorizer.ResolveAsync(
                WorkScope(
                    "site",
                    "SITE-002",
                    actor: "user-active",
                    authorizedSites: ["SITE-001"]),
                CancellationToken.None));
        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            authorizer.ResolveAsync(
                WorkScope(
                    "work-pool",
                    "POOL-EXPIRED",
                    actor: "user-expired",
                    authorizedSites: ["SITE-001"]),
                CancellationToken.None));
    }

    [Fact]
    public async Task Manager_need_not_be_a_pool_member_but_target_operator_must_be_effective()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedPool(
            dbContext,
            "POOL-RECEIVING",
            "收货上架组",
            "SITE-001",
            "user-target",
            Now.AddDays(-1),
            Now.AddDays(1));
        await dbContext.SaveChangesAsync();
        var authorizer = new WarehouseWorkScopeAuthorizer(
            dbContext,
            new StaticTimeProvider(Now));

        var allowed = await authorizer.AuthorizeAssignmentAsync(
            new WarehouseAssignmentAuthorizationRequest(
                "org-001",
                "env-dev",
                "user-manager",
                ["SITE-001"],
                "SITE-001",
                "POOL-RECEIVING",
                "user-target"),
            CancellationToken.None);

        Assert.Equal("POOL-RECEIVING", allowed.PoolCode);
        Assert.Equal("user-target", allowed.OperatorPrincipalId);
        await Assert.ThrowsAsync<WmsAuthorizationException>(() =>
            authorizer.AuthorizeAssignmentAsync(
                new WarehouseAssignmentAuthorizationRequest(
                    "org-001",
                    "env-dev",
                    "user-manager",
                    ["SITE-001"],
                    "SITE-001",
                    "POOL-RECEIVING",
                    "user-not-a-member"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Catalog_contains_only_self_active_pools_and_sites_inside_exact_grants()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedPool(
            dbContext,
            "POOL-RECEIVING",
            "收货上架组",
            "SITE-001",
            "user-emp-049",
            Now.AddDays(-1),
            Now.AddDays(1));
        SeedPool(
            dbContext,
            "POOL-EXPIRED",
            "过期作业组",
            "SITE-001",
            "user-emp-049",
            Now.AddDays(-2),
            Now.AddDays(-1));
        SeedPool(
            dbContext,
            "POOL-OTHER-SITE",
            "外站作业组",
            "SITE-002",
            "user-emp-049",
            Now.AddDays(-1),
            Now.AddDays(1));
        await dbContext.SaveChangesAsync();

        var catalog = await new WarehouseWorkScopeAuthorizer(
                dbContext,
                new StaticTimeProvider(Now))
            .GetCatalogAsync(
                "org-001",
                "env-dev",
                "user-emp-049",
                ["SITE-001"],
                CancellationToken.None);

        Assert.Equal(
            [
                ("self", "user-emp-049"),
                ("work-pool", "POOL-RECEIVING"),
                ("site", "SITE-001"),
            ],
            catalog.Items
                .Select(item => (item.ScopeKind, item.ScopeId))
                .ToArray());
    }

    [Fact]
    public async Task Self_pool_and_site_queues_use_persisted_assignment_and_never_show_legacy_unassigned()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SeedPool(
            dbContext,
            "POOL-A",
            "收货组",
            "SITE-001",
            "user-emp-049",
            Now.AddDays(-1),
            Now.AddDays(1));
        SeedPool(
            dbContext,
            "POOL-B",
            "发货组",
            "SITE-001",
            "user-emp-049",
            Now.AddDays(-1),
            Now.AddDays(1));
        SeedPool(
            dbContext,
            "POOL-C",
            "外站组",
            "SITE-002",
            "user-emp-049",
            Now.AddDays(-1),
            Now.AddDays(1));
        dbContext.WarehouseTasks.AddRange(
            CreateTask("SELF", "SITE-001", "POOL-A", "user-emp-049"),
            CreateTask("POOL-UNCLAIMED", "SITE-001", "POOL-A", null),
            CreateTask("POOL-OTHER-OP", "SITE-001", "POOL-A", "user-other"),
            CreateTask("SITE-OTHER-POOL", "SITE-001", "POOL-B", null),
            CreateTask("OTHER-SITE", "SITE-002", "POOL-C", null),
            CreateTask("LEGACY-UNASSIGNED", "SITE-001", null, null));
        await dbContext.SaveChangesAsync();
        var authorizer = new WarehouseWorkScopeAuthorizer(
            dbContext,
            new StaticTimeProvider(Now));
        var queryHandler = new ListWarehouseTasksQueryHandler(dbContext);

        var selfScope = await authorizer.ResolveAsync(
            WorkScope("self", "user-emp-049", authorizedSites: ["SITE-001"]),
            CancellationToken.None);
        var poolScope = await authorizer.ResolveAsync(
            WorkScope("work-pool", "POOL-A", authorizedSites: ["SITE-001"]),
            CancellationToken.None);
        var siteScope = await authorizer.ResolveAsync(
            WorkScope("site", "SITE-001", authorizedSites: ["SITE-001"]),
            CancellationToken.None);

        var self = await Query(queryHandler, selfScope);
        var pool = await Query(queryHandler, poolScope);
        var site = await Query(queryHandler, siteScope);

        Assert.Equal(["SELF"], self.Items.Select(item => item.TaskNo));
        Assert.Equal(
            ["POOL-OTHER-OP", "POOL-UNCLAIMED", "SELF"],
            pool.Items.Select(item => item.TaskNo).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["POOL-OTHER-OP", "POOL-UNCLAIMED", "SELF", "SITE-OTHER-POOL"],
            site.Items.Select(item => item.TaskNo).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(site.Items, item => item.TaskNo == "LEGACY-UNASSIGNED");
        Assert.DoesNotContain(site.Items, item => item.TaskNo == "OTHER-SITE");
    }

    private static WarehouseWorkScopeRequest WorkScope(
        string kind,
        string id,
        string actor = "user-emp-049",
        IReadOnlyCollection<string>? authorizedSites = null) =>
        new(
            "org-001",
            "env-dev",
            actor,
            authorizedSites ?? ["SITE-001"],
            kind,
            id,
            SiteCode: null);

    private static void SeedPool(
        ApplicationDbContext dbContext,
        string poolCode,
        string displayName,
        string siteCode,
        string principalId,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc)
    {
        dbContext.WarehouseWorkPools.Add(WarehouseWorkPool.Create(
            "org-001",
            "env-dev",
            poolCode,
            displayName,
            siteCode));
        dbContext.WarehouseWorkPoolMemberships.Add(WarehouseWorkPoolMembership.Create(
            "org-001",
            "env-dev",
            poolCode,
            principalId,
            effectiveFromUtc,
            effectiveToUtc));
    }

    private static WarehouseTask CreateTask(
        string taskNo,
        string siteCode,
        string? poolCode,
        string? operatorPrincipalId) =>
        WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            taskNo,
            "OUT-001",
            taskNo,
            "SKU-001",
            "pcs",
            siteCode,
            "BIN-01",
            "PACK-01",
            1m,
            assignedOperatorUserId: operatorPrincipalId,
            assignedPoolCode: poolCode);

    private static Task<ListWarehouseTasksResponse> Query(
        ListWarehouseTasksQueryHandler handler,
        WarehouseWorkScopeSelection selection) =>
        handler.Handle(
            new ListWarehouseTasksQuery(
                "org-001",
                "env-dev",
                WarehouseTaskType.Picking,
                AssignedOperatorUserIds: selection.AssignedOperatorUserId is null
                    ? null
                    : [selection.AssignedOperatorUserId],
                AssignedPoolCodes: selection.AssignedOperatorUserId is null
                    ? selection.PoolCodes
                    : null,
                SiteCodes: selection.SiteCodes),
            CancellationToken.None);

    private sealed class StaticTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(utcNow, TimeSpan.Zero);
    }
}
