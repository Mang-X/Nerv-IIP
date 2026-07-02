using System.Text.Json;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.DomainEvents;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryIntegrationEventTests
{
    [Fact]
    public void Stock_movement_posted_event_uses_stable_adr0011_envelope_shape()
    {
        var converter = new StockMovementPostedIntegrationEventConverter(new StubInventoryIntegrationEventContextAccessor());
        var movement = DomainMovementFactory.Inbound(12.5m);
        AssignId(movement, new StockMovementId(Guid.CreateVersion7()));
        var domainEvent = new StockMovementPostedDomainEvent(movement);

        var integrationEvent = converter.Convert(domainEvent);
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("inventory.StockMovementPosted", integrationEvent.EventType);
        Assert.Equal(1, integrationEvent.EventVersion);
        Assert.Equal("business-inventory", integrationEvent.SourceService);
        Assert.Equal("org-001", integrationEvent.OrganizationId);
        Assert.Equal("env-dev", integrationEvent.EnvironmentId);
        Assert.Equal("inventory:stock-movement-posted:org-001:env-dev:wms:DOC-001:idem-in-001", integrationEvent.IdempotencyKey);
        Assert.Equal(movement.Id.ToString(), integrationEvent.Payload.InventoryMovementId);
        Assert.Equal("SKU-FG-1000", integrationEvent.Payload.SkuCode);
        Assert.Contains("\"eventType\":\"inventory.StockMovementPosted\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Stock_count_variance_event_uses_required_event_type()
    {
        var ledger = DomainLedgerFactory.NewLedger();
        ledger.ApplyMovement(DomainMovementFactory.Inbound(10m));
        var task = DomainCountTaskFactory.NewTask(ledger);
        var adjustment = task.ConfirmAdjustment(ledger, 7.5m, "idem-count-001");
        var converter = new StockCountVarianceConfirmedIntegrationEventConverter(new StubInventoryIntegrationEventContextAccessor());

        var integrationEvent = converter.Convert(new StockCountVarianceConfirmedDomainEvent(task, adjustment));

        Assert.Equal("inventory.StockCountVarianceConfirmed", integrationEvent.EventType);
        Assert.Equal(-2.5m, integrationEvent.Payload.VarianceQuantity);
        Assert.Equal("COUNT-001", integrationEvent.Payload.CountTaskCode);
    }

    [Fact]
    public void Stock_availability_changed_event_uses_required_event_type()
    {
        var ledger = DomainLedgerFactory.NewLedger();
        ledger.ApplyMovement(DomainMovementFactory.Inbound(4m));
        var converter = new StockAvailabilityChangedIntegrationEventConverter(new StubInventoryIntegrationEventContextAccessor());

        var integrationEvent = converter.Convert(new StockAvailabilityChangedDomainEvent(ledger));

        Assert.Equal("inventory.StockAvailabilityChanged", integrationEvent.EventType);
        Assert.Equal(4m, integrationEvent.Payload.OnHandQuantity);
        Assert.Equal(4m, integrationEvent.Payload.AvailableQuantity);
    }

    [Fact]
    public void Stock_movement_posting_failed_event_uses_required_adr0011_envelope_shape()
    {
        var integrationEvent = new StockMovementPostingFailedIntegrationEvent(
            "evt-failed-001",
            InventoryIntegrationEventTypes.StockMovementPostingFailed,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-15T00:00:00Z"),
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:business-inventory",
            "inventory:stock-movement-posting-failed:org-001:env-dev:wms:OUT-001:idem-out-001",
            new StockMovementPostingFailedPayload(
                "outbound",
                "wms",
                "OUT-001",
                "LINE-001",
                "idem-out-001",
                "SKU-FG-1000",
                "kg",
                "SITE-01",
                "LOC-A-01",
                "LOT-001",
                null,
                "qualified",
                "company",
                "owner-001",
                -5m,
                "NEGATIVE_ON_HAND",
                "Stock movement would make on-hand quantity negative.",
                DateTimeOffset.Parse("2026-06-15T00:00:00Z")));
        var json = JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("inventory.StockMovementPostingFailed", integrationEvent.EventType);
        Assert.Equal("NEGATIVE_ON_HAND", integrationEvent.Payload.FailureCode);
        Assert.Equal("OUT-001", integrationEvent.Payload.SourceDocumentId);
        Assert.Contains("\"eventType\":\"inventory.StockMovementPostingFailed\"", json, StringComparison.Ordinal);
    }

    private sealed class StubInventoryIntegrationEventContextAccessor(
        InventoryIntegrationEventContext? context = null)
        : IInventoryIntegrationEventContextAccessor
    {
        public InventoryIntegrationEventContext GetContext()
        {
            return context ?? new InventoryIntegrationEventContext(
                "corr-test-001",
                "cause-test-001",
                "system:business-inventory");
        }
    }

    private static void AssignId(StockMovement movement, StockMovementId id)
    {
        var setter = typeof(StockMovement)
            .GetProperty(nameof(StockMovement.Id))?
            .GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("StockMovement.Id setter was not found.");
        setter.Invoke(movement, [id]);
    }
}
