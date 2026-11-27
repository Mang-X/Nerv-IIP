using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.Tests;

public sealed class PendingInboundReceiptTests
{
    [Fact]
    public void Pending_receipt_requires_explicit_actual_quantity_before_putaway_or_posting()
    {
        var order = InboundOrder.CreatePendingReceipt(
            "org", "env", "IN-001", "purchase-order", "PO-001", "site",
            [new("10", "SKU-001", "EA", 5m, "STAGE", "unrestricted", "company", null)]);

        var line = Assert.Single(order.Lines);
        Assert.Equal(5m, line.ExpectedQuantity);
        Assert.Equal(0m, line.ReceivedQuantity);
        Assert.Throws<InvalidOperationException>(() => order.Complete("receipt", order.Version));
        Assert.Throws<InvalidOperationException>(() =>
            order.CreatePutawayTask("PUT-001", "10", "STAGE", "BIN", 5m));
        Assert.DoesNotContain(order.GetDomainEvents(), item => item is InboundOrderCompletedDomainEvent);

        order.ConfirmReceipt(order.Version, [new("10", 5m)]);
        Assert.DoesNotContain(order.GetDomainEvents(), item => item is InboundOrderCompletedDomainEvent);
        Assert.Equal(5m, order.CreatePutawayTask("PUT-001", "10", "STAGE", "BIN", 5m).PlannedQuantity);
        Assert.Equal(5m, Assert.Single(order.Complete("receipt", order.Version)).Quantity);
        Assert.Equal(5m, line.ExpectedQuantity);
    }

    [Theory]
    [InlineData("20", 0)]
    [InlineData("20", 2)]
    [InlineData("20", 4)]
    [InlineData("10", 5)]
    [InlineData("unknown", 3)]
    public void Invalid_confirmation_leaves_every_line_pending(string secondLineNo, int secondQuantity)
    {
        var order = InboundOrder.CreatePendingReceipt(
            "org", "env", "IN-002", "purchase-order", "PO-002", "site",
            [
                new("10", "SKU-001", "EA", 5m, "STAGE", "unrestricted", "company", null),
                new("20", "SKU-002", "EA", 3m, "STAGE", "unrestricted", "company", null),
            ]);
        var version = order.Version;

        Assert.Throws<InvalidOperationException>(() => order.ConfirmReceipt(version, [new("10", 5m)]));
        Assert.Throws<InvalidOperationException>(() =>
            order.ConfirmReceipt(version, [new("10", 5m), new(secondLineNo, secondQuantity)]));

        Assert.Equal(version, order.Version);
        Assert.All(order.Lines, line => Assert.Equal(0m, line.ReceivedQuantity));
        Assert.Throws<InvalidOperationException>(() => order.Complete("receipt", version));
        order.ConfirmReceipt(version, [new("10", 5m), new("20", 3m)]);
        Assert.Throws<InvalidOperationException>(() => order.Complete("receipt", version));
        Assert.Equal(2, order.Complete("receipt", order.Version).Count);
    }
}
