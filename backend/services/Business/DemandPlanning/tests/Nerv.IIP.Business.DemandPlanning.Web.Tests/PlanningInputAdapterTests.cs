using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.ForecastInputAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Commands;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class PlanningInputAdapterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Fixture_adapter_returns_snapshots_without_cross_service_table_access()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var snapshot = await new DemandPlanningFixtureInputSnapshotProvider(dbContext).GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Equal("fixture-production-engineering-snapshot", snapshot.ProductionEngineeringSnapshotSource);
        Assert.Equal("fixture-inventory-availability-snapshot", snapshot.InventorySnapshotSource);
        Assert.Single(snapshot.Demands);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-FG-1000" && x.AvailableQuantity == 2m);
        Assert.DoesNotContain(dbContext.Model.GetEntityTypes(), x => x.ClrType.FullName?.Contains("ProductEngineering", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(dbContext.Model.GetEntityTypes(), x => x.ClrType.FullName?.Contains("Inventory", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Upstream_adapter_uses_product_engineering_and_inventory_snapshots_for_mrp_inputs()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var engineering = new FakePlanningProductEngineeringClient();
        var inventory = new FakePlanningInventoryClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(dbContext, engineering, inventory);

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Equal("product-engineering-http:2", snapshot.ProductionEngineeringSnapshotSource);
        Assert.Equal("inventory-http:2;scheduled-receipts:none;master-data-planning-parameters:none", snapshot.InventorySnapshotSource);
        Assert.Contains(snapshot.ProductionVersions, x => x.ParentSkuCode == "SKU-FG-1000" && x.ProductionVersionReference == "PV-REAL-001");
        Assert.Contains(snapshot.ProductionVersions, x => x.ParentSkuCode == "SKU-FG-1000" && x.LotSizeMin == 10m && x.LotSizeMax == 50m);
        Assert.Contains(snapshot.BomComponents, x => x.ParentSkuCode == "SKU-FG-1000" && x.ComponentSkuCode == "SKU-RM-1000" && x.QuantityPerParent == 3m);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-FG-1000" && x.AvailableQuantity == 2m);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-RM-1000" && x.AvailableQuantity == 5m);
        Assert.Empty(snapshot.ScheduledReceipts);
        Assert.Equal(["SKU-FG-1000", "SKU-RM-1000"], engineering.RequestedParentSkuCodes);
        Assert.Equal(["SKU-FG-1000", "SKU-RM-1000"], inventory.RequestedSkuCodes);
    }

    [Fact]
    public async Task Upstream_adapter_excludes_cancelled_sales_order_demands_from_mrp_inputs()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var demand = DemandSource.CreateSalesOrderDemand(
            "org-001", "env-dev", "sales-order-id-001", "SO-DEMO-001", "10", "CUST-001",
            "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1), 1);
        demand.CancelFromSalesOrder(2);
        dbContext.DemandSources.Add(demand);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var snapshot = await new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient()).GetSnapshotAsync(
                "org-001", "env-dev", new DateOnly(2026, 5, 25), new DateOnly(2026, 6, 30), CancellationToken.None);

        Assert.DoesNotContain(snapshot.Demands, x => x.DemandSourceReference == "SO-DEMO-001");
    }

    [Fact]
    public async Task Inventory_snapshot_client_preserves_on_hand_and_reserved_quantities_for_explanations()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "success": true,
              "message": "ok",
              "code": 0,
              "data": {
                "organizationId": "org-001",
                "environmentId": "env-dev",
                "skuCode": "SKU-FG-1000",
                "uomCode": "pcs",
                "siteCode": "SITE-01",
                "locationCode": null,
                "lotNo": null,
                "serialNo": null,
                "qualityStatus": null,
                "ownerType": null,
                "ownerId": null,
                "onHandQuantity": 10,
                "reservedQuantity": 3,
                "availableQuantity": 7
              }
            }
            """));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://inventory.test") };
        var client = new HttpPlanningInventorySnapshotClient(httpClient);

        var snapshot = await client.GetAvailabilitySnapshotAsync(
            "token",
            new PlanningInventorySnapshotRequest(
                "org-001",
                "env-dev",
                [new PlanningInventorySnapshotItem("SKU-FG-1000", "pcs", "SITE-01")]),
            CancellationToken.None);

        var item = Assert.Single(snapshot.Availability);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(item, JsonOptions));
        Assert.Equal(10m, document.RootElement.GetProperty("onHandQuantity").GetDecimal());
        Assert.Equal(3m, document.RootElement.GetProperty("reservedQuantity").GetDecimal());
        Assert.Equal(7m, document.RootElement.GetProperty("availableQuantity").GetDecimal());
    }

    [Fact]
    public async Task Upstream_adapter_includes_only_released_mps_buckets_as_mrp_inputs_with_source_type()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var released = MasterProductionSchedule.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 6, 10),
            12m);
        released.MarkReviewed("planner.li");
        released.Release("planning.manager");
        var draft = MasterProductionSchedule.Create(
            "org-001",
            "env-dev",
            "SKU-FG-2000",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 6, 12),
            8m);
        dbContext.MasterProductionSchedules.AddRange(released, draft);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var engineering = new FakePlanningProductEngineeringClient();
        var inventory = new FakePlanningInventoryClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(dbContext, engineering, inventory);

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        var demand = Assert.Single(snapshot.Demands);
        Assert.Equal("mps", demand.SourceType);
        Assert.StartsWith("MPS:", demand.DemandSourceReference, StringComparison.Ordinal);
        Assert.Equal("SKU-FG-1000", demand.SkuCode);
        Assert.Equal(12m, demand.Quantity);
        Assert.Equal(new DateOnly(2026, 6, 10), demand.DueDate);
        Assert.DoesNotContain(snapshot.Demands, x => x.SkuCode == "SKU-FG-2000");
        Assert.Contains("SKU-FG-1000", engineering.RequestedParentSkuCodes);
        Assert.Contains("SKU-FG-1000", inventory.RequestedSkuCodes);
    }

    [Fact]
    public async Task Forecast_input_command_creates_and_lists_forecast_periods()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateOrUpdateForecastInputCommandHandler(dbContext);

        var id = await handler.Handle(NewForecastCommand(), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var forecasts = await new ListForecastInputsQueryHandler(dbContext)
            .Handle(new ListForecastInputsQuery("org-001", "env-dev", "SKU-FG-1000", "SITE-01", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)), CancellationToken.None);

        Assert.NotEqual(default, id);
        var forecast = Assert.Single(forecasts);
        Assert.Equal("FC-2026-06-SKU-FG-1000", forecast.ForecastReference);
        Assert.Equal(new DateOnly(2026, 6, 1), forecast.PeriodStartDate);
        Assert.Equal(new DateOnly(2026, 6, 30), forecast.PeriodEndDate);
        Assert.Equal(10m, forecast.Quantity);
        Assert.Equal(7, forecast.BackwardConsumptionDays);
        Assert.Equal(3, forecast.ForwardConsumptionDays);
    }

    [Fact]
    public async Task Forecast_time_phasing_returns_each_day_when_forecast_is_fully_inside_horizon()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-INSIDE",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            3m);

        var snapshot = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3));

        AssertForecastFacts(
            snapshot,
            "FC-INSIDE",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            [
                (new DateOnly(2026, 7, 1), 1.000000m),
                (new DateOnly(2026, 7, 2), 1.000000m),
                (new DateOnly(2026, 7, 3), 1.000000m),
            ]);
    }

    [Fact]
    public async Task Forecast_time_phasing_returns_only_left_crossing_overlap()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-LEFT",
            new DateOnly(2026, 6, 29),
            new DateOnly(2026, 7, 2),
            4m);

        var snapshot = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        AssertForecastFacts(
            snapshot,
            "FC-LEFT",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            [
                (new DateOnly(2026, 7, 1), 1.000000m),
                (new DateOnly(2026, 7, 2), 1.000000m),
            ]);
        Assert.Equal(2.000000m, snapshot.Demands.Where(x => x.SourceType == "forecast").Sum(x => x.Quantity));
    }

    [Fact]
    public async Task Forecast_time_phasing_replaces_right_cross_horizon_full_quantity_clamp()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-2026-Q3-SKU-FG-1000",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 9, 30),
            90m,
            7,
            7);

        var snapshot = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        var facts = snapshot.Demands.Where(x => x.SourceType == "forecast").ToArray();
        Assert.Equal(31, facts.Length);
        Assert.Equal(30.326087m, facts.Sum(x => x.Quantity));
        Assert.Equal(new DateOnly(2026, 7, 1), facts[0].DueDate);
        Assert.Equal(0.978261m, facts[0].Quantity);
        Assert.Equal(new DateOnly(2026, 7, 31), facts[^1].DueDate);
        Assert.All(facts, x => Assert.Equal("FC-2026-Q3-SKU-FG-1000", x.DemandSourceReference));
        AssertForecastFactInvariants(facts, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
    }

    [Fact]
    public async Task Forecast_time_phasing_returns_daily_share_when_forecast_covers_horizon()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-COVERS-JULY",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 8, 31),
            92m);

        var snapshot = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        var facts = snapshot.Demands.Where(x => x.SourceType == "forecast").ToArray();
        Assert.Equal(31, facts.Length);
        Assert.Equal(31.000000m, facts.Sum(x => x.Quantity));
        Assert.All(facts, x => Assert.Equal(1.000000m, x.Quantity));
        AssertForecastFactInvariants(facts, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
    }

    [Fact]
    public async Task Forecast_time_phasing_omits_forecast_fully_outside_horizon()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-OUTSIDE",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            30m);

        var snapshot = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        Assert.DoesNotContain(snapshot.Demands, x => x.SourceType == "forecast");
    }

    [Fact]
    public async Task Forecast_time_phasing_preserves_complete_quantity_for_single_day_period()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-SINGLE-DAY",
            new DateOnly(2026, 7, 15),
            new DateOnly(2026, 7, 15),
            1.234567m);

        var snapshot = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31));

        AssertForecastFacts(
            snapshot,
            "FC-SINGLE-DAY",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 31),
            [(new DateOnly(2026, 7, 15), 1.234567m)]);
    }

    [Fact]
    public async Task Forecast_time_phasing_handles_leap_day_and_month_boundary_across_horizons()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-LEAP-EDGE",
            new DateOnly(2024, 2, 28),
            new DateOnly(2024, 3, 1),
            3m);

        var february = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2024, 2, 1),
            new DateOnly(2024, 2, 29));
        var march = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2024, 3, 1),
            new DateOnly(2024, 3, 31));

        AssertForecastFacts(
            february,
            "FC-LEAP-EDGE",
            new DateOnly(2024, 2, 1),
            new DateOnly(2024, 2, 29),
            [
                (new DateOnly(2024, 2, 28), 1.000000m),
                (new DateOnly(2024, 2, 29), 1.000000m),
            ]);
        AssertForecastFacts(
            march,
            "FC-LEAP-EDGE",
            new DateOnly(2024, 3, 1),
            new DateOnly(2024, 3, 31),
            [(new DateOnly(2024, 3, 1), 1.000000m)]);
    }

    [Fact]
    public async Task Forecast_time_phasing_balances_indivisible_micro_units_without_zero_or_negative_facts()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-INDIVISIBLE",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            1m);

        var snapshot = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3));

        AssertForecastFacts(
            snapshot,
            "FC-INDIVISIBLE",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            [
                (new DateOnly(2026, 7, 1), 0.333333m),
                (new DateOnly(2026, 7, 2), 0.333334m),
                (new DateOnly(2026, 7, 3), 0.333333m),
            ]);
        var facts = snapshot.Demands.Where(x => x.SourceType == "forecast").ToArray();
        Assert.Equal(1.000000m, facts.Sum(x => x.Quantity));
        Assert.All(facts, x => Assert.True(x.Quantity > 0m));
    }

    [Fact]
    public async Task Forecast_time_phasing_keeps_adjacent_horizons_stable_after_consumption_outside_first_slice()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-ADJACENT",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 6),
            6m);
        dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-001",
            "env-dev",
            "sales-order-id-adjacent",
            "SO-ADJACENT",
            "10",
            "CUST-001",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            2m,
            new DateOnly(2026, 7, 5),
            1));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var first = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3));
        var second = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 4),
            new DateOnly(2026, 7, 6));

        var firstFacts = first.Demands.Where(x => x.SourceType == "forecast").ToArray();
        var secondFacts = second.Demands.Where(x => x.SourceType == "forecast").ToArray();
        Assert.Equal(
            [
                (new DateOnly(2026, 7, 1), 0.666667m),
                (new DateOnly(2026, 7, 2), 0.666666m),
                (new DateOnly(2026, 7, 3), 0.666667m),
            ],
            firstFacts.Select(x => (x.DueDate, x.Quantity)).ToArray());
        Assert.Equal(
            [
                (new DateOnly(2026, 7, 4), 0.666667m),
                (new DateOnly(2026, 7, 5), 0.666666m),
                (new DateOnly(2026, 7, 6), 0.666667m),
            ],
            secondFacts.Select(x => (x.DueDate, x.Quantity)).ToArray());
        Assert.Equal(4.000000m, firstFacts.Concat(secondFacts).Sum(x => x.Quantity));
        Assert.DoesNotContain(first.Demands, x => x.DemandSourceReference == "SO-ADJACENT");
        var salesOrder = Assert.Single(second.Demands, x => x.DemandSourceReference == "SO-ADJACENT");
        Assert.Equal(2m, salesOrder.Quantity);
        Assert.Equal(new DateOnly(2026, 7, 5), salesOrder.DueDate);
    }

    [Fact]
    public async Task Forecast_discovery_review_requests_alternate_sales_uom_across_adjacent_horizons()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-ALT-SALES",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 6),
            6m);
        dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-001",
            "env-dev",
            "sales-order-id-alt-uom",
            "SO-ALT-UOM",
            "10",
            "CUST-001",
            "SKU-FG-1000",
            "box",
            "SITE-01",
            1m,
            new DateOnly(2026, 7, 5),
            1));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var planningParameters = new RequestAwareBoxPlanningParameterClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            null,
            planningParameters);

        var first = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            CancellationToken.None);
        var second = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 7, 4),
            new DateOnly(2026, 7, 6),
            CancellationToken.None);

        Assert.Equal(2, planningParameters.RequestedUomCodes.Count);
        Assert.All(planningParameters.RequestedUomCodes, request => Assert.Contains("box", request));
        Assert.All(
            first.Demands.Concat(second.Demands).Where(x => x.SourceType == "forecast"),
            x => Assert.Equal("pcs", x.UomCode));
        Assert.Equal(
            [
                (new DateOnly(2026, 7, 1), 0.666667m),
                (new DateOnly(2026, 7, 2), 0.666666m),
                (new DateOnly(2026, 7, 3), 0.666667m),
            ],
            first.Demands
                .Where(x => x.SourceType == "forecast")
                .Select(x => (x.DueDate, x.Quantity))
                .ToArray());
        Assert.Equal(
            [
                (new DateOnly(2026, 7, 4), 0.666667m),
                (new DateOnly(2026, 7, 5), 0.666666m),
                (new DateOnly(2026, 7, 6), 0.666667m),
            ],
            second.Demands
                .Where(x => x.SourceType == "forecast")
                .Select(x => (x.DueDate, x.Quantity))
                .ToArray());
        Assert.Equal(
            4.000000m,
            first.Demands
                .Concat(second.Demands)
                .Where(x => x.SourceType == "forecast")
                .Sum(x => x.Quantity));
        Assert.DoesNotContain(first.Demands, x => x.DemandSourceReference == "SO-ALT-UOM");
        var salesOrder = Assert.Single(second.Demands, x => x.DemandSourceReference == "SO-ALT-UOM");
        Assert.Equal("box", salesOrder.UomCode);
        Assert.Equal(1m, salesOrder.Quantity);
    }

    [Fact]
    public async Task Forecast_discovery_review_requests_alternate_released_mps_uom_across_adjacent_horizons()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-ALT-MPS",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 6),
            6m);
        var mps = MasterProductionSchedule.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "box",
            "SITE-01",
            new DateOnly(2026, 7, 5),
            1m);
        mps.MarkReviewed("planner.li");
        mps.Release("planning.manager");
        dbContext.MasterProductionSchedules.Add(mps);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var planningParameters = new RequestAwareBoxPlanningParameterClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            null,
            planningParameters);

        var first = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            CancellationToken.None);
        var second = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 7, 4),
            new DateOnly(2026, 7, 6),
            CancellationToken.None);

        Assert.Equal(2, planningParameters.RequestedUomCodes.Count);
        Assert.All(planningParameters.RequestedUomCodes, request => Assert.Contains("box", request));
        Assert.All(
            first.Demands.Concat(second.Demands).Where(x => x.SourceType == "forecast"),
            x => Assert.Equal("pcs", x.UomCode));
        Assert.Equal(
            [
                (new DateOnly(2026, 7, 1), 0.666667m),
                (new DateOnly(2026, 7, 2), 0.666666m),
                (new DateOnly(2026, 7, 3), 0.666667m),
            ],
            first.Demands
                .Where(x => x.SourceType == "forecast")
                .Select(x => (x.DueDate, x.Quantity))
                .ToArray());
        Assert.Equal(
            [
                (new DateOnly(2026, 7, 4), 0.666667m),
                (new DateOnly(2026, 7, 5), 0.666666m),
                (new DateOnly(2026, 7, 6), 0.666667m),
            ],
            second.Demands
                .Where(x => x.SourceType == "forecast")
                .Select(x => (x.DueDate, x.Quantity))
                .ToArray());
        Assert.Equal(
            2.000000m,
            first.Demands.Where(x => x.SourceType == "forecast").Sum(x => x.Quantity));
        Assert.Equal(
            2.000000m,
            second.Demands.Where(x => x.SourceType == "forecast").Sum(x => x.Quantity));
        Assert.Equal(
            4.000000m,
            first.Demands
                .Concat(second.Demands)
                .Where(x => x.SourceType == "forecast")
                .Sum(x => x.Quantity));
        Assert.DoesNotContain(first.Demands, x => x.SourceType == "mps");
        var releasedMps = Assert.Single(second.Demands, x => x.SourceType == "mps");
        Assert.Equal("box", releasedMps.UomCode);
        Assert.Equal(1m, releasedMps.Quantity);
        Assert.Equal(new DateOnly(2026, 7, 5), releasedMps.DueDate);
    }

    [Fact]
    public async Task Forecast_discovery_review_redistributes_consumed_sub_micro_forecast_into_requested_day()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-SUB-MICRO",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            0.000002m);
        dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-001",
            "env-dev",
            "sales-order-id-sub-micro",
            "SO-SUB-MICRO",
            "10",
            "CUST-001",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            0.000001m,
            new DateOnly(2026, 7, 1),
            1));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var first = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 2),
            new DateOnly(2026, 7, 2));
        dbContext.ChangeTracker.Clear();
        var second = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 2),
            new DateOnly(2026, 7, 2));

        var expected = new DemandSnapshot(
            "FC-SUB-MICRO",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            0.000001m,
            new DateOnly(2026, 7, 2),
            "forecast");
        Assert.Equal(expected, Assert.Single(first.Demands));
        Assert.Equal(first.Demands, second.Demands);
        Assert.Equal(0.000001m, first.Demands.Sum(x => x.Quantity));
        Assert.DoesNotContain(first.Demands, x => x.DemandSourceReference == "SO-SUB-MICRO");
    }

    [Fact]
    public async Task Forecast_discovery_review_saturates_backward_window_at_dateonly_minimum()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-DATE-MIN",
            DateOnly.MinValue,
            DateOnly.MinValue,
            2m,
            backwardConsumptionDays: 1);
        dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-001",
            "env-dev",
            "sales-order-id-date-min",
            "SO-DATE-MIN",
            "10",
            "CUST-001",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            1m,
            DateOnly.MinValue,
            1));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var snapshot = await ReadSnapshotAsync(dbContext, DateOnly.MinValue, DateOnly.MinValue);

        AssertForecastFacts(
            snapshot,
            "FC-DATE-MIN",
            DateOnly.MinValue,
            DateOnly.MinValue,
            [(DateOnly.MinValue, 1.000000m)]);
        Assert.Equal(
            1m,
            Assert.Single(snapshot.Demands, x => x.DemandSourceReference == "SO-DATE-MIN").Quantity);
    }

    [Fact]
    public async Task Forecast_discovery_review_saturates_forward_window_at_dateonly_maximum()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-DATE-MAX",
            DateOnly.MaxValue,
            DateOnly.MaxValue,
            2m,
            forwardConsumptionDays: 1);
        dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-001",
            "env-dev",
            "sales-order-id-date-max",
            "SO-DATE-MAX",
            "10",
            "CUST-001",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            1m,
            DateOnly.MaxValue,
            1));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var snapshot = await ReadSnapshotAsync(dbContext, DateOnly.MaxValue, DateOnly.MaxValue);

        AssertForecastFacts(
            snapshot,
            "FC-DATE-MAX",
            DateOnly.MaxValue,
            DateOnly.MaxValue,
            [(DateOnly.MaxValue, 1.000000m)]);
        Assert.Equal(
            1m,
            Assert.Single(snapshot.Demands, x => x.DemandSourceReference == "SO-DATE-MAX").Quantity);
    }

    [Fact]
    public async Task Forecast_time_phasing_preserves_ordinary_sales_order_fact()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-ORDINARY",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3),
            3m);
        dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-001",
            "env-dev",
            "sales-order-id-ordinary",
            "SO-ORDINARY",
            "20",
            "CUST-001",
            "SKU-FG-1000",
            "box",
            "SITE-01",
            2.345678m,
            new DateOnly(2026, 7, 2),
            1));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var snapshot = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 3));

        Assert.Equal(
            new DemandSnapshot(
                "SO-ORDINARY",
                "SKU-FG-1000",
                "box",
                "SITE-01",
                2.345678m,
                new DateOnly(2026, 7, 2),
                "sales-order"),
            Assert.Single(snapshot.Demands, x => x.DemandSourceReference == "SO-ORDINARY"));
    }

    [Fact]
    public async Task Forecast_time_phasing_repeated_reads_are_ordered_identically_without_duplicate_reference_dates()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await AddForecastAsync(
            dbContext,
            "FC-REPEAT",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 7),
            1m);

        var first = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 7));
        dbContext.ChangeTracker.Clear();
        var second = await ReadSnapshotAsync(
            dbContext,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 7));

        var firstFacts = first.Demands.Where(x => x.SourceType == "forecast").ToArray();
        var secondFacts = second.Demands.Where(x => x.SourceType == "forecast").ToArray();
        Assert.Equal(7, firstFacts.Length);
        Assert.Equal(firstFacts, secondFacts);
        Assert.Equal(
            firstFacts.Length,
            firstFacts.Select(x => (x.DemandSourceReference, x.DueDate)).Distinct().Count());
        AssertForecastFactInvariants(firstFacts, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 7));
    }

    [Fact]
    public async Task Upstream_adapter_consumes_forecast_with_overlapping_sales_orders()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateForecastInputCommandHandler(dbContext).Handle(NewForecastCommand(), CancellationToken.None);
        dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-001", "env-dev", "sales-order-id-1000", "SO-1000", "10", "CUST-001",
            "SKU-FG-1000", "pcs", "SITE-01", 4m, new DateOnly(2026, 6, 15), 1));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient());

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Contains(snapshot.Demands, x => x.SourceType == "sales-order" && x.DemandSourceReference == "SO-1000" && x.Quantity == 4m);
        var forecast = snapshot.Demands.Where(x => x.SourceType == "forecast").ToArray();
        Assert.Equal(30, forecast.Length);
        Assert.Equal(6m, forecast.Sum(x => x.Quantity));
        Assert.All(forecast, x => Assert.Equal("FC-2026-06-SKU-FG-1000", x.DemandSourceReference));
    }

    [Fact]
    public async Task Upstream_adapter_consumes_forecast_after_normalizing_sales_order_uom()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateForecastInputCommandHandler(dbContext).Handle(NewForecastCommand(), CancellationToken.None);
        dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-001", "env-dev", "sales-order-id-box", "SO-BOX", "10", "CUST-001",
            "SKU-FG-1000", "box", "SITE-01", 1m, new DateOnly(2026, 6, 15), 1));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            null,
            new BoxPlanningParameterClient());

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Contains(snapshot.Demands, x => x.SourceType == "sales-order" && x.DemandSourceReference == "SO-BOX" && x.UomCode == "box" && x.Quantity == 1m);
        Assert.DoesNotContain(snapshot.Demands, x => x.SourceType == "forecast");
    }

    [Fact]
    public async Task Upstream_adapter_consumes_forecast_with_released_mps_buckets()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateForecastInputCommandHandler(dbContext).Handle(NewForecastCommand(), CancellationToken.None);
        var mps = MasterProductionSchedule.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 6, 20),
            4m);
        mps.MarkReviewed("planner.li");
        mps.Release("planning.manager");
        dbContext.MasterProductionSchedules.Add(mps);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient());

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Contains(snapshot.Demands, x => x.SourceType == "mps" && x.Quantity == 4m);
        var forecast = snapshot.Demands.Where(x => x.SourceType == "forecast").ToArray();
        Assert.Equal(30, forecast.Length);
        Assert.Equal(6m, forecast.Sum(x => x.Quantity));
    }

    [Fact]
    public async Task Upstream_adapter_omits_fully_consumed_forecast()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateForecastInputCommandHandler(dbContext).Handle(NewForecastCommand(), CancellationToken.None);
        dbContext.DemandSources.Add(DemandSource.CreateSalesOrderDemand(
            "org-001", "env-dev", "sales-order-id-1000", "SO-1000", "10", "CUST-001",
            "SKU-FG-1000", "pcs", "SITE-01", 12m, new DateOnly(2026, 6, 15), 1));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient());

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.DoesNotContain(snapshot.Demands, x => x.SourceType == "forecast");
        Assert.Contains(snapshot.Demands, x => x.SourceType == "sales-order" && x.Quantity == 12m);
    }

    [Fact]
    public async Task Upstream_adapter_adds_master_data_planning_parameters_for_requested_items()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var planningParameters = new FakePlanningParameterClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            null,
            planningParameters);

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Equal("inventory-http:2;scheduled-receipts:none;master-data-planning-parameters:2;master-data-uom-conversions:0", snapshot.InventorySnapshotSource);
        Assert.Contains(snapshot.PlanningParameters, x =>
            x.SkuCode == "SKU-FG-1000"
            && x.LeadTimeDays == 6
            && x.SafetyStockQuantity == 4m
            && x.LotSizeMin == 10m
            && x.LotSizeMax == 50m
            && x.LotSizeMultiple == 5m);
        Assert.Contains(snapshot.PlanningParameters, x =>
            x.SkuCode == "SKU-RM-1000"
            && x.LeadTimeDays == 3
            && x.SafetyStockQuantity == 2m
            && x.LotSizeMultiple == 10m);
        Assert.Equal(["SKU-FG-1000", "SKU-RM-1000"], planningParameters.RequestedSkuCodes);
    }

    [Fact]
    public async Task Upstream_adapter_adds_erp_open_purchase_order_lines_as_scheduled_receipts()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var erp = new FakePlanningErpScheduledReceiptClient();
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            erp);

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        var receipt = Assert.Single(snapshot.ScheduledReceipts);
        Assert.Equal("SKU-RM-1000", receipt.SkuCode);
        Assert.Equal("pcs", receipt.UomCode);
        Assert.Equal("SITE-01", receipt.SiteCode);
        Assert.Equal(7m, receipt.Quantity);
        Assert.Equal("erp", receipt.SourceSystem);
        Assert.Equal("purchase-order", receipt.SourceDocumentType);
        Assert.Equal("PO-1000:10", receipt.SourceDocumentId);
        Assert.Equal(["SKU-FG-1000", "SKU-RM-1000"], erp.RequestedSkuCodes);
    }

    [Fact]
    public async Task Upstream_adapter_degrades_optional_planning_sources_when_they_fail()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            new ThrowingPlanningErpScheduledReceiptClient(),
            new ThrowingPlanningParameterClient());

        var snapshot = await providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None);

        Assert.Equal("inventory-http:2;scheduled-receipts:error;master-data-planning-parameters:error", snapshot.InventorySnapshotSource);
        Assert.Empty(snapshot.ScheduledReceipts);
        Assert.Empty(snapshot.PlanningParameters);
        Assert.Contains(snapshot.Availability, x => x.SkuCode == "SKU-FG-1000");
        Assert.Contains(snapshot.ProductionVersions, x => x.ParentSkuCode == "SKU-FG-1000");
    }

    [Fact]
    public async Task Upstream_adapter_propagates_truncated_uom_conversion_source()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "SO-1000", "SKU-FG-1000", "box", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            null,
            new TruncatedUomConversionPlanningParameterClient());

        var exception = await Assert.ThrowsAsync<RequiredPlanningSnapshotException>(() => providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None));

        Assert.Contains("truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upstream_adapter_propagates_unexpected_erp_invalid_operation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            new BuggyPlanningErpScheduledReceiptClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None));

        Assert.Equal("ERP optional source bug", exception.Message);
    }

    [Fact]
    public async Task Upstream_adapter_propagates_unexpected_master_data_invalid_operation()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new CreateOrUpdateDemandSourceCommandHandler(dbContext).Handle(
            new CreateOrUpdateDemandSourceCommand("org-001", "env-dev", "manual", "SO-1000", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var providerUnderTest = new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient(),
            null,
            new BuggyPlanningParameterClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => providerUnderTest.GetSnapshotAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            CancellationToken.None));

        Assert.Equal("MasterData optional source bug", exception.Message);
    }

    [Fact]
    public async Task Erp_scheduled_receipt_client_wraps_empty_response_envelope_as_optional_snapshot_failure()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse("""
            {
              "success": true,
              "message": "ok",
              "code": 0,
              "data": null
            }
            """));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var client = new HttpPlanningErpScheduledReceiptSnapshotClient(httpClient);

        var exception = await Assert.ThrowsAsync<OptionalPlanningSnapshotException>(() => client.GetScheduledReceiptsAsync(
            "token",
            new PlanningScheduledReceiptSnapshotRequest(
                "org-001",
                "env-dev",
                new DateOnly(2026, 5, 25),
                new DateOnly(2026, 6, 30),
                [new PlanningScheduledReceiptSnapshotItem("SKU-RM-1000", "pcs", "SITE-01")]),
            CancellationToken.None));

        Assert.Contains("ERP", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_wraps_invalid_json_as_optional_snapshot_failure()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var exception = await Assert.ThrowsAsync<OptionalPlanningSnapshotException>(() => client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [new PlanningParameterSnapshotItem("SKU-FG-1000", "pcs", "SITE-01")]),
            CancellationToken.None));

        Assert.Contains("MasterData", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_skips_empty_single_sku_envelope()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/sku/SKU-MISSING", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": null
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "SKU-RM-1000",
                    "displayName": "Raw material",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs",
                    "plannedDeliveryTimeDays": 3,
                    "safetyStockQuantity": 2
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var snapshot = await client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [
                    new PlanningParameterSnapshotItem("SKU-MISSING", "pcs", "SITE-01"),
                    new PlanningParameterSnapshotItem("SKU-RM-1000", "pcs", "SITE-01"),
                ]),
            CancellationToken.None);

        var parameter = Assert.Single(snapshot.PlanningParameters);
        Assert.Equal("master-data-planning-parameters:1;master-data-uom-conversions:0", snapshot.SnapshotSource);
        Assert.Equal("SKU-RM-1000", parameter.SkuCode);
    }

    [Fact]
    public async Task Erp_scheduled_receipt_client_maps_open_purchase_order_lines_across_pages()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Contains("status=Released", request.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("take=500", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);

            if (request.RequestUri.Query.Contains("skip=500", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "total": 501,
                        "items": [
                          {
                            "purchaseOrderNo": "PO-501",
                            "supplierCode": "SUP-001",
                            "siteCode": "SITE-01",
                            "status": "Released",
                            "totalAmount": 0,
                            "lines": [
                              { "lineNo": "10", "skuCode": "SKU-RM-1000", "uomCode": "pcs", "orderedQuantity": 9, "receivedQuantity": 2, "unitPrice": 1, "promisedDate": "2026-05-24" }
                            ]
                          }
                        ]
                      }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "total": 501,
                    "items": [
                      {
                        "purchaseOrderNo": "PO-100",
                        "supplierCode": "SUP-001",
                        "siteCode": "SITE-01",
                        "status": "Released",
                        "totalAmount": 0,
                        "lines": [
                          { "lineNo": "10", "skuCode": "SKU-RM-1000", "uomCode": "pcs", "orderedQuantity": 12, "receivedQuantity": 5, "unitPrice": 1, "promisedDate": "2026-05-30" },
                          { "lineNo": "20", "skuCode": "SKU-RM-1000", "uomCode": "pcs", "orderedQuantity": 5, "receivedQuantity": 5, "unitPrice": 1, "promisedDate": "2026-05-30" },
                          { "lineNo": "30", "skuCode": "SKU-RM-1000", "uomCode": "kg", "orderedQuantity": 4, "receivedQuantity": 0, "unitPrice": 1, "promisedDate": "2026-05-30" },
                          { "lineNo": "40", "skuCode": "SKU-RM-1000", "uomCode": "pcs", "orderedQuantity": 4, "receivedQuantity": 0, "unitPrice": 1, "promisedDate": "2026-07-01" },
                          { "lineNo": "50", "skuCode": "SKU-OTHER", "uomCode": "pcs", "orderedQuantity": 4, "receivedQuantity": 0, "unitPrice": 1, "promisedDate": "2026-05-30" }
                        ]
                      }
                    ]
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://erp.test") };
        var client = new HttpPlanningErpScheduledReceiptSnapshotClient(httpClient);

        var snapshot = await client.GetScheduledReceiptsAsync(
            "token",
            new PlanningScheduledReceiptSnapshotRequest(
                "org-001",
                "env-dev",
                new DateOnly(2026, 5, 25),
                new DateOnly(2026, 6, 30),
                [new PlanningScheduledReceiptSnapshotItem("sku-rm-1000", "PCS", "site-01")]),
            CancellationToken.None);

        Assert.Equal("erp-purchase-orders:2", snapshot.SnapshotSource);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([7m, 7m], snapshot.ScheduledReceipts.Select(x => x.Quantity).ToArray());
        Assert.Contains(snapshot.ScheduledReceipts, x =>
            x.SourceDocumentId == "PO-100:10"
            && x.ExpectedReceiptDate == new DateOnly(2026, 5, 30));
        Assert.Contains(snapshot.ScheduledReceipts, x =>
            x.SourceDocumentId == "PO-501:10"
            && x.ExpectedReceiptDate == new DateOnly(2026, 5, 24));
    }

    [Fact]
    public async Task Mes_scheduled_receipt_client_maps_open_work_order_remaining_output()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("/api/business/v1/mes/work-orders", request.RequestUri!.AbsolutePath);
            Assert.Contains("organizationId=org-001", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("environmentId=env-dev", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "total": 4,
                    "items": [
                      {
                        "workOrderId": "WO-OPEN-001",
                        "skuId": "SKU-FG-1000",
                        "skuCode": "SKU-FG-1000",
                        "uomCode": "pcs",
                        "quantity": 10,
                        "completedQuantity": 4,
                        "priority": 5,
                        "dueUtc": "2026-05-31T00:00:00Z",
                        "status": "started",
                        "operationTasks": []
                      },
                      {
                        "workOrderId": "WO-CLOSED-001",
                        "skuId": "SKU-FG-1000",
                        "skuCode": "SKU-FG-1000",
                        "uomCode": "pcs",
                        "quantity": 12,
                        "completedQuantity": 0,
                        "priority": 5,
                        "dueUtc": "2026-05-31T00:00:00Z",
                        "status": "closed",
                        "operationTasks": []
                      },
                      {
                        "workOrderId": "WO-LATE-001",
                        "skuId": "SKU-FG-1000",
                        "skuCode": "SKU-FG-1000",
                        "uomCode": "pcs",
                        "quantity": 5,
                        "completedQuantity": 0,
                        "priority": 5,
                        "dueUtc": "2026-07-01T00:00:00Z",
                        "status": "released",
                        "operationTasks": []
                      },
                      {
                        "workOrderId": "WO-OTHER-001",
                        "skuId": "SKU-OTHER",
                        "skuCode": "SKU-OTHER",
                        "uomCode": "pcs",
                        "quantity": 5,
                        "completedQuantity": 0,
                        "priority": 5,
                        "dueUtc": "2026-05-31T00:00:00Z",
                        "status": "released",
                        "operationTasks": []
                      }
                    ]
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://mes.test") };
        var client = new HttpPlanningMesScheduledReceiptSnapshotClient(httpClient);

        var snapshot = await client.GetScheduledReceiptsAsync(
            "token",
            new PlanningScheduledReceiptSnapshotRequest(
                "org-001",
                "env-dev",
                new DateOnly(2026, 5, 25),
                new DateOnly(2026, 6, 30),
                [new PlanningScheduledReceiptSnapshotItem("sku-fg-1000", "PCS", "SITE-01")]),
            CancellationToken.None);

        var receipt = Assert.Single(snapshot.ScheduledReceipts);
        Assert.Equal("mes-work-orders:1", snapshot.SnapshotSource);
        Assert.Equal("SKU-FG-1000", receipt.SkuCode);
        Assert.Equal("pcs", receipt.UomCode);
        Assert.Equal("SITE-01", receipt.SiteCode);
        Assert.Equal(6m, receipt.Quantity);
        Assert.Equal(new DateOnly(2026, 5, 31), receipt.ExpectedReceiptDate);
        Assert.Equal("mes", receipt.SourceSystem);
        Assert.Equal("work-order", receipt.SourceDocumentType);
        Assert.Equal("WO-OPEN-001", receipt.SourceDocumentId);
    }

    [Fact]
    public async Task Scheduled_receipt_source_registration_keeps_erp_and_mes_base_addresses_isolated()
    {
        var requests = new List<Uri>();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new CaptureHttpMessageHandlerBuilderFilter(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.AbsolutePath.Contains("/erp/", StringComparison.OrdinalIgnoreCase)
                ? JsonResponse("""{"success":true,"message":"ok","code":0,"data":{"total":0,"items":[]}}""")
                : JsonResponse("""{"success":true,"message":"ok","code":0,"data":{"total":0,"items":[]}}""");
        }));
        services.AddPlanningScheduledReceiptSourceClients(
            new Uri("http://erp.test"),
            new Uri("http://mes.test"));
        await using var provider = services.BuildServiceProvider();
        var sources = provider.GetServices<IPlanningScheduledReceiptSourceClient>().ToArray();

        foreach (var source in sources)
        {
            await source.GetScheduledReceiptsAsync(
                "token",
                new PlanningScheduledReceiptSnapshotRequest(
                    "org-001",
                    "env-dev",
                    new DateOnly(2026, 5, 25),
                    new DateOnly(2026, 6, 30),
                    [new PlanningScheduledReceiptSnapshotItem("SKU-FG-1000", "pcs", "SITE-01")]),
                CancellationToken.None);
        }

        Assert.Equal(2, sources.Length);
        Assert.Contains(requests, x => x.Host == "erp.test" && x.AbsolutePath == "/api/business/v1/erp/purchase-orders");
        Assert.Contains(requests, x => x.Host == "mes.test" && x.AbsolutePath == "/api/business/v1/mes/work-orders");
    }

    [Fact]
    public async Task Composite_scheduled_receipts_uses_explicit_source_name_when_source_degrades()
    {
        var composite = new CompositePlanningScheduledReceiptSnapshotClient(
            [new ThrowingNamedScheduledReceiptSourceClient("mes-work-orders")]);

        var snapshot = await composite.GetScheduledReceiptsAsync(
            "token",
            new PlanningScheduledReceiptSnapshotRequest(
                "org-001",
                "env-dev",
                new DateOnly(2026, 5, 25),
                new DateOnly(2026, 6, 30),
                [new PlanningScheduledReceiptSnapshotItem("SKU-FG-1000", "pcs", "SITE-01")]),
            CancellationToken.None);

        Assert.Equal("mes-work-orders:error", snapshot.SnapshotSource);
        Assert.Empty(snapshot.ScheduledReceipts);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_maps_sku_planning_attributes()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("token", request.Headers.Authorization?.Parameter);
            Assert.Contains("organizationId=org-001", request.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("environmentId=env-dev", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);

            if (request.RequestUri.AbsolutePath.EndsWith("/sku/SKU-FG-1000", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "resourceType": "sku",
                        "code": "SKU-FG-1000",
                        "displayName": "Finished good",
                        "active": true,
                        "snapshotVersion": "v1",
                        "organizationId": "org-001",
                        "environmentId": "env-dev",
                        "baseUomCode": "pcs",
                        "procurementType": "make",
                        "mrpType": "mrp",
                        "lotSizingPolicy": "fixed-lot",
                        "plannedDeliveryTimeDays": 4,
                        "inHouseProductionTimeDays": 5,
                        "goodsReceiptProcessingTimeDays": 1,
                        "safetyStockQuantity": 4,
                        "reorderPointQuantity": 6,
                        "minimumLotSize": 10,
                        "maximumLotSize": 50,
                        "lotSizeMultiple": 5
                      }
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/sku/SKU-BLOCKED", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "resourceType": "sku",
                        "code": "SKU-BLOCKED",
                        "displayName": "Blocked item",
                        "active": true,
                        "snapshotVersion": "v1",
                        "organizationId": "org-001",
                        "environmentId": "env-dev",
                        "baseUomCode": "pcs",
                        "lifecycleStatus": "blocked",
                        "plannedDeliveryTimeDays": 30,
                        "safetyStockQuantity": 99,
                        "lotSizeMultiple": 99
                      }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "SKU-RM-1000",
                    "displayName": "Raw material",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs",
                    "procurementType": "buy",
                    "mrpType": "mrp",
                    "lotSizingPolicy": "fixed-lot",
                    "plannedDeliveryTimeDays": 3,
                    "goodsReceiptProcessingTimeDays": 0,
                    "safetyStockQuantity": 2,
                    "reorderPointQuantity": 8,
                    "lotSizeMultiple": 10
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var snapshot = await client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [
                    new PlanningParameterSnapshotItem("sku-fg-1000", "pcs", "SITE-01"),
                    new PlanningParameterSnapshotItem("SKU-RM-1000", "pcs", "SITE-01"),
                    new PlanningParameterSnapshotItem("SKU-FG-1000", "pcs", "SITE-02"),
                    new PlanningParameterSnapshotItem("SKU-BLOCKED", "pcs", "SITE-01"),
                ]),
            CancellationToken.None);

        Assert.Equal("master-data-planning-parameters:3;master-data-uom-conversions:0", snapshot.SnapshotSource);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(snapshot.PlanningParameters, x => x.SkuCode == "sku-fg-1000" && x.SiteCode == "SITE-01" && x.LeadTimeDays == 6);
        Assert.Contains(snapshot.PlanningParameters, x => x.SkuCode == "SKU-FG-1000" && x.SiteCode == "SITE-02" && x.LotSizeMultiple == 5m);
        Assert.Contains(snapshot.PlanningParameters, x =>
            x.SkuCode == "SKU-RM-1000"
            && x.LeadTimeDays == 3
            && x.SafetyStockQuantity == 2m
            && x.ProcurementType == "buy"
            && x.MrpType == "mrp"
            && x.LotSizingPolicy == "fixed-lot"
            && x.ReorderPointQuantity == 8m
            && x.PlannedDeliveryTimeDays == 3
            && x.InHouseProductionTimeDays is null
            && x.GoodsReceiptProcessingTimeDays == 0);
        Assert.DoesNotContain(snapshot.PlanningParameters, x => x.SkuCode == "SKU-BLOCKED");
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_maps_required_uom_conversions_to_planning_uom()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/resources", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("resourceType=uom-conversion", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("all=True", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "total": 1,
                        "resources": [
                          {
                            "resourceType": "uom-conversion",
                            "code": "box->pcs",
                            "displayName": "box to pcs",
                            "active": true,
                            "snapshotVersion": "v1",
                            "fromUomCode": "box",
                            "toUomCode": "pcs",
                            "factor": 12,
                            "offset": 0,
                            "precision": 0,
                            "roundingMode": "half-up",
                            "effectiveFrom": "2026-01-01"
                          }
                        ]
                      }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "SKU-FG-1000",
                    "displayName": "Finished good",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs",
                    "plannedDeliveryTimeDays": 1
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var snapshot = await client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [new PlanningParameterSnapshotItem("SKU-FG-1000", "box", "SITE-01")]),
            CancellationToken.None);

        Assert.Equal("master-data-planning-parameters:1;master-data-uom-conversions:1", snapshot.SnapshotSource);
        var parameter = Assert.Single(snapshot.PlanningParameters);
        Assert.Equal("pcs", parameter.UomCode);
        var conversion = Assert.Single(snapshot.UomConversions);
        Assert.Equal("box", conversion.FromUomCode);
        Assert.Equal("pcs", conversion.ToUomCode);
        Assert.Equal(12m, conversion.Factor);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_selects_uom_conversion_by_as_of_date()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/resources", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "total": 4,
                        "resources": [
                          {
                            "resourceType": "uom-conversion",
                            "code": "box->pcs",
                            "displayName": "expired",
                            "active": true,
                            "snapshotVersion": "v1",
                            "fromUomCode": "box",
                            "toUomCode": "pcs",
                            "factor": 10,
                            "offset": 0,
                            "precision": 0,
                            "roundingMode": "half-up",
                            "effectiveFrom": "2026-01-01",
                            "effectiveTo": "2026-05-31"
                          },
                          {
                            "resourceType": "uom-conversion",
                            "code": "box->pcs",
                            "displayName": "current",
                            "active": true,
                            "snapshotVersion": "v2",
                            "fromUomCode": "box",
                            "toUomCode": "pcs",
                            "factor": 12,
                            "offset": 0,
                            "precision": 0,
                            "roundingMode": "half-up",
                            "effectiveFrom": "2026-06-01"
                          },
                          {
                            "resourceType": "uom-conversion",
                            "code": "box->pcs",
                            "displayName": "invalid latest",
                            "active": true,
                            "snapshotVersion": "v3",
                            "fromUomCode": "box",
                            "toUomCode": "pcs",
                            "factor": 0,
                            "offset": 0,
                            "precision": 0,
                            "roundingMode": "half-up",
                            "effectiveFrom": "2026-06-15"
                          },
                          {
                            "resourceType": "uom-conversion",
                            "code": "box->pcs",
                            "displayName": "future",
                            "active": true,
                            "snapshotVersion": "v4",
                            "fromUomCode": "box",
                            "toUomCode": "pcs",
                            "factor": 24,
                            "offset": 0,
                            "precision": 0,
                            "roundingMode": "half-up",
                            "effectiveFrom": "2026-07-01"
                          }
                        ]
                      }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "SKU-FG-1000",
                    "displayName": "Finished good",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs"
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var snapshot = await client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [new PlanningParameterSnapshotItem("SKU-FG-1000", "box", "SITE-01")],
                new DateOnly(2026, 6, 20)),
            CancellationToken.None);

        var conversion = Assert.Single(snapshot.UomConversions);
        Assert.Equal(12m, conversion.Factor);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_reports_truncated_uom_conversion_list()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/resources", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "message": "ok",
                      "code": 0,
                      "data": {
                        "total": 5001,
                        "truncated": true,
                        "limit": 5000,
                        "resources": []
                      }
                    }
                    """);
            }

            return JsonResponse("""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "SKU-FG-1000",
                    "displayName": "Finished good",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs"
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);

        var exception = await Assert.ThrowsAsync<RequiredPlanningSnapshotException>(() => client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest(
                "org-001",
                "env-dev",
                [new PlanningParameterSnapshotItem("SKU-FG-1000", "box", "SITE-01")],
                new DateOnly(2026, 6, 20)),
            CancellationToken.None));

        Assert.Contains("truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Master_data_planning_parameter_client_limits_sku_detail_concurrency()
    {
        var current = 0;
        var observedMax = 0;
        var handler = new StubHttpMessageHandler(async request =>
        {
            var running = Interlocked.Increment(ref current);
            int snapshot;
            while (running > (snapshot = Volatile.Read(ref observedMax)))
            {
                Interlocked.CompareExchange(ref observedMax, running, snapshot);
            }

            await Task.Delay(50);
            Interlocked.Decrement(ref current);
            var skuCode = request.RequestUri!.AbsolutePath.Split('/').Last();
            return JsonResponse($$"""
                {
                  "success": true,
                  "message": "ok",
                  "code": 0,
                  "data": {
                    "resourceType": "sku",
                    "code": "{{skuCode}}",
                    "displayName": "{{skuCode}}",
                    "active": true,
                    "snapshotVersion": "v1",
                    "organizationId": "org-001",
                    "environmentId": "env-dev",
                    "baseUomCode": "pcs",
                    "plannedDeliveryTimeDays": 1
                  }
                }
                """);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://master-data.test") };
        var client = new HttpPlanningMasterDataPlanningParameterSnapshotClient(httpClient);
        var items = Enumerable.Range(1, 20)
            .Select(x => new PlanningParameterSnapshotItem($"SKU-{x:000}", "pcs", "SITE-01"))
            .ToArray();

        var snapshot = await client.GetPlanningParametersAsync(
            "token",
            new PlanningParameterSnapshotRequest("org-001", "env-dev", items),
            CancellationToken.None);

        Assert.Equal(20, snapshot.PlanningParameters.Count);
        Assert.True(observedMax <= 8, $"Expected at most 8 concurrent MasterData SKU requests, observed {observedMax}.");
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"demand-planning-adapter-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    private static async Task AddForecastAsync(
        ApplicationDbContext dbContext,
        string forecastReference,
        DateOnly periodStartDate,
        DateOnly periodEndDate,
        decimal quantity,
        int backwardConsumptionDays = 0,
        int forwardConsumptionDays = 0)
    {
        await new CreateOrUpdateForecastInputCommandHandler(dbContext).Handle(
            new CreateOrUpdateForecastInputCommand(
                "org-001",
                "env-dev",
                forecastReference,
                "SKU-FG-1000",
                "pcs",
                "SITE-01",
                periodStartDate,
                periodEndDate,
                quantity,
                backwardConsumptionDays,
                forwardConsumptionDays),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static Task<PlanningInputSnapshotResult> ReadSnapshotAsync(
        ApplicationDbContext dbContext,
        DateOnly horizonStart,
        DateOnly horizonEnd)
    {
        return new DemandPlanningUpstreamInputSnapshotProvider(
            dbContext,
            new FakePlanningProductEngineeringClient(),
            new FakePlanningInventoryClient()).GetSnapshotAsync(
                "org-001",
                "env-dev",
                horizonStart,
                horizonEnd,
                CancellationToken.None);
    }

    private static void AssertForecastFacts(
        PlanningInputSnapshotResult snapshot,
        string forecastReference,
        DateOnly horizonStart,
        DateOnly horizonEnd,
        IReadOnlyCollection<(DateOnly DueDate, decimal Quantity)> expected)
    {
        var facts = snapshot.Demands
            .Where(x => x.SourceType == "forecast")
            .ToArray();
        Assert.Equal(expected, facts.Select(x => (x.DueDate, x.Quantity)).ToArray());
        Assert.All(facts, x => Assert.Equal(forecastReference, x.DemandSourceReference));
        AssertForecastFactInvariants(facts, horizonStart, horizonEnd);
    }

    private static void AssertForecastFactInvariants(
        IReadOnlyCollection<DemandSnapshot> facts,
        DateOnly horizonStart,
        DateOnly horizonEnd)
    {
        Assert.All(facts, fact =>
        {
            Assert.True(fact.Quantity > 0m);
            Assert.InRange(fact.DueDate, horizonStart, horizonEnd);
        });
        Assert.Equal(
            facts.Count,
            facts.Select(x => (x.DemandSourceReference, x.DueDate)).Distinct().Count());
    }

    private static CreateOrUpdateForecastInputCommand NewForecastCommand()
    {
        return new CreateOrUpdateForecastInputCommand(
            "org-001",
            "env-dev",
            "FC-2026-06-SKU-FG-1000",
            "SKU-FG-1000",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30),
            10m,
            7,
            3);
    }

    private sealed class FakePlanningProductEngineeringClient : IPlanningProductEngineeringSnapshotClient
    {
        private readonly List<string> requestedParentSkuCodes = [];

        public IReadOnlyCollection<string> RequestedParentSkuCodes => requestedParentSkuCodes;

        public Task<PlanningProductEngineeringSnapshot> GetSnapshotAsync(
            string internalBearerToken,
            PlanningProductEngineeringSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            requestedParentSkuCodes.AddRange(request.ParentSkuCodes);
            return Task.FromResult(new PlanningProductEngineeringSnapshot(
                "product-engineering-http:2",
                request.ParentSkuCodes.Contains("SKU-FG-1000", StringComparer.OrdinalIgnoreCase)
                    ? [new ProductionVersionSnapshot("SKU-FG-1000", "PV-REAL-001", "MBOM-REAL-001", "ROUTING-REAL-001", 10m, 50m, null)]
                    : [],
                request.ParentSkuCodes.Contains("SKU-FG-1000", StringComparer.OrdinalIgnoreCase)
                    ? [new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "pcs", 3m)]
                    : []));
        }
    }

    private sealed class FakePlanningInventoryClient : IPlanningInventorySnapshotClient
    {
        public IReadOnlyCollection<string> RequestedSkuCodes { get; private set; } = [];

        public Task<PlanningInventorySnapshot> GetAvailabilitySnapshotAsync(
            string internalBearerToken,
            PlanningInventorySnapshotRequest request,
            CancellationToken cancellationToken)
        {
            RequestedSkuCodes = request.Items.Select(x => x.SkuCode).ToArray();
            return Task.FromResult(new PlanningInventorySnapshot(
                "inventory-http:2",
                [
                    new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 2m),
                    new InventoryAvailabilitySnapshot("SKU-RM-1000", "pcs", "SITE-01", 5m),
                ]));
        }
    }

    private sealed class FakePlanningErpScheduledReceiptClient : IPlanningScheduledReceiptSnapshotClient
    {
        public IReadOnlyCollection<string> RequestedSkuCodes { get; private set; } = [];

        public Task<PlanningScheduledReceiptSnapshot> GetScheduledReceiptsAsync(
            string internalBearerToken,
            PlanningScheduledReceiptSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            RequestedSkuCodes = request.Items.Select(x => x.SkuCode).ToArray();
            return Task.FromResult(new PlanningScheduledReceiptSnapshot(
                "erp-purchase-orders:1",
                [
                    new ScheduledReceiptSnapshot("SKU-RM-1000", "pcs", "SITE-01", 7m, new DateOnly(2026, 5, 30), "erp", "purchase-order", "PO-1000:10"),
                ]));
        }
    }

    private sealed class FakePlanningParameterClient : IPlanningParameterSnapshotClient
    {
        public IReadOnlyCollection<string> RequestedSkuCodes { get; private set; } = [];

        public Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
            string internalBearerToken,
            PlanningParameterSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            RequestedSkuCodes = request.Items.Select(x => x.SkuCode).ToArray();
            return Task.FromResult(new PlanningParameterSnapshotResult(
                "master-data-planning-parameters:2;master-data-uom-conversions:0",
                [
                    new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 6, 4m, 10m, 50m, 5m),
                    new PlanningParameterSnapshot("SKU-RM-1000", "pcs", "SITE-01", 3, 2m, null, null, 10m),
                ],
                []));
        }
    }

    private sealed class BoxPlanningParameterClient : IPlanningParameterSnapshotClient
    {
        public Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
            string internalBearerToken,
            PlanningParameterSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlanningParameterSnapshotResult(
                "master-data-planning-parameters:1;master-data-uom-conversions:1",
                [
                    new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 0m, null, null, null),
                ],
                [
                    new UomConversionSnapshot("box", "pcs", 10m, 0m, 0, "half-up"),
                ]));
        }
    }

    private sealed class RequestAwareBoxPlanningParameterClient : IPlanningParameterSnapshotClient
    {
        private readonly List<IReadOnlyCollection<string>> requestedUomCodes = [];

        public IReadOnlyCollection<IReadOnlyCollection<string>> RequestedUomCodes => requestedUomCodes;

        public Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
            string internalBearerToken,
            PlanningParameterSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            var uomCodes = request.Items
                .Select(x => x.UomCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            requestedUomCodes.Add(uomCodes);
            var conversions = uomCodes.Contains("box", StringComparer.OrdinalIgnoreCase)
                ? new[] { new UomConversionSnapshot("box", "pcs", 2m, 0m, 0, "half-up") }
                : [];
            return Task.FromResult(new PlanningParameterSnapshotResult(
                $"master-data-planning-parameters:1;master-data-uom-conversions:{conversions.Length}",
                [
                    new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 0m, null, null, null),
                ],
                conversions));
        }
    }

    private sealed class ThrowingPlanningErpScheduledReceiptClient : IPlanningScheduledReceiptSnapshotClient
    {
        public Task<PlanningScheduledReceiptSnapshot> GetScheduledReceiptsAsync(
            string internalBearerToken,
            PlanningScheduledReceiptSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("ERP unavailable");
        }
    }

    private sealed class ThrowingPlanningParameterClient : IPlanningParameterSnapshotClient
    {
        public Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
            string internalBearerToken,
            PlanningParameterSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new OptionalPlanningSnapshotException("MasterData unavailable");
        }
    }

    private sealed class TruncatedUomConversionPlanningParameterClient : IPlanningParameterSnapshotClient
    {
        public Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
            string internalBearerToken,
            PlanningParameterSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new RequiredPlanningSnapshotException("MasterData UOM conversion list was truncated at 5000 of 5001.");
        }
    }

    private sealed class BuggyPlanningErpScheduledReceiptClient : IPlanningScheduledReceiptSnapshotClient
    {
        public Task<PlanningScheduledReceiptSnapshot> GetScheduledReceiptsAsync(
            string internalBearerToken,
            PlanningScheduledReceiptSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("ERP optional source bug");
        }
    }

    private sealed class BuggyPlanningParameterClient : IPlanningParameterSnapshotClient
    {
        public Task<PlanningParameterSnapshotResult> GetPlanningParametersAsync(
            string internalBearerToken,
            PlanningParameterSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("MasterData optional source bug");
        }
    }

    private sealed class ThrowingNamedScheduledReceiptSourceClient(string sourceName) : IPlanningScheduledReceiptSourceClient
    {
        public string SourceName { get; } = sourceName;

        public Task<PlanningScheduledReceiptSnapshot> GetScheduledReceiptsAsync(
            string internalBearerToken,
            PlanningScheduledReceiptSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            throw new OptionalPlanningSnapshotException($"{SourceName} unavailable");
        }
    }

    private sealed class CaptureHttpMessageHandlerBuilderFilter(Func<HttpRequestMessage, HttpResponseMessage> send)
        : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        {
            return builder =>
            {
                next(builder);
                builder.AdditionalHandlers.Add(new CaptureHttpMessageHandler(send));
            };
        }
    }

    private sealed class CaptureHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
            : this((request, _) => Task.FromResult(send(request)))
        {
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
            : this((request, _) => send(request))
        {
        }

        private StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            this.send = send;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return send(request, cancellationToken);
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
