using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Queries;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WarehouseOperationalCandidatesQueryTests
{
    private static readonly DateTimeOffset AsOf =
        new(2026, 7, 30, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Candidates_are_deduplicated_from_visible_operational_facts_with_explainable_metadata()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = nameof(
            Candidates_are_deduplicated_from_visible_operational_facts_with_explainable_metadata);
        await using (var seed = CreateContext(databaseName, root))
        {
            seed.InboundOrders.Add(Inbound(
                "IN-001",
                "SITE-A",
                "worker-a",
                "POOL-A",
                "SKU-001",
                "LOC-A",
                "LOT-001"));
            seed.OutboundOrders.Add(Outbound(
                "OUT-001",
                "SITE-A",
                "worker-a",
                "POOL-A",
                "SKU-001",
                "LOC-A",
                "LOT-001"));
            seed.WarehouseTasks.Add(WarehouseTask.CreatePutaway(
                "org-001",
                "env-dev",
                "PUT-001",
                "IN-001",
                "10",
                "SKU-002",
                "EA",
                "SITE-A",
                "LOC-A",
                "LOC-B",
                3m,
                "LOT-002",
                assignedOperatorUserId: "worker-a",
                assignedPoolCode: "POOL-A"));
            seed.CountExecutions.Add(CountExecution.Create(
                "org-001",
                "env-dev",
                "COUNT-001",
                "SKU-003",
                "EA",
                "SITE-A",
                "LOC-C",
                1m,
                assignedOperatorUserId: "worker-a",
                assignedPoolCode: "POOL-A"));
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext(databaseName, root);
        var result = await Handler(context).Handle(
            new ListWarehouseOperationalCandidatesQuery(
                "org-001",
                "env-dev",
                "self",
                "worker-a",
                AssignedOperatorUserIds: ["worker-a"],
                SiteCodes: ["SITE-A"],
                Take: 50),
            CancellationToken.None);

        Assert.Equal("wms-operational-facts", result.SourceKind);
        Assert.Equal("self", result.ScopeKind);
        Assert.Equal("worker-a", result.ScopeId);
        Assert.Equal(AsOf.UtcDateTime, result.AsOfUtc);
        Assert.NotNull(result.FreshnessUtc);
        Assert.False(result.Truncated);
        Assert.Equal(["LOC-A", "LOC-B", "LOC-C"], result.Locations
            .Select(candidate => candidate.LocationCode)
            .Order(StringComparer.Ordinal));

        var location = result.Locations.Single(candidate => candidate.LocationCode == "LOC-A");
        Assert.Equal("SITE-A", location.SiteCode);
        Assert.Equal(["SKU-001", "SKU-002"], location.SkuCodes);
        Assert.Equal(3, location.ReferenceCount);

        var lot = result.Lots.Single(candidate => candidate.LotNo == "LOT-001");
        Assert.Equal("SKU-001", lot.SkuCode);
        Assert.Equal(["LOC-A"], lot.LocationCodes);
        Assert.Equal(2, lot.ReferenceCount);
    }

    [Fact]
    public async Task Candidates_fail_closed_to_persisted_operator_pool_site_and_tenant_scope()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = nameof(
            Candidates_fail_closed_to_persisted_operator_pool_site_and_tenant_scope);
        await using (var seed = CreateContext(databaseName, root))
        {
            seed.InboundOrders.AddRange(
                Inbound(
                    "IN-MINE",
                    "SITE-A",
                    "worker-a",
                    "POOL-A",
                    "SKU-MINE",
                    "LOC-MINE",
                    "LOT-MINE"),
                Inbound(
                    "IN-OTHER-OPERATOR",
                    "SITE-A",
                    "worker-b",
                    "POOL-A",
                    "SKU-OTHER-OPERATOR",
                    "LOC-OTHER-OPERATOR",
                    "LOT-OTHER-OPERATOR"),
                Inbound(
                    "IN-OTHER-POOL",
                    "SITE-A",
                    "worker-a",
                    "POOL-B",
                    "SKU-OTHER-POOL",
                    "LOC-OTHER-POOL",
                    "LOT-OTHER-POOL"),
                Inbound(
                    "IN-OTHER-SITE",
                    "SITE-B",
                    "worker-a",
                    "POOL-A",
                    "SKU-OTHER-SITE",
                    "LOC-OTHER-SITE",
                    "LOT-OTHER-SITE"),
                Inbound(
                    "IN-OTHER-TENANT",
                    "SITE-A",
                    "worker-a",
                    "POOL-A",
                    "SKU-OTHER-TENANT",
                    "LOC-OTHER-TENANT",
                    "LOT-OTHER-TENANT",
                    organizationId: "org-002"));
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext(databaseName, root);
        var self = await Handler(context).Handle(
            new ListWarehouseOperationalCandidatesQuery(
                "org-001",
                "env-dev",
                "self",
                "worker-a",
                AssignedOperatorUserIds: ["worker-a"],
                SiteCodes: ["SITE-A"]),
            CancellationToken.None);
        Assert.Equal(
            ["LOC-MINE", "LOC-OTHER-POOL"],
            self.Locations.Select(candidate => candidate.LocationCode).Order(StringComparer.Ordinal));

        var pool = await Handler(context).Handle(
            new ListWarehouseOperationalCandidatesQuery(
                "org-001",
                "env-dev",
                "work-pool",
                "POOL-A",
                AssignedPoolCodes: ["POOL-A"],
                SiteCodes: ["SITE-A"]),
            CancellationToken.None);
        Assert.Equal(
            ["LOC-MINE", "LOC-OTHER-OPERATOR"],
            pool.Locations.Select(candidate => candidate.LocationCode).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            pool.Locations,
            candidate => candidate.LocationCode.Contains("OTHER-SITE", StringComparison.Ordinal));
        Assert.DoesNotContain(
            pool.Locations,
            candidate => candidate.LocationCode.Contains("OTHER-TENANT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lot_candidates_support_exact_sku_and_location_filters_and_bounded_search()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = nameof(
            Lot_candidates_support_exact_sku_and_location_filters_and_bounded_search);
        await using (var seed = CreateContext(databaseName, root))
        {
            seed.InboundOrders.AddRange(
                Inbound(
                    "IN-001",
                    "SITE-A",
                    "worker-a",
                    "POOL-A",
                    "SKU-001",
                    "LOC-A",
                    "LOT-001"),
                Inbound(
                    "IN-002",
                    "SITE-A",
                    "worker-a",
                    "POOL-A",
                    "SKU-001",
                    "LOC-B",
                    "LOT-002"),
                Inbound(
                    "IN-003",
                    "SITE-A",
                    "worker-a",
                    "POOL-A",
                    "SKU-002",
                    "LOC-A",
                    "LOT-003"));
            seed.WarehouseTasks.Add(WarehouseTask.CreateReplenishment(
                "org-001",
                "env-dev",
                "REPLENISH-001",
                "OUT-001",
                "10",
                "SKU-004",
                "EA",
                "SITE-A",
                "LOC-D",
                1m,
                assignedOperatorUserId: "worker-a",
                assignedPoolCode: "POOL-A"));
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateContext(databaseName, root);
        var result = await Handler(context).Handle(
            new ListWarehouseOperationalCandidatesQuery(
                "org-001",
                "env-dev",
                "self",
                "worker-a",
                AssignedOperatorUserIds: ["worker-a"],
                SiteCodes: ["SITE-A"],
                Keyword: "lot",
                SkuCode: "SKU-001",
                LocationCode: "LOC-A",
                Take: 50),
            CancellationToken.None);

        var lot = Assert.Single(result.Lots);
        Assert.Equal("LOT-001", lot.LotNo);
        Assert.Equal("SKU-001", lot.SkuCode);
        Assert.Equal(["LOC-A"], lot.LocationCodes);
        Assert.False(result.Truncated);
        Assert.DoesNotContain(
            result.Locations,
            candidate => candidate.LocationCode == "REPLENISHMENT-SOURCE-PENDING");

        var bounded = await Handler(context).Handle(
            new ListWarehouseOperationalCandidatesQuery(
                "org-001",
                "env-dev",
                "self",
                "worker-a",
                AssignedOperatorUserIds: ["worker-a"],
                SiteCodes: ["SITE-A"],
                Take: 1),
            CancellationToken.None);
        Assert.Single(bounded.Locations);
        Assert.Single(bounded.Lots);
        Assert.True(bounded.Truncated);
    }

    private static ListWarehouseOperationalCandidatesQueryHandler Handler(
        ApplicationDbContext context) =>
        new(context, new FixedTimeProvider(AsOf));

    private static ApplicationDbContext CreateContext(
        string databaseName,
        InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static InboundOrder Inbound(
        string orderNo,
        string siteCode,
        string operatorPrincipalId,
        string poolCode,
        string skuCode,
        string locationCode,
        string lotNo,
        string organizationId = "org-001") =>
        InboundOrder.Create(
            organizationId,
            "env-dev",
            orderNo,
            "asn",
            $"SRC-{orderNo}",
            siteCode,
            [
                new InboundOrderLineDraft(
                    "10",
                    skuCode,
                    "EA",
                    1m,
                    locationCode,
                    lotNo,
                    null,
                    "unrestricted",
                    "company",
                    null),
            ],
            operatorPrincipalId,
            poolCode);

    private static OutboundOrder Outbound(
        string orderNo,
        string siteCode,
        string operatorPrincipalId,
        string poolCode,
        string skuCode,
        string locationCode,
        string lotNo) =>
        OutboundOrder.Create(
            "org-001",
            "env-dev",
            orderNo,
            "delivery",
            $"SRC-{orderNo}",
            siteCode,
            [
                new OutboundOrderLineDraft(
                    "10",
                    skuCode,
                    "EA",
                    1m,
                    locationCode,
                    lotNo,
                    null,
                    "unrestricted",
                    "company",
                    null),
            ],
            operatorPrincipalId,
            poolCode);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
