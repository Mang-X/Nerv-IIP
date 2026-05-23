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
            ]);

        var suggestions = MrpCalculator.Calculate(input);

        var workOrder = Assert.Single(suggestions, x => x.SuggestionType == "planned-work-order");
        var purchase = Assert.Single(suggestions, x => x.SuggestionType == "planned-purchase");
        Assert.Equal("SKU-FG-1000", workOrder.SkuCode);
        Assert.Equal(8m, workOrder.Quantity);
        Assert.Equal("SKU-RM-1000", purchase.SkuCode);
        Assert.Equal(19m, purchase.Quantity);
        Assert.All(suggestions, x => Assert.Contains(x.PeggingLinks, p => p.DemandSourceReference == "DEMAND-001"));
    }
}
