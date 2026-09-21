using System.Text.Json;
using DotNetCore.CAP;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.IntegrationEvents;
using NetCorePal.Extensions.DistributedTransactions.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpProcurementIntegrationEventTests
{
    // NERV-2130：收货暂估冻结，不能随已批准的订单后续改价变化。
    [Fact]
    public void Receipt_inventory_cost_keeps_receipt_price_after_order_amendment()
    {
        var order = PurchaseOrder.Create("org-cost", "env-cost", "PO-cost", "SUP", "SITE",
            [new PurchaseOrderLineDraft("10", "SKU", "pcs", 10m, 1.4m, new DateOnly(2026, 9, 1))]);
        order.MarkApprovalRequested("approval");
        order.ReleaseAfterApproval("approval");
        var receipt = PurchaseReceipt.Record(order, "RCV-cost", [new PurchaseReceiptLineDraft("10", 8m, "unrestricted")]);
        var change = order.RequestChange([new PurchaseOrderLineChangeDraft("10", 10m, 9m, new DateOnly(2026, 9, 1))]);
        change.AssignApprovalChain("amendment");
        order.ApplyApprovedChange("amendment");

        var movement = new PurchaseReceiptInventoryMovementRequestedIntegrationEventConverter().Convert(
            new PurchaseReceiptInventoryMovementRequestedDomainEvent(receipt, Assert.Single(receipt.Lines)));
        Assert.Equal(1.4m, movement.Payload.UnitCost);
        Assert.Equal(11.2m, movement.Payload.UnitCost * movement.Payload.Quantity);
    }

    [Fact]
    public void Purchase_requisition_created_event_uses_stable_adr0011_envelope_shape()
    {
        var requisition = PurchaseRequisition.CreateFromSuggestion(
            "org-001",
            "env-dev",
            "REQ-001",
            "MPS-SUG-001",
            "SKU-RM-1000",
            "kg",
            "SITE-01",
            120m,
            new DateOnly(2026, 6, 1));
        var converter = new PurchaseRequisitionCreatedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new PurchaseRequisitionCreatedDomainEvent(requisition));
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("erp.PurchaseRequisitionCreated", integrationEvent.EventType);
        Assert.Equal(1, integrationEvent.EventVersion);
        Assert.Equal("business-erp", integrationEvent.SourceService);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Contains("MPS-SUG-001", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
        Assert.IsAssignableFrom<IIntegrationEventEnvelope>(integrationEvent);
        Assert.Contains("\"eventType\":\"erp.PurchaseRequisitionCreated\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Purchase_order_released_event_uses_required_event_type()
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]);
        var converter = new PurchaseOrderReleasedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new PurchaseOrderReleasedDomainEvent(order));

        Assert.Equal("erp.PurchaseOrderReleased", integrationEvent.EventType);
        Assert.Equal("PO-001", integrationEvent.Payload.PurchaseOrderNo);
        Assert.Equal(36m, integrationEvent.Payload.TotalAmount);
    }

    [Fact]
    public void Purchase_receipt_recorded_event_uses_required_event_type()
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            "PO-001",
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 3m, 12m, new DateOnly(2026, 6, 5))]);
        order.MarkApprovalRequested("approval-chain-001");
        order.ReleaseAfterApproval("approval-chain-001");
        var receipt = PurchaseReceipt.Record(order, "RCV-001", [new PurchaseReceiptLineDraft("LINE-001", 2m, "accepted")]);
        var converter = new PurchaseReceiptRecordedIntegrationEventConverter();

        var integrationEvent = converter.Convert(new PurchaseReceiptRecordedDomainEvent(receipt));

        Assert.Equal("erp.PurchaseReceiptRecorded", integrationEvent.EventType);
        Assert.Equal("RCV-001", integrationEvent.Payload.PurchaseReceiptNo);
        Assert.Equal("accepted", integrationEvent.Payload.QualityStatus);
        var line = Assert.Single(integrationEvent.Payload.Lines!);
        Assert.Equal("LINE-001", line.LineReference);
        Assert.Equal("SKU-RM-1000", line.SkuCode);
        Assert.Equal(2m, line.ReceivedQuantity);
    }

    [Fact]
    public void Material_supply_eta_changed_event_exposes_stable_sku_invalidation_contract()
    {
        var domainEvent = new MaterialSupplyEtaChangedDomainEvent(
            "org-001",
            "env-dev",
            "purchase-order",
            "PO-001",
            "purchase-order-change-approved",
            "purchase-order:PO-001:change:2",
            new DateTimeOffset(2026, 6, 2, 3, 4, 5, TimeSpan.Zero),
            ["SKU-RM-2000", "SKU-RM-1000", "SKU-RM-1000"]);

        var integrationEvent = new MaterialSupplyEtaChangedIntegrationEventConverter(new StaticContextAccessor()).Convert(domainEvent);
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<MaterialSupplyEtaChangedIntegrationEvent>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.StartsWith("evt-", integrationEvent.EventId, StringComparison.Ordinal);
        Assert.Equal("erp.MaterialSupplyEtaChanged", integrationEvent.EventType);
        Assert.Equal(ErpIntegrationEventVersions.V1, integrationEvent.EventVersion);
        Assert.Equal(ErpIntegrationEventSources.BusinessErp, integrationEvent.SourceService);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Equal("corr-eta-001", integrationEvent.CorrelationId);
        Assert.Equal("command:record-receipt:001", integrationEvent.CausationId);
        Assert.Equal("user:buyer-001", integrationEvent.Actor);
        Assert.EndsWith("purchase-order:PO-001:change:2", integrationEvent.IdempotencyKey, StringComparison.Ordinal);
        Assert.Equal("purchase-order", integrationEvent.Payload.SourceDocumentType);
        Assert.Equal("PO-001", integrationEvent.Payload.SourceDocumentNo);
        Assert.Equal("purchase-order-change-approved", integrationEvent.Payload.ChangeReason);
        Assert.Equal(domainEvent.ChangedAtUtc, integrationEvent.OccurredAtUtc);
        Assert.Equal(["SKU-RM-1000", "SKU-RM-2000"], integrationEvent.Payload.SkuCodes);
        Assert.NotNull(roundTripped);
        Assert.Equal(integrationEvent.EventId, roundTripped.EventId);
        Assert.Equal(integrationEvent.EventType, roundTripped.EventType);
        Assert.Equal(integrationEvent.EventVersion, roundTripped.EventVersion);
        Assert.Equal(integrationEvent.OccurredAtUtc, roundTripped.OccurredAtUtc);
        Assert.Equal(integrationEvent.SourceService, roundTripped.SourceService);
        Assert.Equal(integrationEvent.CorrelationId, roundTripped.CorrelationId);
        Assert.Equal(integrationEvent.CausationId, roundTripped.CausationId);
        Assert.Equal(integrationEvent.OrganizationId, roundTripped.OrganizationId);
        Assert.Equal(integrationEvent.EnvironmentId, roundTripped.EnvironmentId);
        Assert.Equal(integrationEvent.Actor, roundTripped.Actor);
        Assert.Equal(integrationEvent.IdempotencyKey, roundTripped.IdempotencyKey);
        Assert.Equal(integrationEvent.Payload.SkuCodes, roundTripped.Payload.SkuCodes);
    }

    [Fact]
    public async Task Material_supply_eta_changed_event_publishes_on_its_controlled_named_cap_alias()
    {
        var domainEvent = new MaterialSupplyEtaChangedDomainEvent(
            "org-001",
            "env-dev",
            "purchase-order",
            "PO-001",
            "purchase-order-change-approved",
            "purchase-order:PO-001:change:2",
            new DateTimeOffset(2026, 6, 2, 3, 4, 5, TimeSpan.Zero),
            ["SKU-RM-1000"]);
        var etaEvent = new MaterialSupplyEtaChangedIntegrationEventConverter(new StaticContextAccessor()).Convert(domainEvent);
        var otherErpEvent = new ErpIntegrationEvent<PurchaseOrderReleasedPayload>(
            "evt-other",
            ErpIntegrationEventTypes.PurchaseOrderReleased,
            ErpIntegrationEventVersions.V1,
            domainEvent.ChangedAtUtc,
            ErpIntegrationEventSources.BusinessErp,
            "corr-other",
            "cause-other",
            domainEvent.OrganizationId,
            domainEvent.EnvironmentId,
            "system:erp",
            "erp:purchase-order-released:PO-002",
            new PurchaseOrderReleasedPayload("po-id-002", "PO-002", "SUP-001", "SITE-01", 12m));
        var cap = new RecordingCapPublisher();
        var publisher = new CapIntegrationEventPublisher(cap, []);

        await publisher.PublishAsync(etaEvent, CancellationToken.None);
        await publisher.PublishAsync(otherErpEvent, CancellationToken.None);

        Assert.Equal(
            [nameof(MaterialSupplyEtaChangedIntegrationEvent), "ErpIntegrationEvent`1"],
            cap.Published.Select(item => item.Name));
    }

    private sealed class StaticContextAccessor : IErpIntegrationEventContextAccessor
    {
        public ErpIntegrationEventContext GetContext() =>
            new("corr-eta-001", "command:record-receipt:001", "user:buyer-001");
    }

    private sealed class RecordingCapPublisher : ICapPublisher
    {
        public List<(string Name, object? Content)> Published { get; } = [];
        public IServiceProvider ServiceProvider => throw new NotSupportedException();
        public ICapTransaction? Transaction { get; set; }

        public Task PublishAsync<T>(string name, T? contentObj, string? callbackName = null, CancellationToken cancellationToken = default)
        {
            Published.Add((name, contentObj));
            return Task.CompletedTask;
        }

        public Task PublishAsync<T>(string name, T? contentObj, IDictionary<string, string?> headers, CancellationToken cancellationToken = default)
        {
            Published.Add((name, contentObj));
            return Task.CompletedTask;
        }

        public void Publish<T>(string name, T? contentObj, string? callbackName = null) =>
            Published.Add((name, contentObj));

        public void Publish<T>(string name, T? contentObj, IDictionary<string, string?> headers) =>
            Published.Add((name, contentObj));

        public Task PublishDelayAsync<T>(TimeSpan delayTime, string name, T? contentObj, IDictionary<string, string?> headers, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PublishDelayAsync<T>(TimeSpan delayTime, string name, T? contentObj, string? callbackName = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void PublishDelay<T>(TimeSpan delayTime, string name, T? contentObj, IDictionary<string, string?> headers) =>
            throw new NotSupportedException();

        public void PublishDelay<T>(TimeSpan delayTime, string name, T? contentObj, string? callbackName = null) =>
            throw new NotSupportedException();
    }
}
