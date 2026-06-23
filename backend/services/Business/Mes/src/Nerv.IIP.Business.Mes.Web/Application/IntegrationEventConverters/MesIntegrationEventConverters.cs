using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using NetCorePal.Extensions.DistributedTransactions;
using System.Globalization;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;

public sealed class ProductionMaterialConsumedIntegrationEventConverter
    : IIntegrationEventConverter<ProductionMaterialConsumedDomainEvent, InventoryMovementRequestedIntegrationEvent>
{
    public InventoryMovementRequestedIntegrationEvent Convert(ProductionMaterialConsumedDomainEvent domainEvent)
    {
        var consumption = domainEvent.MaterialConsumption;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var idempotencyKey = EventIds.Idempotency(
            "production-consumption",
            consumption.OrganizationId,
            consumption.EnvironmentId,
            consumption.ReportNo,
            consumption.MaterialIssueRequestNo,
            consumption.MaterialId,
            consumption.MaterialLotId);
        EventIds.ThrowIfUnsupportedUom(consumption.UomCode, consumption.MaterialIssueRequestNo);

        return NewInventoryMovementRequested(
            consumption.OrganizationId,
            consumption.EnvironmentId,
            consumption.ReportNo,
            idempotencyKey,
            consumption.ReportNo,
            consumption.MaterialIssueRequestNo,
            consumption.MaterialId,
            consumption.UomCode,
            "production",
            "line-side",
            consumption.MaterialLotId,
            -Math.Abs(consumption.ConsumedQuantity),
            occurredAtUtc);
    }

    internal static InventoryMovementRequestedIntegrationEvent NewInventoryMovementRequested(
        string organizationId,
        string environmentId,
        string correlationId,
        string idempotencyKey,
        string sourceDocumentId,
        string? sourceDocumentLineId,
        string skuCode,
        string uomCode,
        string siteCode,
        string locationCode,
        string? lotNo,
        decimal quantity,
        DateTimeOffset requestedAtUtc,
        decimal? unitCost = null)
    {
        var movementType = quantity < 0 ? "outbound" : "inbound";
        return new InventoryMovementRequestedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            requestedAtUtc,
            InventoryIntegrationEventSources.BusinessMes,
            correlationId,
            sourceDocumentId,
            organizationId,
            environmentId,
            "system:mes",
            idempotencyKey,
            new InventoryMovementRequestedPayload(
                movementType,
                InventoryIntegrationEventSources.BusinessMes,
                sourceDocumentId,
                sourceDocumentLineId,
                idempotencyKey,
                skuCode,
                uomCode,
                siteCode,
                locationCode,
                lotNo,
                null,
                "Unrestricted",
                "production",
                null,
                quantity,
                requestedAtUtc,
                UnitCost: unitCost));
    }
}

public sealed class FinishedGoodsReceiptRequestedIntegrationEventConverter
    : IIntegrationEventConverter<FinishedGoodsReceiptRequestedDomainEvent, InventoryMovementRequestedIntegrationEvent>
{
    public InventoryMovementRequestedIntegrationEvent Convert(FinishedGoodsReceiptRequestedDomainEvent domainEvent)
    {
        var request = domainEvent.FinishedGoodsReceiptRequest;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var idempotencyKey = EventIds.Idempotency("finished-goods-receipt", request.OrganizationId, request.EnvironmentId, request.RequestNo);
        return ProductionMaterialConsumedIntegrationEventConverter.NewInventoryMovementRequested(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            idempotencyKey,
            request.RequestNo,
            request.WorkOrderId,
            request.SkuId,
            request.UomCode,
            "finished-goods",
            "receiving",
            request.ProducedLotNo,
            request.Quantity,
            occurredAtUtc,
            request.UnitCost);
    }
}

