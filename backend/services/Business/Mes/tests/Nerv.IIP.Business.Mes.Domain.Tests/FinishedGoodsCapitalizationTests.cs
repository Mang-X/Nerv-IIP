using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class FinishedGoodsCapitalizationTests
{
    [Fact]
    public void Uncosted_receipt_waits_for_erp_capitalization_before_requesting_inventory_posting()
    {
        var receipt = FinishedGoodsReceiptRequest.Create("org-001", "env-dev", "FGR-001", "WO-001", "FG-001", 8m, "ea", DateTimeOffset.UtcNow);

        Assert.Empty(receipt.GetDomainEvents());

        receipt.ApplyCapitalizedUnitCost(20m);

        Assert.Equal(20m, receipt.UnitCost);
        var posting = Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(Assert.Single(receipt.GetDomainEvents()));
        Assert.Equal(8m, posting.Quantity);
    }
}
