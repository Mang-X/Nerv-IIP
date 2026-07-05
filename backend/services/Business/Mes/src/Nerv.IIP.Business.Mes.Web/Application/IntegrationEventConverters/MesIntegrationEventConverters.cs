using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
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
            -consumption.ConsumedQuantity,
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
        return ProductionMaterialConsumedIntegrationEventConverter.NewInventoryMovementRequested(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            domainEvent.IdempotencyKey,
            request.RequestNo,
            request.WorkOrderId,
            request.SkuId,
            request.UomCode,
            "finished-goods",
            "receiving",
            request.ProducedLotNo,
            domainEvent.Quantity,
            occurredAtUtc,
            request.UnitCost);
    }
}

public sealed class FinishedGoodsReceiptRequestedForQualityIntegrationEventConverter
    : IIntegrationEventConverter<FinishedGoodsReceiptRequestedDomainEvent, FinishedGoodsReceiptRequestedIntegrationEvent>
{
    public FinishedGoodsReceiptRequestedIntegrationEvent Convert(FinishedGoodsReceiptRequestedDomainEvent domainEvent)
    {
        var request = domainEvent.FinishedGoodsReceiptRequest;
        var occurredAtUtc = DateTimeOffset.UtcNow;
        return new FinishedGoodsReceiptRequestedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            MesIntegrationEventTypes.FinishedGoodsReceiptRequested,
            MesIntegrationEventVersions.V1,
            occurredAtUtc,
            MesIntegrationEventSources.BusinessMes,
            domainEvent.IdempotencyKey,
            request.RequestNo,
            request.OrganizationId,
            request.EnvironmentId,
            "system:mes",
            EventIds.Idempotency("finished-goods-receipt-quality", request.OrganizationId, request.EnvironmentId, request.RequestNo),
            new FinishedGoodsReceiptRequestedPayload(
                request.RequestNo,
                request.WorkOrderId,
                request.SkuId,
                domainEvent.Quantity,
                request.UomCode,
                request.ProducedLotNo,
                request.SerialNo,
                occurredAtUtc));
    }
}

public sealed class OperationTaskCompletedIntegrationEventConverter
    : IIntegrationEventConverter<OperationTaskCompletedDomainEvent, OperationTaskCompletedIntegrationEvent>
{
    public OperationTaskCompletedIntegrationEvent Convert(OperationTaskCompletedDomainEvent domainEvent)
    {
        var task = domainEvent.OperationTask;
        var completedAtUtc = task.ExistingEndUtc ?? DateTimeOffset.UtcNow;
        var idempotencyKey = EventIds.Idempotency(
            "operation-task-completed",
            task.OrganizationId,
            task.EnvironmentId,
            task.OperationTaskId);
        return new OperationTaskCompletedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            MesIntegrationEventTypes.OperationTaskCompleted,
            MesIntegrationEventVersions.V1,
            completedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            idempotencyKey,
            task.WorkOrderId,
            task.OrganizationId,
            task.EnvironmentId,
            "system:mes",
            idempotencyKey,
            new OperationTaskCompletedPayload(
                task.WorkOrderId,
                task.OperationTaskId,
                task.SkuCode,
                task.OperationSequence,
                task.WorkCenterId,
                task.PlannedQuantity,
                task.RequiresQualityInspection,
                completedAtUtc));
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

public sealed class MaterialLineSideReturnRequestedIntegrationEventConverter
    : IIntegrationEventConverter<MaterialLineSideReturnRequestedDomainEvent, InventoryMovementRequestedIntegrationEvent>
{
    public InventoryMovementRequestedIntegrationEvent Convert(MaterialLineSideReturnRequestedDomainEvent domainEvent)
    {
        var request = domainEvent.MaterialIssueRequest;
        var occurredAtUtc = domainEvent.ReturnedAtUtc;
        var idempotencyKey = EventIds.Idempotency(
            "line-side-return-outbound",
            request.OrganizationId,
            request.EnvironmentId,
            request.RequestNo,
            domainEvent.MaterialLotId,
            domainEvent.ReturnedQuantity.ToString("0.######", CultureInfo.InvariantCulture),
            occurredAtUtc.ToString("O", CultureInfo.InvariantCulture));
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
            domainEvent.MaterialLotId,
            -Math.Abs(domainEvent.ReturnedQuantity),
            occurredAtUtc);
    }
}