public sealed class MaterialIssueRequestedIntegrationEventConverter
    : IIntegrationEventConverter<MaterialIssueRequestedDomainEvent, InventoryMovementRequestedIntegrationEvent>
{
    public InventoryMovementRequestedIntegrationEvent Convert(MaterialIssueRequestedDomainEvent domainEvent)
    {
        var request = domainEvent.MaterialIssueRequest;
        var occurredAtUtc = request.ReceivedAtUtc ?? DateTimeOffset.UtcNow;
        // Use cumulative received quantity in the idempotency key so repeated partial receipts with
        // the same delta still produce distinct Inventory movements; movement quantity stays the delta.
        var idempotencyKey = EventIds.Idempotency(
            "material-issue",
            request.OrganizationId,
            request.EnvironmentId,
            request.RequestNo,
            request.MaterialLotId,
            request.ReceivedQuantity.ToString("0.######", CultureInfo.InvariantCulture));
        EventIds.ThrowIfUnsupportedUom(request.UomCode, request.RequestNo);
        return ProductionMaterialConsumedIntegrationEventConverter.NewInventoryMovementRequested(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            idempotencyKey,
            request.RequestNo,
            request.OperationTaskId,
            request.MaterialId,
            request.UomCode,
            "warehouse",
            "line-side",
            request.MaterialLotId,
            -Math.Abs(domainEvent.IssuedQuantity),
            occurredAtUtc);
    }
}

public sealed class MaterialLineSideReceiptConfirmedIntegrationEventConverter
    : IIntegrationEventConverter<MaterialLineSideReceiptConfirmedDomainEvent, InventoryMovementRequestedIntegrationEvent>
{
    public InventoryMovementRequestedIntegrationEvent Convert(MaterialLineSideReceiptConfirmedDomainEvent domainEvent)
    {
        var request = domainEvent.MaterialIssueRequest;
        var occurredAtUtc = request.ReceivedAtUtc ?? DateTimeOffset.UtcNow;
        // Keep this key in lockstep with the warehouse outbound leg: cumulative quantity identifies
        // the receipt step, while the posted quantity remains this confirmation's delta.
        var idempotencyKey = EventIds.Idempotency(
            "line-side-receipt",
            request.OrganizationId,
            request.EnvironmentId,
            request.RequestNo,
            request.MaterialLotId,
            request.ReceivedQuantity.ToString("0.######", CultureInfo.InvariantCulture));
        EventIds.ThrowIfUnsupportedUom(request.UomCode, request.RequestNo);
        return ProductionMaterialConsumedIntegrationEventConverter.NewInventoryMovementRequested(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            idempotencyKey,
            request.RequestNo,
            request.OperationTaskId,
            request.MaterialId,
            request.UomCode,
            "production",
            "line-side",
            request.MaterialLotId,
            Math.Abs(domainEvent.ReceivedQuantity),
            occurredAtUtc);
    }
}

public sealed class DefectRaisedIntegrationEventConverter
    : IIntegrationEventConverter<DefectRaisedDomainEvent, DefectRaisedIntegrationEvent>
{
    public DefectRaisedIntegrationEvent Convert(DefectRaisedDomainEvent domainEvent)
    {
        var defect = domainEvent.DefectRecord;
        var idempotencyKey = EventIds.Idempotency("defect-raised", defect.OrganizationId, defect.EnvironmentId, defect.DefectNo);
        return new DefectRaisedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            QualityIntegrationEventTypes.DefectRaised,
            QualityIntegrationEventVersions.V1,
            defect.RecordedAtUtc,
            QualityIntegrationEventSources.BusinessMes,
            idempotencyKey,
            defect.DefectNo,
            defect.OrganizationId,
            defect.EnvironmentId,
            "system:mes",
            idempotencyKey,
            new DefectRaisedPayload(
                defect.DefectNo,
                defect.WorkOrderId,
                defect.OperationTaskId,
                defect.DefectCode,
                defect.Quantity,
                defect.RecordedAtUtc));
    }
}

internal static class EventIds
{
    public static string Idempotency(params string?[] parts) =>
        $"mes:{string.Join(':', parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()))}";

    public static void ThrowIfUnsupportedUom(string uomCode, string sourceDocumentId)
    {
        if (string.Equals(uomCode, MaterialIssueRequest.UnspecifiedUomCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"MES material movement cannot emit Inventory request without UOM, SourceDocumentId = {sourceDocumentId}");
        }
    }
}
