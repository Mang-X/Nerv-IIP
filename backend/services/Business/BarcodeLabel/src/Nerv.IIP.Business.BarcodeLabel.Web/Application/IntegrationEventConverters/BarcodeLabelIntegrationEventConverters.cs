using Nerv.IIP.Business.BarcodeLabel.Domain.DomainEvents;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEvents;
using Nerv.IIP.Contracts.BarcodeLabel;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.IntegrationEventConverters;

public sealed class LabelPrintBatchCreatedIntegrationEventConverter
    : IIntegrationEventConverter<LabelPrintBatchCreatedDomainEvent, LabelPrintBatchCreatedIntegrationEvent>
{
    public LabelPrintBatchCreatedIntegrationEvent Convert(LabelPrintBatchCreatedDomainEvent domainEvent)
    {
        var batch = domainEvent.LabelPrintBatch;
        return new LabelPrintBatchCreatedIntegrationEvent(
            batch.Id,
            batch.BarcodeRuleId,
            batch.LabelTemplateId,
            batch.OrganizationId,
            batch.EnvironmentId,
            batch.SourceDocumentType,
            batch.SourceDocumentId,
            batch.IdempotencyKey,
            batch.RequestedQuantity);
    }
}

public sealed class LabelPrintBatchCompletedIntegrationEventConverter
    : IIntegrationEventConverter<LabelPrintBatchCompletedDomainEvent, LabelPrintBatchCompletedIntegrationEvent>
{
    public LabelPrintBatchCompletedIntegrationEvent Convert(LabelPrintBatchCompletedDomainEvent domainEvent)
    {
        var batch = domainEvent.LabelPrintBatch;
        return new LabelPrintBatchCompletedIntegrationEvent(
            batch.Id,
            batch.OrganizationId,
            batch.EnvironmentId,
            batch.SourceDocumentType,
            batch.SourceDocumentId,
            batch.IdempotencyKey,
            batch.RequestedQuantity);
    }
}

public sealed class LabelScannedIntegrationEventConverter
    : IIntegrationEventConverter<LabelScannedDomainEvent, LabelScannedIntegrationEvent>
{
    public LabelScannedIntegrationEvent Convert(LabelScannedDomainEvent domainEvent)
    {
        var scan = domainEvent.ScanRecord;
        return new LabelScannedIntegrationEvent(
            scan.Id,
            scan.OrganizationId,
            scan.EnvironmentId,
            scan.DeviceCode,
            scan.ScannedValue,
            scan.SourceWorkflow,
            scan.SourceDocumentId,
            scan.IdempotencyKey);
    }
}

public sealed class ScanRejectedIntegrationEventConverter
    : IIntegrationEventConverter<ScanRejectedDomainEvent, ScanRejectedIntegrationEvent>
{
    public ScanRejectedIntegrationEvent Convert(ScanRejectedDomainEvent domainEvent)
    {
        var scan = domainEvent.ScanRecord;
        return new ScanRejectedIntegrationEvent(
            scan.Id,
            scan.OrganizationId,
            scan.EnvironmentId,
            scan.DeviceCode,
            scan.ScannedValue,
            scan.SourceWorkflow,
            scan.SourceDocumentId,
            scan.IdempotencyKey,
            scan.RejectionReason);
    }
}

public sealed class BarcodeScanAcceptedIntegrationEventConverter
    : IIntegrationEventConverter<LabelScannedDomainEvent, BarcodeScanAcceptedIntegrationEvent>
{
    public BarcodeScanAcceptedIntegrationEvent Convert(LabelScannedDomainEvent domainEvent)
    {
        var scan = domainEvent.ScanRecord;
        return new BarcodeScanAcceptedIntegrationEvent(
            $"evt-barcode-scan-{scan.Id}",
            BarcodeLabelIntegrationEventTypes.BarcodeScanAccepted,
            BarcodeLabelIntegrationEventVersions.V1,
            scan.ScannedAtUtc,
            BarcodeLabelIntegrationEventSources.BusinessBarcodeLabel,
            scan.IdempotencyKey,
            scan.Id.ToString(),
            scan.OrganizationId,
            scan.EnvironmentId,
            "system:barcode-label",
            scan.IdempotencyKey,
            new BarcodeScanAcceptedPayload(
                scan.Id.ToString(),
                scan.DeviceCode,
                scan.ScannedValue,
                scan.SourceWorkflow,
                scan.SourceDocumentId,
                scan.Gtin,
                scan.LotNo,
                scan.SerialNumber,
                scan.Quantity,
                scan.ScannedAtUtc));
    }
}

public sealed class InventoryMovementRequestedFromBarcodeScanIntegrationEventConverter
    : IIntegrationEventConverter<InventoryMovementRequestedFromScanDomainEvent, InventoryMovementRequestedIntegrationEvent>
{
    public InventoryMovementRequestedIntegrationEvent Convert(InventoryMovementRequestedFromScanDomainEvent domainEvent)
    {
        var scan = domainEvent.ScanRecord;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var idempotencyKey = $"barcode:{scan.OrganizationId}:{scan.EnvironmentId}:{scan.IdempotencyKey}";
        return new InventoryMovementRequestedIntegrationEvent(
            scan.DownstreamEventId ?? $"evt-{Guid.CreateVersion7():N}",
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            occurredAtUtc,
            "barcode-label",
            idempotencyKey,
            scan.Id.ToString(),
            scan.OrganizationId,
            scan.EnvironmentId,
            "system:barcode-label",
            idempotencyKey,
            new InventoryMovementRequestedPayload(
                MovementType(scan.SourceWorkflow),
                "barcode-label",
                scan.SourceDocumentId,
                null,
                idempotencyKey,
                Required(scan.SkuCode, nameof(scan.SkuCode)),
                Required(scan.UomCode, nameof(scan.UomCode)),
                Required(scan.SiteCode, nameof(scan.SiteCode)),
                Required(scan.LocationCode, nameof(scan.LocationCode)),
                scan.LotNo,
                scan.SerialNumber,
                Required(scan.QualityStatus, nameof(scan.QualityStatus)),
                Required(scan.OwnerType, nameof(scan.OwnerType)),
                scan.OwnerId,
                Quantity(scan),
                occurredAtUtc));
    }

    private static string MovementType(string sourceWorkflow)
    {
        return sourceWorkflow switch
        {
            "inventory.receipt" => "inbound",
            "inventory.issue" => "outbound",
            "inventory.adjustment" => "adjustment",
            _ => throw new InvalidOperationException($"Unsupported inventory scan workflow '{sourceWorkflow}'."),
        };
    }

    private static decimal Quantity(Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate.ScanRecord scan)
    {
        var quantity = scan.Quantity ?? throw new InvalidOperationException("Inventory scan quantity is required.");
        return scan.SourceWorkflow == "inventory.issue" ? -Math.Abs(quantity) : quantity;
    }

    private static string Required(string? value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} is required for inventory scan routing.")
            : value;
    }
}
