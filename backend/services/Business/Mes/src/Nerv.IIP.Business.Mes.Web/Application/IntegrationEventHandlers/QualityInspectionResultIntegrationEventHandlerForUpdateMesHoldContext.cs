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

    // Quality 发布 MES 归属检验的 sourceService 词汇为 "mes"（工单级）/"mes-operation"（工序任务级），与 MES 内部及
    // 契约 QualityIntegrationEventSources.BusinessMes（"business-mes"）不一致（历史跨服务词汇分歧）。此处入站归一化：
    // 接受 Quality 的 MES 词汇，统一以 business-mes 存储保留上下文/时间线，使 MES 查询与前端（均用 business-mes）一致。
    private static readonly HashSet<string> MesSourceServiceTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "mes",
        "mes-operation",
        QualityIntegrationEventSources.BusinessMes,
    };

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

    [CapSubscribe(nameof(InspectionResultIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        return HandleAsync(integrationEvent, cancellationToken);
    }

    private async Task HandleValidEventAsync(InspectionResultIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var payload = integrationEvent.Payload;
        if (!MesSourceServiceTokens.Contains(payload.SourceService?.Trim() ?? string.Empty))
        {
            return;
        }

        // 统一以 MES 契约词汇存储，使保留上下文/时间线与 MES 查询、前端（均用 business-mes）一致。
        var sourceService = QualityIntegrationEventSources.BusinessMes;

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
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var existing = await dbContext.QualityHoldContexts.SingleOrDefaultAsync(
                x => x.OrganizationId == integrationEvent.OrganizationId &&
                    x.EnvironmentId == integrationEvent.EnvironmentId &&
                    x.SourceService == sourceService &&
                    x.SourceDocumentId == sourceDocumentId,
                cancellationToken);
            if (existing is null)
            {
                var hold = QualityHoldContext.Capture(
                    integrationEvent.OrganizationId,
                    integrationEvent.EnvironmentId,
                    source.WorkOrderId,
                    source.OperationTaskId,
                    sourceService,
                    sourceDocumentId,
                    payload.InspectionRecordId,
                    payload.InspectionPlanId,
                    payload.Result,
                    integrationEvent.EventType,
                    payload.DispositionReason,
                    payload.RecordedAtUtc,
                    integrationEvent.Actor);
                dbContext.QualityHoldContexts.Add(hold);
                if (hold.Active)
                {
                    AddTransition(integrationEvent, sourceService, "hold-applied", payload.InspectionRecordId);
                }
            }
            else
            {
                var wasActive = existing.Active;
                if (existing.ApplyInspectionResult(
                    payload.InspectionRecordId,
                    payload.InspectionPlanId,
                    payload.Result,
                    integrationEvent.EventType,
                    payload.DispositionReason,
                    payload.RecordedAtUtc,
                    integrationEvent.Actor))
                {
                    AddTransition(
                        integrationEvent,
                        sourceService,
                        wasActive ? "inspection-released" : "hold-applied",
                        wasActive ? existing.HeldInspectionRecordId! : payload.InspectionRecordId);
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "quality-inspection-result-divergence",
                    exception.Message),
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void AddTransition(InspectionResultIntegrationEvent integrationEvent, string sourceService, string eventKind, string holdCycleId)
    {
        var payload = integrationEvent.Payload;
        dbContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            integrationEvent.OrganizationId, integrationEvent.EnvironmentId, sourceService,
            payload.SourceDocumentId.Trim(), holdCycleId, integrationEvent.CorrelationId, eventKind,
            integrationEvent.Actor, payload.RecordedAtUtc, payload.DispositionReason, payload.InspectionRecordId,
            payload.InspectionPlanId, "automatic", integrationEvent.IdempotencyKey));
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
