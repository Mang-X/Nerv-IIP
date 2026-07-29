using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

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

    private sealed class StaticTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(utcNow, TimeSpan.Zero);
    }
}
