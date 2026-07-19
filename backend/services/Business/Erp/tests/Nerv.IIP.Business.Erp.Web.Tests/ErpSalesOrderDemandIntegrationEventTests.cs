using System.Text.RegularExpressions;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpSalesOrderDemandIntegrationEventTests
{
    [Fact]
    public void Released_converter_emits_complete_versioned_sales_order_snapshot()
    {
        var order = CreateReleasedOrder();

        var integrationEvent = new SalesOrderReleasedIntegrationEventConverter(Context)
            .Convert(new SalesOrderReleasedDomainEvent(order));

        Assert.Equal(ErpIntegrationEventTypes.SalesOrderReleased, integrationEvent.EventType);
        Assert.Equal(ErpIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal(ErpIntegrationEventSources.BusinessErp, integrationEvent.SourceService);
        Assert.Equal("corr-http-001", integrationEvent.CorrelationId);
        Assert.Equal("command-create-so-001", integrationEvent.CausationId);
        Assert.Equal("user:planner-001", integrationEvent.Actor);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Equal("SO-DEMO-001", integrationEvent.Payload.SalesOrderNo);
        Assert.Equal("CUST-001", integrationEvent.Payload.CustomerCode);
        Assert.Equal("SITE-001", integrationEvent.Payload.SiteCode);
        Assert.Equal(1, integrationEvent.Payload.OrderVersion);
        Assert.Equal("released", integrationEvent.Payload.Status);
        var line = Assert.Single(integrationEvent.Payload.Lines);
        Assert.Equal("10", line.SalesOrderLineNo);
        Assert.Equal("SKU-FG", line.SkuCode);
        Assert.Equal("EA", line.UomCode);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(new DateOnly(2026, 8, 15), line.RequiredDate);
        Assert.False(line.Cancelled);
        Assert.Matches(new Regex("^[A-Za-z0-9:_-]+$"), integrationEvent.IdempotencyKey);
        Assert.DoesNotContain('+', integrationEvent.IdempotencyKey);
        Assert.Contains(":v1:released", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Changed_and_cancelled_converters_use_distinct_facts_and_order_versions()
    {
        var order = CreateReleasedOrder();
        order.ClearDomainEvents();
        order.ChangeLine("10", 3m, 10m, new DateOnly(2026, 8, 16), "customer changed quantity");

        var changed = new SalesOrderChangedIntegrationEventConverter(Context)
            .Convert(new SalesOrderChangedDomainEvent(order));

        Assert.Equal(ErpIntegrationEventTypes.SalesOrderChanged, changed.EventType);
        Assert.Equal(2, changed.Payload.OrderVersion);
        Assert.Equal(3m, Assert.Single(changed.Payload.Lines).Quantity);
        Assert.Contains(":v2:changed", changed.IdempotencyKey, StringComparison.Ordinal);

        order.Cancel("customer cancelled");
        var cancelled = new SalesOrderCancelledIntegrationEventConverter(Context)
            .Convert(new SalesOrderCancelledDomainEvent(order));

        Assert.Equal(ErpIntegrationEventTypes.SalesOrderCancelled, cancelled.EventType);
        Assert.Equal(3, cancelled.Payload.OrderVersion);
        Assert.Equal("cancelled", cancelled.Payload.Status);
        Assert.True(Assert.Single(cancelled.Payload.Lines).Cancelled);
        Assert.Contains(":v3:cancelled", cancelled.IdempotencyKey, StringComparison.Ordinal);
    }

    private static SalesOrder CreateReleasedOrder()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-DEMAND-001",
            "CUST-001",
            new DateOnly(2026, 8, 1),
            [new QuotationLineDraft("10", "SKU-FG", "EA", 2m, 10m, new DateOnly(2026, 8, 15))]);
        quotation.Approve();
        return SalesOrder.CreateFromQuotation("SO-DEMO-001", "SITE-001", quotation);
    }

    private static readonly IErpIntegrationEventContextAccessor Context = new StaticContextAccessor();

    private sealed class StaticContextAccessor : IErpIntegrationEventContextAccessor
    {
        public ErpIntegrationEventContext GetContext() => new("corr-http-001", "command-create-so-001", "user:planner-001");
    }
}
