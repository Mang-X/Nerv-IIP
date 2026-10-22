using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Queries;
using Nerv.IIP.Business.Wms.Web.Application.Seed;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WmsWorkPoolMembershipSeedServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Seed_gate_requires_explicit_wms_opt_in_and_development_environment()
    {
        var noOptIn = Configuration(
            ("LeaderDemo:World:Enabled", "false"),
            ("LeaderDemo:History:Enabled", "false"),
            ("LeaderDemo:History:Scale", "0"));

        Assert.False(WmsWorkPoolMembershipSeedGate.ShouldSeed(noOptIn, isDevelopment: true));

        var optIn = Configuration(
            (WmsWorkPoolMembershipSeedGate.EnabledKey, "true"),
            ("LeaderDemo:World:Enabled", "false"),
            ("LeaderDemo:History:Enabled", "false"),
            ("LeaderDemo:History:Scale", "0"));

        Assert.True(WmsWorkPoolMembershipSeedGate.ShouldSeed(optIn, isDevelopment: true));
        Assert.Throws<InvalidOperationException>(() =>
            WmsWorkPoolMembershipSeedGate.ShouldSeed(optIn, isDevelopment: false));
    }

    [Fact]
    public void Seed_gate_stays_off_when_world_history_owns_wms_facts()
    {
        var configuration = Configuration(
            (WmsWorkPoolMembershipSeedGate.EnabledKey, "true"),
            ("LeaderDemo:World:Enabled", "false"),
            ("LeaderDemo:History:Enabled", "true"),
            ("LeaderDemo:History:Scale", "1"));

        Assert.False(WmsWorkPoolMembershipSeedGate.ShouldSeed(configuration, isDevelopment: true));
    }

    [Fact]
    public async Task Minimum_seed_creates_receiving_scope_and_receipt_without_history_facts()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var report = await new WmsWorkPoolMembershipSeedService(dbContext, new StaticTimeProvider(Now))
            .SeedAsync("org-001", "env-dev", CancellationToken.None);

        Assert.Equal(1, report.WorkPoolsWritten);
        Assert.Equal(1, report.WorkPoolMembershipsWritten);
        Assert.Equal(1, report.InboundOrdersWritten);

        var pool = Assert.Single(await dbContext.WarehouseWorkPools.AsNoTracking().ToArrayAsync());
        Assert.Equal("POOL-WMS-RECEIVING", pool.PoolCode);
        Assert.Equal("收货与上架", pool.DisplayName);
        Assert.Equal("SITE-001", pool.SiteCode);
        Assert.True(pool.Active);

        var membership = Assert.Single(
            await dbContext.WarehouseWorkPoolMemberships.AsNoTracking().ToArrayAsync());
        Assert.Equal("POOL-WMS-RECEIVING", membership.PoolCode);
        Assert.Equal("user-emp-049", membership.PrincipalId);
        Assert.True(membership.Active);
        Assert.Null(membership.EffectiveToUtc);
        Assert.DoesNotContain(
            await dbContext.WarehouseWorkPoolMemberships.AsNoTracking().ToArrayAsync(),
            item => item.PrincipalId == "user-admin");

        var inbound = Assert.Single(
            await dbContext.InboundOrders
                .AsNoTracking()
                .Include(order => order.Lines)
                .ToArrayAsync());
        Assert.Equal("IB-WMS-SEED-001", inbound.InboundOrderNo);
        Assert.Equal(InboundOrderStatus.Open, inbound.Status);
        Assert.Equal("SITE-001", inbound.SiteCode);
        Assert.Equal("user-emp-049", inbound.AssignedOperatorUserId);
        Assert.Equal("POOL-WMS-RECEIVING", inbound.AssignedPoolCode);
        var line = Assert.Single(inbound.Lines);
        Assert.Equal("10", line.LineNo);
        Assert.Equal("RM-TUB-01", line.SkuCode);
        Assert.Equal("unrestricted", line.QualityStatus);

        // The opt-in is deliberately not the full world-history switch: no derived history facts are written.
        Assert.Empty(await dbContext.BackorderOrders.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.OutboundOrders.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.WarehouseTasks.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.WarehouseTaskActionReceipts.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.WarehouseAssignmentReceipts.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.CountExecutions.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.WcsTasks.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.WcsDispatchCircuits.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.SupplierReturnRequests.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.InventoryMovementRequests.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.AsNoTracking().ToArrayAsync());

        var authorizer = new WarehouseWorkScopeAuthorizer(
            dbContext,
            new StaticTimeProvider(Now));
        var catalog = await authorizer.GetCatalogAsync(
            "org-001",
            "env-dev",
            "user-emp-049",
            ["SITE-001"],
            CancellationToken.None);

        Assert.Equal(
            [
                ("self", "user-emp-049"),
                ("work-pool", "POOL-WMS-RECEIVING"),
                ("site", "SITE-001"),
            ],
            catalog.Items.Select(item => (item.ScopeKind, item.ScopeId)).ToArray());

        var self = await authorizer.ResolveAsync(
            new WarehouseWorkScopeRequest(
                "org-001",
                "env-dev",
                "user-emp-049",
                ["SITE-001"],
                "self",
                "user-emp-049",
                SiteCode: null),
            CancellationToken.None);
        var inboundResult = await new ListInboundOrdersQueryHandler(dbContext).Handle(
            new ListInboundOrdersQuery(
                "org-001",
                "env-dev",
                Status: "Open",
                AssignedOperatorUserIds: [self.AssignedOperatorUserId!],
                SiteCodes: self.SiteCodes),
            CancellationToken.None);

        Assert.Single(inboundResult.Items);
        Assert.Equal("IB-WMS-SEED-001", inboundResult.Items.Single().InboundOrderNo);
    }

    [Fact]
    public async Task Repeated_seed_is_idempotent_and_does_not_duplicate_scope_facts()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new WmsWorkPoolMembershipSeedService(dbContext, new StaticTimeProvider(Now));

        var first = await service.SeedAsync("org-001", "env-dev", CancellationToken.None);
        var second = await service.SeedAsync("org-001", "env-dev", CancellationToken.None);

        Assert.Equal(1, first.WorkPoolsWritten);
        Assert.Equal(1, first.WorkPoolMembershipsWritten);
        Assert.Equal(1, first.InboundOrdersWritten);
        Assert.Equal(0, second.WorkPoolsWritten);
        Assert.Equal(0, second.WorkPoolMembershipsWritten);
        Assert.Equal(0, second.InboundOrdersWritten);
        Assert.Single(await dbContext.WarehouseWorkPools.ToArrayAsync());
        Assert.Single(await dbContext.WarehouseWorkPoolMemberships.ToArrayAsync());
        Assert.Single(await dbContext.InboundOrders.ToArrayAsync());
    }

    [Fact]
    public async Task Existing_unapproved_memberships_fail_closed_before_writing_any_seed_fact()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        dbContext.WarehouseWorkPools.Add(WarehouseWorkPool.Create(
            "org-001",
            "env-dev",
            "POOL-WMS-RECEIVING",
            "收货与上架",
            "SITE-001"));
        dbContext.WarehouseWorkPoolMemberships.AddRange(
            WarehouseWorkPoolMembership.Create(
                "org-001",
                "env-dev",
                "POOL-WMS-RECEIVING",
                "user-admin",
                Now.AddDays(-1)),
            WarehouseWorkPoolMembership.Create(
                "org-001",
                "env-dev",
                "POOL-WMS-RECEIVING",
                "user-emp-048",
                Now.AddDays(-1)));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WmsWorkPoolMembershipSeedService(dbContext, new StaticTimeProvider(Now))
                .SeedAsync("org-001", "env-dev", CancellationToken.None));

        Assert.Contains("user-admin", exception.Message, StringComparison.Ordinal);
        Assert.Contains("user-emp-048", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, await dbContext.WarehouseWorkPools.CountAsync());
        Assert.Equal(2, await dbContext.WarehouseWorkPoolMemberships.CountAsync());
        Assert.Empty(await dbContext.InboundOrders.ToArrayAsync());
    }

    [Theory]
    [InlineData(InboundOrderStatus.Completed)]
    [InlineData(InboundOrderStatus.Cancelled)]
    public async Task Existing_non_open_inbound_order_fails_closed_without_partial_seed_writes(
        InboundOrderStatus status)
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inboundOrder = CreateCanonicalInboundOrder();
        if (status == InboundOrderStatus.Completed)
        {
            _ = inboundOrder.Complete("seed-conflict-complete", inboundOrder.Version);
        }
        else
        {
            inboundOrder.Cancel("seed-conflict-cancelled");
        }

        dbContext.InboundOrders.Add(inboundOrder);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WmsWorkPoolMembershipSeedService(dbContext, new StaticTimeProvider(Now))
                .SeedAsync("org-001", "env-dev", CancellationToken.None));

        Assert.Contains("canonical", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await dbContext.WarehouseWorkPools.ToArrayAsync());
        Assert.Empty(await dbContext.WarehouseWorkPoolMemberships.ToArrayAsync());
        var persisted = Assert.Single(await dbContext.InboundOrders.ToArrayAsync());
        Assert.Equal(status, persisted.Status);
    }

    [Fact]
    public async Task Existing_inbound_assignment_conflict_fails_closed_without_partial_seed_writes()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inboundOrder = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IB-WMS-SEED-001",
            "wms-walkthrough-seed",
            "WMS-WALKTHROUGH-SEED-001",
            "SITE-001",
            [
                new InboundOrderLineDraft(
                    "10",
                    "RM-TUB-01",
                    "kg",
                    1m,
                    "loc-raw-01",
                    "LOT-WMS-SEED-001",
                    SerialNo: null,
                    "unrestricted",
                    "company",
                    OwnerId: null),
            ],
            assignedOperatorUserId: "user-emp-048",
            assignedPoolCode: "POOL-WMS-SHIPPING");
        dbContext.InboundOrders.Add(inboundOrder);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WmsWorkPoolMembershipSeedService(dbContext, new StaticTimeProvider(Now))
                .SeedAsync("org-001", "env-dev", CancellationToken.None));

        Assert.Contains("canonical", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await dbContext.WarehouseWorkPools.ToArrayAsync());
        Assert.Empty(await dbContext.WarehouseWorkPoolMemberships.ToArrayAsync());
        var persisted = Assert.Single(await dbContext.InboundOrders.ToArrayAsync());
        Assert.Equal("user-emp-048", persisted.AssignedOperatorUserId);
        Assert.Equal("POOL-WMS-SHIPPING", persisted.AssignedPoolCode);
    }

    [Fact]
    public async Task Existing_inbound_identity_and_line_conflict_fails_closed_without_partial_seed_writes()
    {
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inboundOrder = InboundOrder.Create(
            "org-001",
            "env-dev",
            "IB-WMS-SEED-001",
            "purchase-order",
            "PO-WMS-SEED-001",
            "SITE-002",
            [
                new InboundOrderLineDraft(
                    "10",
                    "RM-TUB-02",
                    "pcs",
                    2m,
                    "loc-other-01",
                    "LOT-OTHER-001",
                    SerialNo: null,
                    "unrestricted",
                    "customer",
                    OwnerId: "owner-001"),
            ],
            assignedOperatorUserId: "user-emp-049",
            assignedPoolCode: "POOL-WMS-RECEIVING");
        dbContext.InboundOrders.Add(inboundOrder);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WmsWorkPoolMembershipSeedService(dbContext, new StaticTimeProvider(Now))
                .SeedAsync("org-001", "env-dev", CancellationToken.None));

        Assert.Contains("canonical", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await dbContext.WarehouseWorkPools.ToArrayAsync());
        Assert.Empty(await dbContext.WarehouseWorkPoolMemberships.ToArrayAsync());
        var persisted = await dbContext.InboundOrders
            .Include(order => order.Lines)
            .SingleAsync();
        Assert.Equal("SITE-002", persisted.SiteCode);
        var persistedLine = Assert.Single(persisted.Lines);
        Assert.Equal("RM-TUB-02", persistedLine.SkuCode);
        Assert.Equal(2m, persistedLine.ReceivedQuantity);
    }

    private static InboundOrder CreateCanonicalInboundOrder() => InboundOrder.Create(
        "org-001",
        "env-dev",
        "IB-WMS-SEED-001",
        "wms-walkthrough-seed",
        "WMS-WALKTHROUGH-SEED-001",
        "SITE-001",
        [
            new InboundOrderLineDraft(
                "10",
                "RM-TUB-01",
                "kg",
                1m,
                "loc-raw-01",
                "LOT-WMS-SEED-001",
                SerialNo: null,
                "unrestricted",
                "company",
                OwnerId: null),
        ],
        assignedOperatorUserId: "user-emp-049",
        assignedPoolCode: "POOL-WMS-RECEIVING");

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value =>
                new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();

    private sealed class StaticTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
