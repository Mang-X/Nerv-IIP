using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
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

    // Quality 发布 MES 归属检验的 sourceService 词汇为 "mes"（工单级）/"mes-operation"（工序任务级），
    // 与契约 QualityIntegrationEventSources.BusinessMes（"business-mes"）不同。
    // 按 #1370 ③ 批次 D 裁决：这两个是同一「事件信封来源」面上的**历史别名**（非两个面），
    // 权威取值是 business-mes，别名仅在入站侧接受、不外扩；
    // 另一侧的 DemandPlanningDownstreamReferences.BusinessMes（"BusinessMes"）属 DP 接受面，是另一个面，与此无关。
    // 此处入站归一化：接受 Quality 的 MES 别名，统一以 business-mes 存储保留上下文/时间线，
    // 使 MES 查询与前端（均用 business-mes）一致。
    private static readonly HashSet<string> MesSourceServiceTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        QualityInspectionSourceServices.Mes,
        QualityInspectionSourceServices.MesOperation,
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

        // #3315：这一段的取值是 Quality 的来源单据身份，首件与周期检发过来的是**复合串**（见 :191 的说明），
        // 长度上界由 Quality 的产出列宽决定，不是 MES 自己的工单/工序 id 宽度。它被逐字写进
        // quality_hold_contexts.source_document_id 与 quality_hold_transitions.source_document_id 两列，
        // 超宽时 SaveChangesAsync 抛 DbUpdateException(22001)——下面那两个 catch 只接 InvalidOperationException /
        // ArgumentException，接不住它，异常会逃逸出 HandleAsync 变成 poison message（#877），整条消费链卡死。
        // 这里就地判长并走死信：既不截断（截断会把两道工序的首件结论折叠成同一条保留上下文），
        // 也不抛出（抛出就是回到 poison message）。列宽已加宽到与 Quality 产出列一致，
        // 因此今天合法的复合身份走不到这条分支；它看守的是「将来任一侧列宽/复合构成再变」。
        if (MesQualityHoldSourceDocumentIdPolicy.ExceedsColumn(sourceDocumentId))
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    MesQualityHoldSourceDocumentIdPolicy.OverlongFailureCode,
                    MesQualityHoldSourceDocumentIdPolicy.OverlongFailureMessage(sourceDocumentId)),
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        MesInspectionSource? source;
        try
        {
            source = await ResolveMesSourceAsync(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                sourceDocumentId,
                payload.WorkOrderId,
                payload.OperationTaskId,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "quality-inspection-result-divergence",
                    exception.Message),
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (source is null)
        {
            await deadLetterStore.AddAsync(
                IntegrationEventDeadLetterMessage.Create(
                    ConsumerName,
                    integrationEvent,
                    "unknown-source-document",
                    $"MES source document '{sourceDocumentId}' was not found in the event scope."),
                cancellationToken);
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
                // Concurrent first deliveries may race on ux_quality_hold_contexts_scope_source.
                // The loser is retried by CAP and then converges through the inbox row committed by the winner.
                dbContext.QualityHoldContexts.Add(hold);
                if (hold.Active)
                {
                    AddTransition(integrationEvent, sourceService, sourceDocumentId, "hold-applied", payload.InspectionRecordId);
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
                        sourceDocumentId,
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

    /// <summary>
    /// 时间线与保留上下文必须写**同一个**来源身份取值。此前这里第二次从
    /// <c>payload.SourceDocumentId</c> 派生（再 Trim 一次），与调用方已经算好的那一份是两地等价派生：
    /// 改一处就静默漂移，而且长度守卫（#3315）也只看得住其中一份。改为由调用方传入。
    /// </summary>
    private void AddTransition(
        InspectionResultIntegrationEvent integrationEvent,
        string sourceService,
        string sourceDocumentId,
        string eventKind,
        string holdCycleId)
    {
        var payload = integrationEvent.Payload;
        dbContext.QualityHoldTransitions.Add(QualityHoldTransition.Record(
            integrationEvent.OrganizationId, integrationEvent.EnvironmentId, sourceService,
            sourceDocumentId, holdCycleId, integrationEvent.CorrelationId, eventKind,
            integrationEvent.Actor, payload.RecordedAtUtc, payload.DispositionReason, payload.InspectionRecordId,
            payload.InspectionPlanId, "automatic", integrationEvent.IdempotencyKey));
    }

    /// <summary>
    /// 定位检验对象。首件与周期检的 <paramref name="sourceDocumentId"/> 是 Quality 内部的复合串，
    /// 按它去查工单表与工序任务表两边都查不到，结果是整条首件／周期检结论进死信
    /// （<c>unknown-source-document</c>，#3177）。生产者现在把工单／工序结构化发布在 payload 上，
    /// 优先按结构化身份定位；仍然回库校验存在性，不拿事件自称的身份当事实。
    /// </summary>
    private async Task<MesInspectionSource?> ResolveMesSourceAsync(
        string organizationId,
        string environmentId,
        string sourceDocumentId,
        string? payloadWorkOrderId,
        string? payloadOperationTaskId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(payloadOperationTaskId))
        {
            var operationScoped = await dbContext.OperationTasks
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.OperationTaskIdValue == payloadOperationTaskId)
                .Select(x => new MesInspectionSource(x.WorkOrderId, x.OperationTaskIdValue))
                .SingleOrDefaultAsync(cancellationToken);
            if (operationScoped is not null)
            {
                return operationScoped;
            }
        }

        if (!string.IsNullOrWhiteSpace(payloadWorkOrderId))
        {
            var workOrderScoped = await dbContext.WorkOrders
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId &&
                    x.WorkOrderIdValue == payloadWorkOrderId)
                .Select(x => new MesInspectionSource(x.WorkOrderIdValue, (string?)null))
                .SingleOrDefaultAsync(cancellationToken);
            if (workOrderScoped is not null)
            {
                return workOrderScoped;
            }
        }

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
