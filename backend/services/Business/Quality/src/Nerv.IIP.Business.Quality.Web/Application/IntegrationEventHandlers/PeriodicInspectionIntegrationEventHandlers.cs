using DotNetCore.CAP;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.IntegrationEvents;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

[IntegrationEventConsumer(nameof(WorkOrderReleasedIntegrationEvent), ConsumerName)]
public sealed class WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<WorkOrderReleasedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-work-order-released-periodic-inspection";

    private readonly IntegrationEventConsumerGuard<WorkOrderReleasedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.WorkOrderReleased,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(WorkOrderReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(WorkOrderReleasedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(WorkOrderReleasedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task HandleValidEventAsync(
        WorkOrderReleasedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        PeriodicInspectionReleaseProjection.ApplyAsync(
            dbContext,
            scopeCoordinator,
            deadLetterStore,
            integrationEvent,
            integrationEvent.Payload,
            ConsumerName,
            ReleaseFactAuthority.Authoritative,
            cancellationToken);
}

/// <summary>
/// 发布事实的**权威性**：两条入口的全部行为差异都由它派生，不各自带开关。
/// </summary>
internal enum ReleaseFactAuthority
{
    /// <summary>
    /// MES 直投的 <c>mes.WorkOrderReleased</c>：发布时刻是当初那一次发布事件带的值，权威。
    /// 因此同一工序收到第二份**内容不同**的发布事实是真实异常，必须由 <c>ApplyRelease</c> 判为冲突进死信，
    /// 不得跳过——跳过会把这个信号吞掉。
    /// </summary>
    Authoritative,

    /// <summary>
    /// #3000 回填的 <c>mes.WorkOrderReleaseProjectionBackfilled</c>：发布时刻是从 MES 存量数据重建的**下界**，
    /// 不等于当初那一次发布事件带的时刻。由此派生两条行为：
    /// ① 已有发布事实的工序只跳过、不覆盖（拿重建下界去比对必然判冲突），这同时是「重复执行回填不改变投影内容」的落点；
    /// ② 补投之前累计的产量与流逝的时间不追认周期巡检窗口（见
    /// <c>PeriodicInspectionOperation.SkipPeriodicWindowsAccruedBefore</c>）。
    /// </summary>
    ReconstructedLowerBound,
}

/// <summary>
/// 工单发布事实落成 <c>PeriodicInspectionOperation</c> 投影的**唯一**写法：直投（<c>mes.WorkOrderReleased</c>）
/// 与存量回填（<c>mes.WorkOrderReleaseProjectionBackfilled</c>，#3000）共用本方法，两条入口不各写一份。
/// </summary>
internal static class PeriodicInspectionReleaseProjection
{
    public static async Task ApplyAsync(
        ApplicationDbContext dbContext,
        IPeriodicInspectionOperationScopeCoordinator scopeCoordinator,
        IIntegrationEventDeadLetterStore deadLetterStore,
        IIntegrationEventEnvelope integrationEvent,
        WorkOrderReleasedPayload payload,
        string consumerName,
        ReleaseFactAuthority authority,
        CancellationToken cancellationToken)
    {
        try
        {
            var operations = ValidateReleasedOperations(payload);
            var workCenterIds = operations.Select(x => x.WorkCenterId.Trim()).Distinct(StringComparer.Ordinal).ToArray();
            var plans = await dbContext.InspectionPlans
                .AsNoTracking()
                .Where(plan =>
                    plan.OrganizationId == integrationEvent.OrganizationId
                    && plan.EnvironmentId == integrationEvent.EnvironmentId
                    && plan.Status == "active"
                    && plan.Category == "operation"
                    && plan.SkuCode == payload.SkuCode.Trim()
                    && plan.WorkCenterId != null
                    && workCenterIds.Contains(plan.WorkCenterId)
                    && (plan.TimeIntervalHours != null || plan.QuantityInterval != null))
                .ToArrayAsync(cancellationToken);

            await scopeCoordinator.ExecuteAsync(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkOrderId,
                operations.Select(x => x.OperationId).ToArray(),
                async ct =>
                {
                    if (!await QualityProcessedIntegrationEventInbox.TryRecordAsync(
                            dbContext,
                            consumerName,
                            integrationEvent,
                            ct))
                    {
                        return;
                    }

                    foreach (var operationPayload in operations)
                    {
                        var operation = await PeriodicInspectionOperationEventProcessing.LoadOrCreateAsync(
                            dbContext,
                            integrationEvent.OrganizationId,
                            integrationEvent.EnvironmentId,
                            payload.WorkOrderId,
                            operationPayload.OperationId,
                            ct);
                        if (authority == ReleaseFactAuthority.ReconstructedLowerBound
                            && operation.ReleasedAtUtc.HasValue)
                        {
                            continue;
                        }

                        // 重建值先与既有权威事实对齐：不一致时以权威事实为准，被顶掉的属性留痕。
                        var facts = authority == ReleaseFactAuthority.ReconstructedLowerBound
                            ? operation.ResolveReconstructedReleaseFacts(
                                payload.SkuCode,
                                operationPayload.OperationSequence,
                                operationPayload.WorkCenterId)
                            : new PeriodicInspectionReleaseFacts(
                                payload.SkuCode.Trim(),
                                operationPayload.OperationSequence,
                                operationPayload.WorkCenterId.Trim(),
                                []);

                        // 巡检档按**校正后**的 SKU 与工作中心筛。档是按载荷 SKU 查出来的，
                        // 因此让位到别的 SKU 时这里筛不出档、该工序不建周期运行上下文——
                        // 拿载荷 SKU 的档去配一条声明着另一个 SKU 的上下文才是真错。
                        var snapshots = plans
                            .Where(plan => plan.SkuCode == facts.SkuCode && plan.WorkCenterId == facts.WorkCenterId)
                            .OrderBy(plan => plan.PlanCode, StringComparer.Ordinal)
                            .Select(PeriodicInspectionPlanSnapshot.From)
                            .ToArray();

                        if (authority == ReleaseFactAuthority.Authoritative)
                        {
                            // 直投：冲突是真实异常，照旧整封进死信，语义不变。
                            operation.ApplyRelease(
                                facts.SkuCode,
                                facts.OperationSequence,
                                facts.WorkCenterId,
                                payload.ReleasedAtUtc.UtcDateTime,
                                snapshots);
                            PeriodicInspectionQuantityTaskGeneration.AddDueTasks(
                                dbContext,
                                operation.RuntimeContexts,
                                integrationEvent.OccurredAtUtc);
                            continue;
                        }

                        try
                        {
                            operation.ApplyRelease(
                                facts.SkuCode,
                                facts.OperationSequence,
                                facts.WorkCenterId,
                                payload.ReleasedAtUtc.UtcDateTime,
                                snapshots);
                            operation.SkipPeriodicWindowsAccruedBefore(
                                integrationEvent.OccurredAtUtc.UtcDateTime);
                            PeriodicInspectionQuantityTaskGeneration.AddDueTasks(
                                dbContext,
                                operation.RuntimeContexts,
                                integrationEvent.OccurredAtUtc);
                        }
                        catch (Exception exception)
                            when (PeriodicInspectionOperationEventProcessing.IsInvalidBusinessFact(exception))
                        {
                            // 失败粒度是工序，不是整封事件：一道工序补不上，不能让同工单其余工序
                            // 一起失去补投——那等于让整张工单继续 not-synchronized 被门禁永久拒。
                            await PeriodicInspectionOperationEventProcessing.RecordBackfillNoticeAsync(
                                deadLetterStore,
                                consumerName,
                                integrationEvent,
                                "backfill-operation-rejected",
                                $"Operation '{operationPayload.OperationId}' of work order '{payload.WorkOrderId}' "
                                + $"could not take the reconstructed release facts: {exception.Message}",
                                ct);
                            continue;
                        }

                        if (facts.Substitutions.Count > 0)
                        {
                            await PeriodicInspectionOperationEventProcessing.RecordBackfillNoticeAsync(
                                deadLetterStore,
                                consumerName,
                                integrationEvent,
                                "backfill-release-fact-substituted",
                                $"Operation '{operationPayload.OperationId}' of work order '{payload.WorkOrderId}' "
                                + "was backfilled with the existing authoritative completion facts: "
                                + string.Join(
                                    "; ",
                                    facts.Substitutions.Select(x =>
                                        $"{x.Attribute} reconstructed='{x.ReconstructedValue}' authoritative='{x.AuthoritativeValue}'")),
                                ct);
                        }
                    }
                },
                cancellationToken);
        }
        catch (Exception exception) when (PeriodicInspectionOperationEventProcessing.IsInvalidBusinessFact(exception))
        {
            await PeriodicInspectionOperationEventProcessing.DeadLetterAsync(
                dbContext,
                deadLetterStore,
                consumerName,
                integrationEvent,
                exception,
                cancellationToken);
        }
    }

    private static ReleasedOperationPayload[] ValidateReleasedOperations(WorkOrderReleasedPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.WorkOrderId)
            || string.IsNullOrWhiteSpace(payload.SkuCode)
            || payload.ReleasedAtUtc == default
            || payload.Operations is null
            || payload.Operations.Count == 0)
        {
            throw new ArgumentException("Work-order release payload requires work order, SKU, release time and operations.");
        }

        var operations = payload.Operations.ToArray();
        if (operations.Any(operation =>
                string.IsNullOrWhiteSpace(operation.OperationId)
                || operation.OperationSequence <= 0
                || string.IsNullOrWhiteSpace(operation.WorkCenterId)))
        {
            throw new ArgumentException("Released operations require operation id, positive sequence and work center.");
        }

        var duplicate = operations
            .GroupBy(operation => operation.OperationId.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Work-order release contains duplicate operation '{duplicate.Key}'.");
        }

        return operations.OrderBy(operation => operation.OperationId, StringComparer.Ordinal).ToArray();
    }
}

/// <summary>
/// 存量在制工单的发布投影回填（#3000）。这批工单在 Quality 订阅 <c>mes.WorkOrderReleased</c> 之前就已发布，
/// 投影里没有它们的行，首件确认读面因此恒回 <c>not-synchronized</c>、#2780 的报工门禁会持续拒绝，且不靠报工自愈。
/// 补投由 MES 的内部回填端点一次性发出，本消费者只把发布事实补进空缺的工序行。
/// </summary>
[IntegrationEventConsumer(nameof(WorkOrderReleaseProjectionBackfilledIntegrationEvent), ConsumerName)]
public sealed class WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<WorkOrderReleaseProjectionBackfilledIntegrationEvent>, ICapSubscribe
{
    /// <summary>
    /// 与直投消费组分开：两者的 inbox 记录、死信归属和重放语义都不同，混在一个组里，
    /// 回填就会被直投那次的 inbox 记录挡掉或反过来污染它。
    /// </summary>
    public const string ConsumerName = "business-quality.mes-work-order-release-projection-backfill";

    private readonly IntegrationEventConsumerGuard<WorkOrderReleaseProjectionBackfilledIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(
        WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(WorkOrderReleaseProjectionBackfilledIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(
        WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private Task HandleValidEventAsync(
        WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        PeriodicInspectionReleaseProjection.ApplyAsync(
            dbContext,
            scopeCoordinator,
            deadLetterStore,
            integrationEvent,
            integrationEvent.Payload,
            ConsumerName,
            ReleaseFactAuthority.ReconstructedLowerBound,
            cancellationToken);
}

[IntegrationEventConsumer(nameof(ProductionReportRecordedIntegrationEvent), ConsumerName)]
public sealed class ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<ProductionReportRecordedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-production-report-periodic-inspection";

    private readonly IntegrationEventConsumerGuard<ProductionReportRecordedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.ProductionReportRecorded,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(ProductionReportRecordedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(ProductionReportRecordedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        ProductionReportRecordedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = integrationEvent.Payload;
            if (string.IsNullOrWhiteSpace(payload.WorkOrderId)
                || string.IsNullOrWhiteSpace(payload.OperationTaskId)
                || string.IsNullOrWhiteSpace(payload.ReportNo)
                || string.IsNullOrWhiteSpace(payload.WorkCenterId)
                || string.IsNullOrWhiteSpace(payload.UomCode)
                || payload.ReportedAtUtc == default)
            {
                throw new ArgumentException("Production report payload requires report, work order, operation, work center, UOM and report time.");
            }

            await scopeCoordinator.ExecuteAsync(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkOrderId,
                [payload.OperationTaskId],
                async ct =>
                {
                    if (!await QualityProcessedIntegrationEventInbox.TryRecordAsync(
                            dbContext,
                            ConsumerName,
                            integrationEvent,
                            ct))
                    {
                        return;
                    }

                    var operation = await PeriodicInspectionOperationEventProcessing.LoadOrCreateAsync(
                        dbContext,
                        integrationEvent.OrganizationId,
                        integrationEvent.EnvironmentId,
                        payload.WorkOrderId,
                        payload.OperationTaskId,
                        ct);
                    operation.RecordProductionReport(
                        payload.ReportNo,
                        payload.WorkCenterId,
                        payload.GoodQuantity,
                        payload.UomCode,
                        payload.ReportedAtUtc.UtcDateTime,
                        payload.IsReversal,
                        payload.ReversedReportNo);
                    PeriodicInspectionQuantityTaskGeneration.AddDueTasks(
                        dbContext,
                        operation.RuntimeContexts,
                        integrationEvent.OccurredAtUtc);
                },
                cancellationToken);
        }
        catch (Exception exception) when (PeriodicInspectionOperationEventProcessing.IsInvalidBusinessFact(exception))
        {
            await PeriodicInspectionOperationEventProcessing.DeadLetterAsync(
                dbContext,
                deadLetterStore,
                ConsumerName,
                integrationEvent,
                exception,
                cancellationToken);
        }
    }
}

[IntegrationEventConsumer(nameof(MesOperationTaskCompletedIntegrationEvent), ConsumerName)]
public sealed class MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator,
    IIntegrationEventDeadLetterStore deadLetterStore)
    : IIntegrationEventHandler<MesOperationTaskCompletedIntegrationEvent>, ICapSubscribe
{
    public const string ConsumerName = "business-quality.mes-operation-completed-periodic-inspection";

    private readonly IntegrationEventConsumerGuard<MesOperationTaskCompletedIntegrationEvent> consumerGuard = new(
        new IntegrationEventEnvelopeValidator(),
        deadLetterStore,
        new IntegrationEventConsumerOptions(
            ConsumerName,
            MesIntegrationEventTypes.OperationTaskCompleted,
            MesIntegrationEventVersions.V1));

    public Task HandleAsync(MesOperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        consumerGuard.HandleAsync(integrationEvent, HandleValidEventAsync, cancellationToken);

    [CapSubscribe(nameof(MesOperationTaskCompletedIntegrationEvent), Group = ConsumerName)]
    public Task HandleCapAsync(MesOperationTaskCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        HandleAsync(integrationEvent, cancellationToken);

    private async Task HandleValidEventAsync(
        MesOperationTaskCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = integrationEvent.Payload;
            if (string.IsNullOrWhiteSpace(payload.WorkOrderId)
                || string.IsNullOrWhiteSpace(payload.OperationTaskId)
                || string.IsNullOrWhiteSpace(payload.SkuCode)
                || payload.OperationSequence <= 0
                || string.IsNullOrWhiteSpace(payload.WorkCenterId)
                || string.IsNullOrWhiteSpace(payload.UomCode)
                || payload.CompletedAtUtc == default)
            {
                throw new ArgumentException("Operation completion payload requires work order, operation, SKU, sequence, work center, UOM and completion time.");
            }

            await scopeCoordinator.ExecuteAsync(
                integrationEvent.OrganizationId,
                integrationEvent.EnvironmentId,
                payload.WorkOrderId,
                [payload.OperationTaskId],
                async ct =>
                {
                    if (!await QualityProcessedIntegrationEventInbox.TryRecordAsync(
                            dbContext,
                            ConsumerName,
                            integrationEvent,
                            ct))
                    {
                        return;
                    }

                    var operation = await PeriodicInspectionOperationEventProcessing.LoadOrCreateAsync(
                        dbContext,
                        integrationEvent.OrganizationId,
                        integrationEvent.EnvironmentId,
                        payload.WorkOrderId,
                        payload.OperationTaskId,
                        ct);
                    operation.Complete(
                        payload.SkuCode,
                        payload.OperationSequence,
                        payload.WorkCenterId,
                        payload.UomCode,
                        payload.CompletedAtUtc.UtcDateTime);
                },
                cancellationToken);
        }
        catch (Exception exception) when (PeriodicInspectionOperationEventProcessing.IsInvalidBusinessFact(exception))
        {
            await PeriodicInspectionOperationEventProcessing.DeadLetterAsync(
                dbContext,
                deadLetterStore,
                ConsumerName,
                integrationEvent,
                exception,
                cancellationToken);
        }
    }
}

internal static class PeriodicInspectionQuantityTaskGeneration
{
    public const int MaxWindowsPerTransaction = 256;

    public static void AddDueTasks(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<PeriodicInspectionRuntimeContext> contexts,
        DateTimeOffset occurredAtUtc,
        int maxWindows = MaxWindowsPerTransaction,
        DateTime? continuationNextAttemptAtUtc = null)
    {
        foreach (var context in contexts.OrderBy(x => x.Id))
        {
            foreach (var window in context.TakeDueQuantityWindows(
                         occurredAtUtc.UtcDateTime,
                         maxWindows,
                         continuationNextAttemptAtUtc))
            {
                var generatedAtUtc = new DateTimeOffset(window.GeneratedAtUtc);
                var task = InspectionTask.CreatePending(
                    context.OrganizationId,
                    context.EnvironmentId,
                    context.InspectionPlanId,
                    sourceType: "operation",
                    sourceService: "mes",
                    sourceDocumentId: context.WorkOrderId,
                    sourceDocumentLineId: $"{context.OperationId}:periodic-quantity:{context.Id.Id:D}:{window.Sequence}",
                    skuCode: context.SkuCode,
                    quantity: window.ThresholdQuantity,
                    uomCode: context.UomCode!,
                    batchNo: null,
                    serialNo: null,
                    generatedAtUtc,
                    dueAtUtc: generatedAtUtc.AddHours(24),
                    triggerIdempotencyKey: $"quality:periodic-quantity:{context.Id.Id:D}:{window.Sequence}");
                if (context.AssignedInspectorUserId is not null || context.AssignedTeamId is not null)
                {
                    task.Assign(
                        context.AssignedInspectorUserId,
                        context.AssignedTeamId,
                        task.Version,
                        generatedAtUtc);
                }

                dbContext.InspectionTasks.Add(task);
            }
        }
    }
}

internal static class QualityProcessedIntegrationEventInbox
{
    public static async Task<bool> TryRecordAsync(
        ApplicationDbContext dbContext,
        string consumerName,
        IIntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken)
    {
        return await ProcessedIntegrationEventInbox.TryRecordAsync(
            dbContext,
            dbContext.ProcessedIntegrationEvents,
            consumerName,
            integrationEvent,
            record => new ProcessedIntegrationEvent(
                record.ConsumerName,
                record.EventId,
                record.EventType,
                record.EventVersion,
                record.SourceService,
                record.IdempotencyKey,
                record.ProcessedAtUtc),
            ProcessedIntegrationEventInboxIdentity.EventId,
            AcquireEventIdentityLockAsync,
            cancellationToken);
    }

    private static async Task AcquireEventIdentityLockAsync(
        DbContext dbContext,
        string consumerName,
        string eventId,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        var lockKey = $"quality-integration-event-inbox:{consumerName}:{eventId}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }
}

internal static class PeriodicInspectionOperationEventProcessing
{
    public static async Task<PeriodicInspectionOperation> LoadOrCreateAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationId,
        CancellationToken cancellationToken)
    {
        var normalizedWorkOrderId = Required(workOrderId);
        var normalizedOperationId = Required(operationId);
        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.ProductionReports)
            .Include(x => x.RuntimeContexts)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.WorkOrderId == normalizedWorkOrderId
                    && x.OperationId == normalizedOperationId,
                cancellationToken);
        if (operation is not null)
        {
            return operation;
        }

        operation = PeriodicInspectionOperation.CreatePending(
            organizationId,
            environmentId,
            normalizedWorkOrderId,
            normalizedOperationId);
        dbContext.PeriodicInspectionOperations.Add(operation);
        return operation;
    }

    /// <summary>
    /// 回填过程中单道工序的处置留痕。与 <see cref="DeadLetterAsync"/> 的关键差别是**不清变更跟踪**：
    /// 同一封补投事件里其它工序已经应用的改动必须留住——清掉就等于把工序级失败重新退回成整封失败。
    /// 写进死信存储是因为它是仓库里唯一持久、可查询的消费侧留痕通道；这两个 reason code 记录的是
    /// 「已处置」而非「未处置」，重放它们是幂等的 no-op。
    /// </summary>
    public static async Task RecordBackfillNoticeAsync<TIntegrationEvent>(
        IIntegrationEventDeadLetterStore deadLetterStore,
        string consumerName,
        TIntegrationEvent integrationEvent,
        string reasonCode,
        string message,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        await deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(consumerName, integrationEvent, reasonCode, message),
            cancellationToken);
    }

    public static bool IsInvalidBusinessFact(Exception exception) =>
        exception is ArgumentException or InvalidOperationException;

    public static async Task DeadLetterAsync<TIntegrationEvent>(
        ApplicationDbContext dbContext,
        IIntegrationEventDeadLetterStore deadLetterStore,
        string consumerName,
        TIntegrationEvent integrationEvent,
        Exception exception,
        CancellationToken cancellationToken)
        where TIntegrationEvent : IIntegrationEventEnvelope
    {
        dbContext.ChangeTracker.Clear();
        await deadLetterStore.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                consumerName,
                integrationEvent,
                "invalid-business-facts",
                exception.Message),
            cancellationToken);
    }

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("MES business identity is required.")
            : value.Trim();
}
