using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.NcrReworkRequestedIntegrationEvent", ConsumerName)]
public sealed class NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder(
    ApplicationDbContext dbContext,
    MesCodingService codingService,
    IIntegrationEventDeadLetterStore deadLetterStore,
    IMesMaterialRequirementSnapshotProvider materialSnapshotProvider,
    IMesReworkWorkOrderScopeCoordinator scopeCoordinator)
    : IIntegrationEventHandler<NcrReworkRequestedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.quality-ncr-rework-requested";

    private readonly IntegrationEventConsumerGuard<NcrReworkRequestedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            QualityIntegrationEventTypes.NcrReworkRequested,
            QualityIntegrationEventVersions.V1));

    public Task HandleAsync(
        NcrReworkRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(NcrReworkRequestedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        NcrReworkRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task HandleValidEventAsync(
        NcrReworkRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        scopeCoordinator.ExecuteAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            integrationEvent.Payload.NcrId,
            token => HandleScopedAsync(integrationEvent, token),
            cancellationToken);

    private async Task HandleScopedAsync(
        NcrReworkRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var payload = integrationEvent.Payload;
        var existing = await dbContext.WorkOrders.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.SourceNcrId == payload.NcrId,
            cancellationToken);
        if (existing is not null)
        {
            if (!Matches(existing, payload))
            {
                await DeadLetterAsync(
                    integrationEvent,
                    "mes.ncrReworkRequested.payloadConflict",
                    $"NCR '{payload.NcrId}' already created rework work order '{existing.WorkOrderIdValue}' from a different payload.",
                    cancellationToken);
                return;
            }

            await MesProcessedIntegrationEventInbox.TryRecordAsync(
                dbContext,
                ConsumerName,
                integrationEvent,
                cancellationToken);
            return;
        }

        var defect = await dbContext.DefectRecords.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.DefectNo == payload.SourceDefectNo,
            cancellationToken);
        if (defect is null)
        {
            await DeadLetterAsync(
                integrationEvent,
                "mes.ncrReworkRequested.sourceDefectMissing",
                $"MES defect '{payload.SourceDefectNo}' was not found in the event scope.",
                cancellationToken);
            return;
        }

        var sourceWorkOrder = await dbContext.WorkOrders.SingleAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.WorkOrderIdValue == defect.WorkOrderId,
            cancellationToken);

        var sourceRouting = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.WorkOrderId == sourceWorkOrder.WorkOrderIdValue)
            .OrderBy(x => x.OperationSequence)
            .ThenBy(x => x.OperationTaskIdValue)
            .ToArrayAsync(cancellationToken);
        if (sourceRouting.Length == 0)
        {
            await DeadLetterAsync(
                integrationEvent,
                "mes.ncrReworkRequested.sourceRoutingMissing",
                $"MES source work order '{sourceWorkOrder.WorkOrderIdValue}' has no frozen operation routing.",
                cancellationToken);
            return;
        }

        var firstSourceOperationIndex = defect.OperationTaskId is null
            ? 0
            : Array.FindIndex(
                sourceRouting,
                x => string.Equals(x.OperationTaskIdValue, defect.OperationTaskId, StringComparison.Ordinal));
        if (firstSourceOperationIndex < 0)
        {
            await DeadLetterAsync(
                integrationEvent,
                "mes.ncrReworkRequested.sourceOperationMismatch",
                $"MES defect '{defect.DefectNo}' references operation '{defect.OperationTaskId}' outside source work order '{sourceWorkOrder.WorkOrderIdValue}'.",
                cancellationToken);
            return;
        }

        if (!string.Equals(sourceWorkOrder.SkuId, payload.SkuCode, StringComparison.Ordinal))
        {
            await DeadLetterAsync(
                integrationEvent,
                "mes.ncrReworkRequested.skuMismatch",
                $"NCR SKU '{payload.SkuCode}' does not match source work order SKU '{sourceWorkOrder.SkuId}'.",
                cancellationToken);
            return;
        }

        if (defect.Quantity != payload.Quantity)
        {
            await DeadLetterAsync(
                integrationEvent,
                "mes.ncrReworkRequested.quantityMismatch",
                $"NCR quantity '{payload.Quantity}' does not match MES defect quantity '{defect.Quantity}'.",
                cancellationToken);
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(
                dbContext,
                ConsumerName,
                integrationEvent,
                cancellationToken))
        {
            return;
        }

        var allocation = await codingService.AllocateWorkOrderIdAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            null,
            integrationEvent.IdempotencyKey,
            MesCodingService.Fingerprint(
                payload.NcrId,
                payload.NcrCode,
                payload.SourceDefectNo,
                payload.SkuCode,
                payload.Quantity,
                payload.LotNo,
                payload.SerialNo,
                sourceWorkOrder.WorkOrderIdValue,
                defect.OperationTaskId,
                payload.RequestedAtUtc),
            cancellationToken);
        var reworkWorkOrder = WorkOrder.CreateRework(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            allocation.Code,
            sourceWorkOrder.SkuId,
            sourceWorkOrder.ProductionVersionId,
            sourceWorkOrder.UomCode,
            payload.Quantity,
            sourceWorkOrder.Priority,
            sourceWorkOrder.DueUtc,
            sourceWorkOrder.WorkOrderIdValue,
            defect.OperationTaskId,
            defect.DefectNo,
            payload.NcrId,
            payload.NcrCode,
            payload.LotNo,
            payload.SerialNo,
            payload.RequestedAtUtc,
            integrationEvent.CorrelationId,
            integrationEvent.EventId);
        var reworkRouting = sourceRouting[firstSourceOperationIndex..]
            .Select((source, index) => new RoutingStepSnapshot(
                $"OPT-{index:D4}-{Guid.CreateVersion7():N}",
                source.OperationSequence,
                source.WorkCenterId,
                source.AlternativeWorkCenterIdList,
                source.Duration,
                source.RequiresQualityInspection,
                source.OperationCode))
            .ToArray();
        var reworkOperationTasks = reworkWorkOrder.Release(payload.RequestedAtUtc, reworkRouting);
        dbContext.WorkOrders.Add(reworkWorkOrder);
        dbContext.OperationTasks.AddRange(reworkOperationTasks);
        await MaterialReadinessGuards.EnsureRequirementSnapshotsAsync(
            dbContext,
            materialSnapshotProvider,
            reworkWorkOrder,
            payload.RequestedAtUtc,
            cancellationToken);
    }

    private static bool Matches(WorkOrder workOrder, NcrReworkRequestedPayload payload) =>
        workOrder.WorkOrderType == WorkOrder.ReworkType &&
        workOrder.SourceNcrCode == payload.NcrCode &&
        workOrder.SourceDefectNo == payload.SourceDefectNo &&
        workOrder.SkuId == payload.SkuCode &&
        workOrder.Quantity == payload.Quantity &&
        workOrder.SourceLotNo == payload.LotNo &&
        workOrder.SourceSerialNo == payload.SerialNo &&
        workOrder.SourceReworkRequestedAtUtc == payload.RequestedAtUtc;

    private Task DeadLetterAsync(
        NcrReworkRequestedIntegrationEvent integrationEvent,
        string failureCode,
        string failureMessage,
        CancellationToken cancellationToken) =>
        deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                ConsumerName,
                integrationEvent,
                failureCode,
                failureMessage),
            cancellationToken);
}
