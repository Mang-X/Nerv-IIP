using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", ConsumerName)]
public sealed class QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext(
    ApplicationDbContext dbContext,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<InspectionResultIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-mes.quality-inspection-result";

    private static readonly string[] SupportedEventTypes =
    [
        QualityIntegrationEventTypes.InspectionPassed,
        QualityIntegrationEventTypes.InspectionConditionalReleased,
        QualityIntegrationEventTypes.InspectionRejected,
    ];

    private readonly IntegrationEventConsumerGuard<InspectionResultIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            SupportedEventTypes,
            QualityIntegrationEventVersions.V1));

    public async Task HandleAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);
    }

    [CapSubscribe("Nerv.IIP.Contracts.Quality.InspectionResultIntegrationEvent", Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var payload = integrationEvent.Payload;
        if (!string.Equals(payload.SourceService, QualityIntegrationEventSources.BusinessMes, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!await MesProcessedIntegrationEventInbox.TryRecordAsync(dbContext, ConsumerName, integrationEvent, cancellationToken))
        {
            return;
        }

        var sourceDocumentId = payload.SourceDocumentId.Trim();
        var source = await ResolveMesSourceAsync(
            integrationEvent.OrganizationId,
            integrationEvent.EnvironmentId,
            sourceDocumentId,
            cancellationToken);
        if (source is null)
        {
            return;
        }

        var existing = await dbContext.QualityHoldContexts.SingleOrDefaultAsync(
            x => x.OrganizationId == integrationEvent.OrganizationId &&
                x.EnvironmentId == integrationEvent.EnvironmentId &&
                x.SourceService == payload.SourceService &&
                x.SourceDocumentId == sourceDocumentId,
            cancellationToken);
        if (existing is null)
        {
            dbContext.QualityHoldContexts.Add(QualityHoldContext.Capture(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                source.WorkOrderId,
                source.OperationTaskId,
                payload.SourceService,
                sourceDocumentId,
                payload.InspectionRecordId,
                payload.InspectionPlanId,
                payload.Result,
                integrationEvent.EventType,
                payload.DispositionReason,
                payload.RecordedAtUtc,
                integrationEvent.Actor));
            return;
        }

        existing.ApplyInspectionResult(
            payload.InspectionRecordId,
            payload.InspectionPlanId,
            payload.Result,
            integrationEvent.EventType,
            payload.DispositionReason,
            payload.RecordedAtUtc,
            integrationEvent.Actor);
    }

    private async Task<MesInspectionSource?> ResolveMesSourceAsync(
        string organizationId,
        string environmentId,
        string sourceDocumentId,
        CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.WorkOrderIdValue == sourceDocumentId)
            .Select(x => new MesInspectionSource(x.WorkOrderIdValue, null))
            .SingleOrDefaultAsync(cancellationToken);
        if (workOrder is not null)
        {
            return workOrder;
        }

        return await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId &&
                x.EnvironmentId == environmentId &&
                x.OperationTaskIdValue == sourceDocumentId)
            .Select(x => new MesInspectionSource(x.WorkOrderId, x.OperationTaskIdValue))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private sealed record MesInspectionSource(string WorkOrderId, string? OperationTaskId);
}
