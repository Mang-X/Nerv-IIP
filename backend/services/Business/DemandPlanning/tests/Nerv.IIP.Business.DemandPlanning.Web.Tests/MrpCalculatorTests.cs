using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using System.Text.Json;

namespace Nerv.IIP.Business.DemandPlanning.Web.Tests;

public sealed class MrpCalculatorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Sales_order_demand_keeps_so_demo_reference_on_production_suggestion_pegging()
    {
        var suggestions = MrpCalculator.Calculate(NewInput(demands:
        [
            new DemandSnapshot("SO-DEMO-001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1), "sales-order"),
        ]));

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        Assert.Contains(workOrder.PeggingLinks, link =>
            link.PeggingType == "demand" &&
            link.DemandSourceReference == "SO-DEMO-001");
    }

    [Fact]
    public void Suggestions_expose_net_requirement_formula_from_real_mrp_inputs()
    {
        var input = NewInput(
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 8m),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 2m, null, null, null),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(workOrder, JsonOptions));
        var explanation = document.RootElement.GetProperty("netRequirementExplanation");
        Assert.Equal(10m, explanation.GetProperty("grossDemandQuantity").GetDecimal());
        Assert.Equal(8m, explanation.GetProperty("onHandQuantity").GetDecimal());
        Assert.Equal(0m, explanation.GetProperty("reservedQuantity").GetDecimal());
        Assert.Equal(6m, explanation.GetProperty("availableToNetQuantity").GetDecimal());
        Assert.Equal(0m, explanation.GetProperty("scheduledReceiptQuantity").GetDecimal());
        Assert.Equal(2m, explanation.GetProperty("safetyStockQuantity").GetDecimal());
        Assert.Equal(4m, explanation.GetProperty("netRequirementQuantity").GetDecimal());
        Assert.Equal(4m, explanation.GetProperty("plannedQuantity").GetDecimal());
        Assert.Contains("10 - 6 - 0 = 4", explanation.GetProperty("formula").GetString(), StringComparison.Ordinal);
        Assert.Empty(explanation.GetProperty("degradationSources").EnumerateArray());
    }

    [Fact]
    public void Safety_stock_deficit_is_replenished_in_first_requirement_bucket()
    {
        var input = NewInput(
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 8m),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 12m, null, null, null),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        Assert.Equal(14m, workOrder.Quantity);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(workOrder, JsonOptions));
        var explanation = document.RootElement.GetProperty("netRequirementExplanation");
        Assert.Equal(10m, explanation.GetProperty("grossDemandQuantity").GetDecimal());
        Assert.Equal(8m, explanation.GetProperty("onHandQuantity").GetDecimal());
        Assert.Equal(0m, explanation.GetProperty("availableToNetQuantity").GetDecimal());
        Assert.Equal(12m, explanation.GetProperty("safetyStockQuantity").GetDecimal());
        Assert.Equal(14m, explanation.GetProperty("netRequirementQuantity").GetDecimal());
        Assert.Equal(14m, explanation.GetProperty("plannedQuantity").GetDecimal());
        Assert.Contains("10 - 0 - 0 + 4 safety-stock = 14", explanation.GetProperty("formula").GetString(), StringComparison.Ordinal);
        Assert.Contains(workOrder.PeggingLinks, x =>
            x.SourceType == "safety-stock"
            && x.Quantity == 4m
            && x.GrossDemandQuantity == 0m);
    }

    [Fact]
    public void Safety_stock_deficit_without_demand_uses_the_normal_buy_planning_path()
    {
        var input = NewInput(
            demands: [],
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-RM-1000", "pcs", "SITE-01", 2m),
            ],
            productionVersions: [],
            bomComponents: [],
            planningParameters:
            [
                new PlanningParameterSnapshot(
                    "SKU-RM-1000",
                    "pcs",
                    "SITE-01",
                    0,
                    5m,
                    null,
                    null,
                    null,
                    ProcurementType: "buy"),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var purchase = Assert.Single(suggestions);
        Assert.Equal("planned-purchase", purchase.SuggestionType);
        Assert.Equal("SKU-RM-1000", purchase.SkuCode);
        Assert.Equal(3m, purchase.Quantity);
        Assert.Equal(3m, purchase.NetRequirementExplanation.NetRequirementQuantity);
        Assert.Equal(3m, purchase.NetRequirementExplanation.PlannedQuantity);
        Assert.Equal(0m, purchase.NetRequirementExplanation.GrossDemandQuantity);
        Assert.Equal("safety-stock", purchase.NetRequirementExplanation.PrimarySourceType);
        Assert.Contains("0 - 0 - 0 + 3 safety-stock = 3", purchase.NetRequirementExplanation.Formula, StringComparison.Ordinal);
        var safetyPegging = Assert.Single(purchase.PeggingLinks);
        Assert.Equal("safety-stock", safetyPegging.SourceType);
        Assert.Equal(3m, safetyPegging.Quantity);
        Assert.Equal(0m, safetyPegging.GrossDemandQuantity);
    }

    [Fact]
    public void Partial_scheduled_receipt_is_consumed_once_across_demand_and_safety_deficit()
    {
        var input = NewInput(
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 8m),
            ],
            scheduledReceipts:
            [
                new ScheduledReceiptSnapshot(
                    "SKU-FG-1000",
                    "pcs",
                    "SITE-01",
                    3m,
                    new DateOnly(2026, 6, 1),
                    "erp",
                    "purchase-order",
                    "PO-SAFETY-PARTIAL"),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 12m, null, null, null),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        Assert.Equal(11m, workOrder.Quantity);
        Assert.Equal(11m, workOrder.NetRequirementExplanation.NetRequirementQuantity);
        Assert.Equal(3m, workOrder.NetRequirementExplanation.ScheduledReceiptQuantity);
        Assert.Contains("10 - 0 - 3 + 4 safety-stock = 11", workOrder.NetRequirementExplanation.Formula, StringComparison.Ordinal);
        var receiptPegging = Assert.Single(workOrder.PeggingLinks, x => x.PeggingType == "scheduled-receipt");
        Assert.Equal(3m, receiptPegging.Quantity);
        Assert.DoesNotContain(suggestions, x => x.SuggestionType == "cancel");
    }

    [Fact]
    public void Full_scheduled_receipt_covers_demand_and_safety_deficit_without_new_or_cancel_supply()
    {
        var input = NewInput(
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 8m),
            ],
            scheduledReceipts:
            [
                new ScheduledReceiptSnapshot(
                    "SKU-FG-1000",
                    "pcs",
                    "SITE-01",
                    14m,
                    new DateOnly(2026, 6, 1),
                    "erp",
                    "purchase-order",
                    "PO-SAFETY-FULL"),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 12m, null, null, null),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Safety_stock_floor_is_replenished_once_then_later_bucket_plans_only_demand()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-001", "SKU-FG-1000", "pcs", "SITE-01", 3m, new DateOnly(2026, 6, 1)),
                new DemandSnapshot("DEMAND-002", "SKU-FG-1000", "pcs", "SITE-01", 4m, new DateOnly(2026, 6, 2)),
            ],
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "pcs", "SITE-01", 0m),
            ],
            bomComponents: [],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 2m, null, null, null),
            ]);

        var workOrders = MrpCalculator.Calculate(input)
            .Where(x => x.SuggestionType == "planned-work-order")
            .OrderBy(x => x.RequiredDate)
            .ToArray();

        Assert.Equal(2, workOrders.Length);
        Assert.Equal(new DateOnly(2026, 6, 1), workOrders[0].RequiredDate);
        Assert.Equal(5m, workOrders[0].Quantity);
        Assert.Contains("3 - 0 - 0 + 2 safety-stock = 5", workOrders[0].NetRequirementExplanation.Formula, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 6, 2), workOrders[1].RequiredDate);
        Assert.Equal(4m, workOrders[1].Quantity);
        Assert.Equal("4 - 0 - 0 = 4", workOrders[1].NetRequirementExplanation.Formula);
    }

    [Fact]
    public void Safety_stock_replenishment_preserves_decimal_uom_precision_and_non_negative_boundaries()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-BOX", "SKU-FG-1000", "box", "SITE-01", 0.333m, new DateOnly(2026, 6, 1)),
            ],
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-FG-1000", "box", "SITE-01", 0.111m),
            ],
            bomComponents: [],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-FG-1000", "pcs", "SITE-01", 0, 0.50m, null, null, null),
            ],
            uomConversions:
            [
                new UomConversionSnapshot("box", "pcs", 3m, 0m, 2, "half-up"),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions);
        Assert.Equal("pcs", workOrder.UomCode);
        Assert.Equal(1.17m, workOrder.Quantity);
        Assert.Equal(1.00m, workOrder.NetRequirementExplanation.GrossDemandQuantity);
        Assert.Equal(0.33m, workOrder.NetRequirementExplanation.OnHandQuantity);
        Assert.Equal(0.50m, workOrder.NetRequirementExplanation.SafetyStockQuantity);
        Assert.Equal(1.17m, workOrder.NetRequirementExplanation.NetRequirementQuantity);
        Assert.Equal(1.17m, workOrder.NetRequirementExplanation.PlannedQuantity);
        Assert.Contains("1 - 0 - 0 + 0.17 safety-stock = 1.17", workOrder.NetRequirementExplanation.Formula, StringComparison.Ordinal);
        Assert.Contains(workOrder.NetRequirementExplanation.UomConversions, x => x.Contains("0.333 box -> 1.00 pcs", StringComparison.Ordinal));
        Assert.All(suggestions, suggestion =>
        {
            Assert.True(suggestion.Quantity >= 0m);
            Assert.True(suggestion.NetRequirementExplanation.GrossDemandQuantity >= 0m);
            Assert.True(suggestion.NetRequirementExplanation.OnHandQuantity >= 0m);
            Assert.True(suggestion.NetRequirementExplanation.ReservedQuantity >= 0m);
            Assert.True(suggestion.NetRequirementExplanation.AvailableToNetQuantity >= 0m);
            Assert.True(suggestion.NetRequirementExplanation.ScheduledReceiptQuantity >= 0m);
            Assert.True(suggestion.NetRequirementExplanation.SafetyStockQuantity >= 0m);
            Assert.True(suggestion.NetRequirementExplanation.NetRequirementQuantity >= 0m);
            Assert.True(suggestion.NetRequirementExplanation.PlannedQuantity >= 0m);
            Assert.All(suggestion.PeggingLinks, link =>
            {
                Assert.True(link.Quantity >= 0m);
                Assert.True(link.GrossDemandQuantity >= 0m);
            });
        });
    }

    [Fact]
    public void Component_suggestions_explain_scrap_yield_and_component_source()
    {
        var input = NewInput(
            availability: [],
            bomComponents:
            [
                new BomComponentSnapshot("SKU-FG-1000", "SKU-RM-1000", "pcs", 2m, ScrapRate: 0.1m, YieldRate: 0.8m),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var purchase = Assert.Single(suggestions, x => x.SuggestionType == "planned-purchase");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(purchase, JsonOptions));
        var explanation = document.RootElement.GetProperty("netRequirementExplanation");
        Assert.Equal(27.5m, explanation.GetProperty("grossDemandQuantity").GetDecimal());
        Assert.Equal(0.1m, explanation.GetProperty("scrapRate").GetDecimal());
        Assert.Equal(0.8m, explanation.GetProperty("yieldRate").GetDecimal());
        Assert.Equal("component", explanation.GetProperty("primarySourceType").GetString());
        Assert.Contains("scrap/yield", explanation.GetProperty("formula").GetString(), StringComparison.OrdinalIgnoreCase);
    }

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
    public void Late_scheduled_receipt_creates_reschedule_in_exception_instead_of_duplicate_new_supply()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("SO-1001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1), "sales-order"),
            ],
            availability: [],
            bomComponents: [],
            scheduledReceipts:
            [
                new ScheduledReceiptSnapshot("SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 5), "erp", "purchase-order", "PO-1001"),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var exception = Assert.Single(suggestions);
        Assert.Equal("reschedule-in", exception.SuggestionType);
        Assert.Equal("SKU-FG-1000", exception.SkuCode);
        Assert.Equal(10m, exception.Quantity);
        Assert.Equal(new DateOnly(2026, 6, 1), exception.RequiredDate);
        Assert.Equal(new DateOnly(2026, 6, 5), exception.ReleaseDate);
        Assert.Equal("scheduled-receipt-late", exception.ReasonCode);
        Assert.Contains(exception.PeggingLinks, x => x.PeggingType == "demand" && x.DemandSourceReference == "SO-1001");
        Assert.Contains(exception.PeggingLinks, x => x.PeggingType == "scheduled-receipt" && x.DemandSourceReference == "erp:purchase-order:PO-1001");
    }

    [Fact]
    public void Late_same_day_scheduled_receipt_exceptions_keep_receipt_pegging_separate()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("SO-1001", "SKU-FG-1000", "pcs", "SITE-01", 12m, new DateOnly(2026, 6, 1), "sales-order"),
            ],
            availability: [],
            bomComponents: [],
            scheduledReceipts:
            [
                new ScheduledReceiptSnapshot("SKU-FG-1000", "pcs", "SITE-01", 5m, new DateOnly(2026, 6, 5), "erp", "purchase-order", "PO-1001"),
                new ScheduledReceiptSnapshot("SKU-FG-1000", "pcs", "SITE-01", 7m, new DateOnly(2026, 6, 5), "erp", "purchase-order", "PO-1002"),
            ]);

        var suggestions = MrpCalculator.Calculate(input)
            .Where(x => x.SuggestionType == "reschedule-in")
            .OrderBy(x => x.Quantity)
            .ToArray();

        Assert.Equal(2, suggestions.Length);
        Assert.All(suggestions, suggestion =>
        {
            var receiptLink = Assert.Single(suggestion.PeggingLinks, x => x.PeggingType == "scheduled-receipt");
            Assert.Equal(suggestion.Quantity, receiptLink.Quantity);
        });
        Assert.Contains(suggestions, x => x.Quantity == 5m && x.PeggingLinks.Any(y => y.DemandSourceReference == "erp:purchase-order:PO-1001"));
        Assert.Contains(suggestions, x => x.Quantity == 7m && x.PeggingLinks.Any(y => y.DemandSourceReference == "erp:purchase-order:PO-1002"));
    }

    [Fact]
    public void Early_scheduled_receipt_used_by_future_requirement_creates_reschedule_out_exception()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("SO-1001", "SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 10), "sales-order"),
            ],
            availability: [],
            bomComponents: [],
            scheduledReceipts:
            [
                new ScheduledReceiptSnapshot("SKU-FG-1000", "pcs", "SITE-01", 10m, new DateOnly(2026, 6, 1), "erp", "purchase-order", "PO-1001"),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var exception = Assert.Single(suggestions);
        Assert.Equal("reschedule-out", exception.SuggestionType);
        Assert.Equal(10m, exception.Quantity);
        Assert.Equal(new DateOnly(2026, 6, 10), exception.RequiredDate);
        Assert.Equal(new DateOnly(2026, 6, 1), exception.ReleaseDate);
        Assert.Equal("scheduled-receipt-early", exception.ReasonCode);
        Assert.Contains(exception.PeggingLinks, x => x.PeggingType == "demand" && x.DemandSourceReference == "SO-1001");
        Assert.Contains(exception.PeggingLinks, x => x.PeggingType == "scheduled-receipt" && x.DemandSourceReference == "erp:purchase-order:PO-1001");
    }

    [Fact]
    public void Unused_scheduled_receipt_creates_cancel_exception_when_no_matching_requirement_exists()
    {
        var input = NewInput(
            demands: [],
            availability: [],
            bomComponents: [],
            scheduledReceipts:
            [
                new ScheduledReceiptSnapshot("SKU-RM-1000", "pcs", "SITE-01", 6m, new DateOnly(2026, 6, 15), "erp", "purchase-order", "PO-2001"),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var exception = Assert.Single(suggestions);
        Assert.Equal("cancel", exception.SuggestionType);
        Assert.Equal("SKU-RM-1000", exception.SkuCode);
        Assert.Equal(6m, exception.Quantity);
        Assert.Equal(new DateOnly(2026, 6, 15), exception.RequiredDate);
        Assert.Equal(new DateOnly(2026, 6, 15), exception.ReleaseDate);
        Assert.Equal("scheduled-receipt-unneeded", exception.ReasonCode);
        var receipt = Assert.Single(exception.PeggingLinks);
        Assert.Equal("scheduled-receipt", receipt.PeggingType);
        Assert.Equal("erp:purchase-order:PO-2001", receipt.DemandSourceReference);
    }

    [Fact]
    public void Cancel_exception_keeps_receipt_quantity_needed_to_restore_safety_stock()
    {
        var input = NewInput(
            demands: [],
            availability:
            [
                new InventoryAvailabilitySnapshot("SKU-RM-1000", "pcs", "SITE-01", 2m),
            ],
            bomComponents: [],
            scheduledReceipts:
            [
                new ScheduledReceiptSnapshot("SKU-RM-1000", "pcs", "SITE-01", 6m, new DateOnly(2026, 6, 15), "erp", "purchase-order", "PO-2001"),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-RM-1000", "pcs", "SITE-01", 0, 5m, null, null, null, ProcurementType: "buy"),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var exception = Assert.Single(suggestions, x => x.SuggestionType == "cancel");
        Assert.Equal(3m, exception.Quantity);
        Assert.Equal("scheduled-receipt-unneeded", exception.ReasonCode);
        var safetyReceipt = Assert.Single(suggestions, x => x.SuggestionType == "reschedule-in");
        Assert.Equal(3m, safetyReceipt.Quantity);
        Assert.Equal("scheduled-receipt-late", safetyReceipt.ReasonCode);
        var safetyPegging = Assert.Single(safetyReceipt.PeggingLinks, x => x.SourceType == "safety-stock");
        Assert.Equal("safety-stock", safetyPegging.PeggingType);
        Assert.Equal(3m, safetyPegging.Quantity);
        Assert.Equal(0m, safetyPegging.GrossDemandQuantity);
        Assert.DoesNotContain(suggestions, x => x.SuggestionType is "planned-purchase" or "planned-work-order");
        var receipt = Assert.Single(exception.PeggingLinks);
        Assert.Equal(3m, receipt.Quantity);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_uom_conversion_factor_fails_instead_of_zeroing_requirement(int conversionFactor)
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
                new UomConversionSnapshot("box", "pcs", conversionFactor, 0m, 0, "half-up"),
            ]);

        var exception = Assert.Throws<InvalidOperationException>(() => MrpCalculator.Calculate(input));

        Assert.Contains("Invalid global UOM conversion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("SKU-FG-1000", exception.Message, StringComparison.Ordinal);
        Assert.Contains("box", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pcs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Negative_uom_conversion_result_fails_instead_of_swallowing_requirement()
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
                new UomConversionSnapshot("box", "pcs", 1m, -2m, 0, "half-up"),
            ]);

        var exception = Assert.Throws<InvalidOperationException>(() => MrpCalculator.Calculate(input));

        Assert.Contains("negative quantity", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Shared_component_across_bom_levels_is_netted_once_at_its_lowest_level()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-FG-A", "SKU-FG-A", "pcs", "SITE-01", 1m, new DateOnly(2026, 6, 1)),
                new DemandSnapshot("DEMAND-FG-B", "SKU-FG-B", "pcs", "SITE-01", 1m, new DateOnly(2026, 6, 1)),
            ],
            availability: [],
            productionVersions:
            [
                new ProductionVersionSnapshot("SKU-FG-A", "PV-FG-A", "MBOM-FG-A", "ROUTING-FG-A"),
                new ProductionVersionSnapshot("SKU-FG-B", "PV-FG-B", "MBOM-FG-B", "ROUTING-FG-B"),
                new ProductionVersionSnapshot("SKU-SA", "PV-SA", "MBOM-SA", "ROUTING-SA"),
            ],
            bomComponents:
            [
                new BomComponentSnapshot("SKU-FG-A", "SKU-RM-X", "pcs", 7m),
                new BomComponentSnapshot("SKU-FG-B", "SKU-SA", "pcs", 1m),
                new BomComponentSnapshot("SKU-SA", "SKU-RM-X", "pcs", 7m),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot("SKU-RM-X", "pcs", "SITE-01", 0, 0m, 10m, null, null, ProcurementType: "buy"),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var rawMaterialPurchase = Assert.Single(suggestions, x => x.SkuCode == "SKU-RM-X");
        Assert.Equal("planned-purchase", rawMaterialPurchase.SuggestionType);
        Assert.Equal(14m, rawMaterialPurchase.Quantity);
        Assert.Contains(rawMaterialPurchase.PeggingLinks, x => x.DemandSourceReference == "DEMAND-FG-A" && x.Quantity == 7m);
        Assert.Contains(rawMaterialPurchase.PeggingLinks, x => x.DemandSourceReference == "DEMAND-FG-B" && x.Quantity == 7m);
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
    public void Procurement_type_drives_make_buy_suggestion_type_lead_time_and_lot_policy()
    {
        var input = NewInput(
            demands:
            [
                new DemandSnapshot("DEMAND-MAKE", "SKU-MAKE", "pcs", "SITE-01", 7m, new DateOnly(2026, 6, 10)),
                new DemandSnapshot("DEMAND-BUY", "SKU-BUY", "pcs", "SITE-01", 7m, new DateOnly(2026, 6, 10)),
            ],
            availability: [],
            productionVersions:
            [
                new ProductionVersionSnapshot("SKU-MAKE", "PV-MAKE", "MBOM-MAKE", "ROUTING-MAKE"),
                new ProductionVersionSnapshot("SKU-BUY", "PV-BUY", "MBOM-BUY", "ROUTING-BUY"),
            ],
            bomComponents:
            [
                new BomComponentSnapshot("SKU-BUY", "SKU-BUY-COMPONENT", "pcs", 2m),
            ],
            planningParameters:
            [
                new PlanningParameterSnapshot(
                    "SKU-MAKE",
                    "pcs",
                    "SITE-01",
                    0,
                    0m,
                    null,
                    null,
                    5m,
                    ProcurementType: "make",
                    MrpType: "mrp",
                    LotSizingPolicy: "fixed-lot",
                    ReorderPointQuantity: null,
                    PlannedDeliveryTimeDays: 9,
                    InHouseProductionTimeDays: 3,
                    GoodsReceiptProcessingTimeDays: 1),
                new PlanningParameterSnapshot(
                    "SKU-BUY",
                    "pcs",
                    "SITE-01",
                    0,
                    0m,
                    null,
                    null,
                    5m,
                    ProcurementType: "buy",
                    MrpType: "mrp",
                    LotSizingPolicy: "fixed-lot",
                    ReorderPointQuantity: null,
                    PlannedDeliveryTimeDays: 9,
                    InHouseProductionTimeDays: 3,
                    GoodsReceiptProcessingTimeDays: 1),
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SkuCode == "SKU-MAKE");
        Assert.Equal("planned-work-order", workOrder.SuggestionType);
        Assert.Equal(10m, workOrder.Quantity);
        Assert.Equal(new DateOnly(2026, 6, 6), workOrder.ReleaseDate);

        var purchase = Assert.Single(suggestions, x => x.SkuCode == "SKU-BUY");
        Assert.Equal("planned-purchase", purchase.SuggestionType);
        Assert.Equal(10m, purchase.Quantity);
        Assert.Equal(new DateOnly(2026, 5, 31), purchase.ReleaseDate);
        Assert.DoesNotContain(suggestions, x => x.SkuCode == "SKU-BUY-COMPONENT");
        Assert.All(purchase.PeggingLinks, x =>
        {
            Assert.Null(x.ProductionVersionReference);
            Assert.Null(x.ManufacturingBomReference);
            Assert.Null(x.RoutingReference);
        });
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