public sealed class MaterialReturnedToWarehouseIntegrationEventConverter
    : IIntegrationEventConverter<MaterialReturnedToWarehouseDomainEvent, InventoryMovementRequestedIntegrationEvent>
{
    public InventoryMovementRequestedIntegrationEvent Convert(MaterialReturnedToWarehouseDomainEvent domainEvent)
    {
        var request = domainEvent.MaterialIssueRequest;
        var occurredAtUtc = domainEvent.ReturnedAtUtc;
        var idempotencyKey = EventIds.Idempotency(
            "line-side-return-warehouse-inbound",
            request.OrganizationId,
            request.EnvironmentId,
            request.RequestNo,
            domainEvent.MaterialLotId,
            domainEvent.ReturnedQuantity.ToString("0.######", CultureInfo.InvariantCulture),
            occurredAtUtc.ToString("O", CultureInfo.InvariantCulture));
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
            domainEvent.MaterialLotId,
            Math.Abs(domainEvent.ReturnedQuantity),
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

public sealed class WorkOrderReleasedIntegrationEventConverter
    : IIntegrationEventConverter<WorkOrderReleasedDomainEvent, WorkOrderReleasedIntegrationEvent>
{
    public WorkOrderReleasedIntegrationEvent Convert(WorkOrderReleasedDomainEvent domainEvent)
    {
        var workOrder = domainEvent.WorkOrder;
        var idempotencyKey = EventIds.Idempotency(
            "work-order-released",
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.WorkOrderId);
        var occurredAtUtc = DateTimeOffset.UtcNow;

        return new WorkOrderReleasedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            MesIntegrationEventTypes.WorkOrderReleased,
            MesIntegrationEventVersions.V1,
            occurredAtUtc,
            MesIntegrationEventSources.BusinessMes,
            idempotencyKey,
            workOrder.WorkOrderId,
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            "system:mes",
            idempotencyKey,
            new WorkOrderReleasedPayload(
                workOrder.WorkOrderId,
                workOrder.SkuId,
                workOrder.Quantity,
                occurredAtUtc,
                domainEvent.OperationTasks
                    .OrderBy(x => x.OperationSequence)
                    .Select(x => new ReleasedOperationPayload(
                        x.OperationTaskId,
                        x.OperationSequence,
                        x.WorkCenterId))
                    .ToArray()));
    }
}

public sealed class WorkOrderCompletedIntegrationEventConverter
    : IIntegrationEventConverter<WorkOrderCompletedDomainEvent, WorkOrderCompletedIntegrationEvent>
{
    public WorkOrderCompletedIntegrationEvent Convert(WorkOrderCompletedDomainEvent domainEvent)
    {
        var workOrder = domainEvent.WorkOrder;
        var idempotencyKey = EventIds.Idempotency(
            "work-order-completed",
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.WorkOrderId);
        return new WorkOrderCompletedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            MesIntegrationEventTypes.WorkOrderCompleted,
            MesIntegrationEventVersions.V1,
            domainEvent.CompletedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            idempotencyKey,
            workOrder.WorkOrderId,
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            "system:mes",
            idempotencyKey,
            new WorkOrderCompletedPayload(
                workOrder.WorkOrderId,
                workOrder.SkuId,
                workOrder.Quantity,
                workOrder.CompletedQuantity,
                workOrder.ScrapQuantity,
                domainEvent.CompletedAtUtc));
    }
}

public sealed class WorkOrderClosedIntegrationEventConverter
    : IIntegrationEventConverter<WorkOrderClosedDomainEvent, WorkOrderClosedIntegrationEvent>
{
    public WorkOrderClosedIntegrationEvent Convert(WorkOrderClosedDomainEvent domainEvent)
    {
        var workOrder = domainEvent.WorkOrder;
        var idempotencyKey = EventIds.Idempotency(
            "work-order-closed",
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.WorkOrderId);
        return new WorkOrderClosedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            MesIntegrationEventTypes.WorkOrderClosed,
            MesIntegrationEventVersions.V1,
            domainEvent.ClosedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            idempotencyKey,
            workOrder.WorkOrderId,
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            "system:mes",
            idempotencyKey,
            new WorkOrderClosedPayload(
                workOrder.WorkOrderId,
                workOrder.SkuId,
                workOrder.Quantity,
                workOrder.CompletedQuantity,
                workOrder.ScrapQuantity,
                domainEvent.ClosedAtUtc));
    }
}

public sealed class WorkOrderCancelledIntegrationEventConverter
    : IIntegrationEventConverter<WorkOrderCancelledDomainEvent, InventoryReservationReleaseRequestedIntegrationEvent>
{
    public InventoryReservationReleaseRequestedIntegrationEvent Convert(WorkOrderCancelledDomainEvent domainEvent)
    {
        var workOrder = domainEvent.WorkOrder;
        var idempotencyKey = EventIds.Idempotency(
            "work-order-cancelled-reservation-release",
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            workOrder.WorkOrderId);
        return new InventoryReservationReleaseRequestedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            InventoryIntegrationEventTypes.InventoryReservationReleaseRequested,
            InventoryIntegrationEventVersions.V1,
            domainEvent.CancelledAtUtc,
            InventoryIntegrationEventSources.BusinessMes,
            idempotencyKey,
            workOrder.WorkOrderId,
            workOrder.OrganizationId,
            workOrder.EnvironmentId,
            "system:mes",
            idempotencyKey,
            new InventoryReservationReleaseRequestedPayload(
                InventoryIntegrationEventSources.BusinessMes,
                workOrder.WorkOrderId,
                domainEvent.MaterialIssueRequestNos,
                domainEvent.Reason,
                domainEvent.CancelledAtUtc));
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
