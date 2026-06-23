using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class MrpCalculatorTests
{
    [Fact]
    public void Deterministic_fixture_creates_work_order_8_and_purchase_19()
    {
        var input = new MrpCalculationInput(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            [
                new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            ],
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 2m),
                new InventoryAvailabilitySnapshot("SKU-RM-1000", "pcs", "SITE-01", 5m),
            ],
            [
                new ProductionVersionSnapshot("SKU-FG-1000", "PV-001", "MBOM-001", "ROUTING-001"),
            ],
            [
                new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "pcs", 3m),
            ],
            [],
            [],
            []);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        var purchase = Assert.Single(suggestions, x => x.SuggestionType == "planned-purchase");
        Assert.Equal("SKU-FG-1000", workOrder.SkuCode);
        Assert.Equal(8m, workOrder.Quantity);
        Assert.Equal("SKU-RM-1000", purchase.SkuCode);
        Assert.Equal(19m, purchase.Quantity);
        Assert.All(suggestions, x => Assert.Contains(x.PeggingLinks, p => p.DemandSourceReference == "DEMAND-001"));
    }

    [Fact]
    public void Scheduled_receipts_reduce_net_requirement_before_new_suggestions()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            ],
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 2m),
            ],
            scheduledReceipts:
            [
                new ScheduledReceiptSnapshot("SKU-FG-1000", "pcs", "SITE-01", 5m, new DateOnly(2026, 5, 31), "erp", "purchase-order", "PO-001"),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        Assert.Equal(3m, workOrder.Quantity);
        Assert.Contains(workOrder.PeggingLinks, x => x.PeggingType == "scheduled-receipt" && x.DemandSourceReference == "erp:purchase-order:PO-001");
    }

    [Fact]
    public void Multi_uom_inputs_are_normalized_to_planning_uom_before_netting_and_pegging()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-BOX", "SKU-FG-1000", "box", "SITE-01", 2m, new DateOnly(2026, 6, 1)),
            ],
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 6m),
                new InventoryAvailabilitySnapshot("SKU-RM-1000", "g", "SITE-01", 500m),
            ],
            productionVersions:
            [
                new ProductionVersionSnapshot("SKU-FG-1000", "PV-001", "MBOM-001", "ROUTING-001"),
            ],
            bomComponents:
            [
                new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "kg", 1.5m),
            ],
            scheduledReceipts:
            [
                new ScheduledReceiptSnapshot("SKU-FG-1000", "box", "SITE-01", 1m, new DateOnly(2026, 5, 31), "mes", "work-order", "WO-001"),
                new ScheduledReceiptSnapshot("SKU-RM-1000", "kg", "SITE-01", 1m, new DateOnly(2026, 5, 30), "erp", "purchase-order", "PO-001"),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 0m, null, null, null),
                new PlanningParameterSnapshot("SKU-RM-1000", "g", "SITE-01", 0, 0m, null, null, null),
            ],
            uomConversions:
            [
                new UomConversionSnapshot("box", "pcs", 12m, 0m, 0, "half-up"),
                new UomConversionSnapshot("kg", "g", 1000m, 0m, 0, "half-up"),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        Assert.Equal("pcs", workOrder.UomCode);
        Assert.Equal(6m, workOrder.Quantity);
        Assert.Contains(workOrder.PeggingLinks, x =>
            x.PeggingType == "demand"
            && x.DemandSourceReference == "DEMAND-BOX"
            && x.Quantity == 24m);
        Assert.Contains(workOrder.PeggingLinks, x =>
            x.PeggingType == "scheduled-receipt"
            && x.DemandSourceReference == "mes:work-order:WO-001"
            && x.Quantity == 12m);

        var purchase = Assert.Single(suggestions, x => x.SuggestionType == "planned-purchase");
        Assert.Equal("SKU-RM-1000", purchase.SkuCode);
        Assert.Equal("g", purchase.UomCode);
        Assert.Equal(7500m, purchase.Quantity);
        Assert.Contains(purchase.PeggingLinks, x =>
            x.PeggingType == "scheduled-receipt"
            && x.DemandSourceReference == "erp:purchase-order:PO-001"
            && x.Quantity == 1000m);
    }

    [Fact]
    public void Bom_component_conversion_rounds_after_total_component_requirement()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 3m, new DateOnly(2026, 6, 1)),
            ],
            availability: [],
            productionVersions:
            [
                new ProductionVersionSnapshot("SKU-FG-1000", "PV-001", "MBOM-001", "ROUTING-001"),
            ],
            bomComponents:
            [
                new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "lb", 1m),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 0m, null, null, null),
                new PlanningParameterSnapshot("SKU-RM-1000", "kg", "SITE-01", 0, 0m, null, null, null),
            ],
            uomConversions:
            [
                new UomConversionSnapshot("lb", "kg", 0.45359237m, 0m, 2, "half-up"),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var purchase = Assert.Single(suggestions, x => x.SuggestionType == "planned-purchase");
        Assert.Equal("kg", purchase.UomCode);
        Assert.Equal(1.36m, purchase.Quantity);
    }

    [Fact]
    public void Missing_required_uom_conversion_fails_instead_of_silently_mismatching_units()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-BOX", "SKU-FG-1000", "box", "SITE-01", 1m, new DateOnly(2026, 6, 1)),
            ],
            availability: [],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 0m, null, null, null),
            ],
            uomConversions: []);

        var exception = Assert.Throws<InvalidOperationException>(() => MrpCalculator.Calculate(input));

        Assert.Contains("Missing global UOM conversion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SKU-FG-1000", exception.Message, StringComparison.Ordinal);
        Assert.Contains("box", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pcs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invalid_uom_conversion_factor_fails_instead_of_zeroing_requirement()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-BOX", "SKU-FG-1000", "box", "SITE-01", 1m, new DateOnly(2026, 6, 1)),
            ],
            availability: [],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 0m, null, null, null),
            ],
            uomConversions:
            [
                new UomConversionSnapshot("box", "pcs", 0m, 0m, 0, "half-up"),
            ]);

        var exception = Assert.Throws<InvalidOperationException>(() => MrpCalculator.Calculate(input));

        Assert.Contains("Invalid global UOM conversion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SKU-FG-1000", exception.Message, StringComparison.Ordinal);
        Assert.Contains("box", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pcs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Multi_level_bom_creates_make_suggestion_for_subassembly_then_purchase_for_raw_material()
    {
        var input = NewInput(
            availability: [],
            productionVersions:
            [
                new ProductionVersionSnapshot("SKU-FG-1000", "PV-FG", "MBOM-FG", "ROUTING-FG"),
                new ProductionVersionSnapshot("SKU-SA-1000", "PV-SA", "MBOM-SA", "ROUTING-SA"),
            ],
            bomComponents:
            [
                new BomComponentSnapshot("SKU-FG-1000", "SKU-SA-1000", "pcs", 2m),
                new BomComponentSnapshot("SKU-SA-1000", "SKU-RM-1000", "pcs", 3m),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        Assert.Contains(suggestions, x => x.SuggestionType == "planned-work-order" && x.SkuCode == "SKU-FG-1000" && x.Quantity == 10m);
        Assert.Contains(suggestions, x => x.SuggestionType == "planned-work-order" && x.SkuCode == "SKU-SA-1000" && x.Quantity == 20m);
        Assert.Contains(suggestions, x => x.SuggestionType == "planned-purchase" && x.SkuCode == "SKU-RM-1000" && x.Quantity == 60m);
    }

    [Fact]
    public void Component_pegging_quantities_are_apportioned_by_source_demand_share()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 4m, new DateOnly(2026, 6, 1)),
                new DemandSnapshot("DEMAND-002", "SKU-FG-1000", "pcs", "SITE-01", 6m, new DateOnly(2026, 6, 1)),
            ],
            availability: [],
            productionVersions:
            [
                new ProductionVersionSnapshot("SKU-FG-1000", "PV-FG", "MBOM-FG", "ROUTING-FG"),
            ],
            bomComponents:
            [
                new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "pcs", 3m),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var purchase = Assert.Single(suggestions, x => x.SuggestionType == "planned-purchase" && x.SkuCode == "SKU-RM-1000");
        Assert.Equal(30m, purchase.Quantity);
        Assert.Contains(purchase.PeggingLinks, x => x.DemandSourceReference == "DEMAND-001" && x.Quantity == 12m);
        Assert.Contains(purchase.PeggingLinks, x => x.DemandSourceReference == "DEMAND-002" && x.Quantity == 18m);
        Assert.Equal(30m, purchase.PeggingLinks.Where(x => x.PeggingType == "demand").Sum(x => x.Quantity));
    }

    [Fact]
    public void Lead_time_offsets_release_date_without_changing_required_date()
    {
        var input = NewInput(
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 5, 0m, null, null, null),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        Assert.Equal(new DateOnly(2026, 6, 1), workOrder.RequiredDate);
        Assert.Equal(new DateOnly(2026, 5, 27), workOrder.ReleaseDate);
    }

    [Fact]
    public void Daily_bucket_aggregation_applies_lot_size_min_and_max()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 4m, new DateOnly(2026, 6, 1)),
                new DemandSnapshot("DEMAND-002", "SKU-FG-1000", "pcs", "SITE-01", 5m, new DateOnly(2026, 6, 1)),
            ],
            availability: [],
            productionVersions:
            [
                new ProductionVersionSnapshot("SKU-FG-1000", "PV-001", "MBOM-001", "ROUTING-001", 10m, 12m, null),
            ],
            bomComponents: []);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        Assert.Equal(10m, workOrder.Quantity);
        Assert.Equal(2, workOrder.PeggingLinks.Count(x => x.PeggingType == "demand"));
    }

    [Fact]
    public void Lot_size_max_splits_suggestions_without_underplanning()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 30m, new DateOnly(2026, 6, 1)),
            ],
            availability: [],
            productionVersions:
            [
                new ProductionVersionSnapshot("SKU-FG-1000", "PV-001", "MBOM-001", "ROUTING-001", null, 12m, null),
            ],
            bomComponents: []);

        var suggestions = MrpCalculator.Calculate(input)
            .Where(x => x.SuggestionType == "planned-work-order")
            .ToArray();

        Assert.Equal([12m, 12m, 6m], suggestions.Select(x => x.Quantity).ToArray());
        Assert.Equal(30m, suggestions.Sum(x => x.Quantity));
    }

    [Fact]
    public void Safety_stock_is_protected_in_net_requirement()
    {
        var input = NewInput(
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 10m),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 3m, null, null, null),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        Assert.Equal(3m, workOrder.Quantity);
    }

    [Fact]
    public void Safety_stock_floor_is_not_repeated_as_gross_requirement_across_date_buckets()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 4m, new DateOnly(2026, 6, 1)),
                new DemandSnapshot("DEMAND-002", "SKU-FG-1000", "pcs", "SITE-01", 4m, new DateOnly(2026, 6, 2)),
            ],
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 10m),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 3m, null, null, null),
            ],
            bomComponents: []);

        var suggestions = MrpCalculator.Calculate(input)
            .Where(x => x.SuggestionType == "planned-work-order")
            .ToArray();

        var workOrder = Assert.Single(suggestions);
        Assert.Equal(new DateOnly(2026, 6, 2), workOrder.RequiredDate);
        Assert.Equal(1m, workOrder.Quantity);
    }

    [Fact]
    public void Demand_outside_horizon_does_not_create_suggestions()
    {
        var input = NewInput(demands:
        [
            new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 7, 1)),
        ]);

        var suggestions = MrpCalculator.Calculate(input);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Finished_good_availability_can_cover_all_demand()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            ],
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 10m),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Multiple_demands_consume_shared_availability_by_due_date_then_reference()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-B", "SKU-FG-1000", "pcs", "SITE-01", 6m, new DateOnly(2026, 6, 2)),
                new DemandSnapshot("DEMAND-A", "SKU-FG-1000", "pcs", "SITE-01", 6m, new DateOnly(2026, 6, 1)),
            ],
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 8m),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        Assert.Equal("DEMAND-B", workOrder.PeggingLinks.Single().DemandSourceReference);
        Assert.Equal(4m, workOrder.Quantity);
    }

    private static MrpCalculationInput NewInput(
        IReadOnlyCollection<DemandSnapshot>? demands = null,
        IReadOnlyCollection<InventoryAvailabilitySnapshot>? availability = null,
        IReadOnlyCollection<ProductionVersionSnapshot>? productionVersions = null,
        IReadOnlyCollection<BomComponentSnapshot>? bomComponents = null,
        IReadOnlyCollection<ScheduledReceiptSnapshot>? scheduledReceipts = null,
        IReadOnlyCollection<PlanningParameterSnapshot>? planningParameters = null,
        IReadOnlyCollection<UomConversionSnapshot>? uomConversions = null)
    {
        return new MrpCalculationInput(
            "org-001",
            "env-dev",
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 6, 30),
            demands ??
            [
                new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1)),
            ],
            availability ??
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 2m),
                new InventoryAvailabilitySnapshot("SKU-RM-1000", "pcs", "SITE-01", 5m),
            ],
            productionVersions ??
            [
                new ProductionVersionSnapshot("SKU-FG-1000", "PV-001", "MBOM-001", "ROUTING-001"),
            ],
            bomComponents ??
            [
                new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "pcs", 3m),
            ],
            scheduledReceipts ?? [],
            planningParameters ?? [],
            uomConversions ?? []);
    }
}
