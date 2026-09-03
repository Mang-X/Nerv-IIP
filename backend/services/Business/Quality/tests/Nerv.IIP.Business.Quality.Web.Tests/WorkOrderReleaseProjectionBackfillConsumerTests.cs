using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 存量在制工单的发布投影回填（#3000）。这批工序读首件确认恒回 <c>not-synchronized</c>、被 #2780 门禁持续拒，
/// 且不靠报工自愈；回填补上发布事实后该状态才消失。回填可重复执行是本票的核心不变量。
/// </summary>
public sealed class WorkOrderReleaseProjectionBackfillConsumerTests
{
    private static readonly DateTimeOffset ReleasedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z");

    [Fact]
    public async Task Backfill_lifts_the_operation_out_of_not_synchronized()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(FirstArticlePlan());
        await dbContext.SaveChangesAsync();
        Assert.Equal(
            QualityFirstArticleConfirmationStatuses.NotSynchronized,
            (await ConfirmAsync(dbContext)).Status);

        await HandleBackfillAsync(dbContext, Backfill());

        var confirmation = await ConfirmAsync(dbContext);
        Assert.Equal(QualityFirstArticleConfirmationStatuses.NotOpened, confirmation.Status);
        var operation = await dbContext.PeriodicInspectionOperations.SingleAsync();
        Assert.Equal("SKU-FG-1000", operation.SkuCode);
        Assert.Equal("WC-MIX", operation.WorkCenterId);
        Assert.Equal(10, operation.OperationSequence);
        Assert.Equal(ReleasedAtUtc.UtcDateTime, operation.ReleasedAtUtc);
    }

    [Fact]
    public async Task Rerunning_the_backfill_changes_no_projection_row_and_opens_no_first_article_task()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(FirstArticlePlan());
        dbContext.InspectionPlans.Add(PeriodicPlan());
        await dbContext.SaveChangesAsync();

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await HandleBackfillAsync(dbContext, Backfill(), deadLetters);
        var firstRun = await SnapshotAsync(dbContext);

        // 第二次补投带的是新 EventId、且发布时刻被重建得不一样——inbox 挡不住它，
        // 挡住它的是「已有发布事实的工序不覆盖」。
        await HandleBackfillAsync(
            dbContext,
            Backfill(eventId: "evt-backfill-second", releasedAtUtc: ReleasedAtUtc.AddHours(3)),
            deadLetters);
        var secondRun = await SnapshotAsync(dbContext);

        Assert.Equal(firstRun, secondRun);
        // 不跳过已有发布事实时，第二次补投会被判为冲突事实进死信：投影确实没变，但工单被当成坏数据。
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Empty(await dbContext.InspectionTasks
            .Where(x => x.SourceType == FirstArticleInspection.SourceType)
            .ToArrayAsync());
    }

    [Fact]
    public async Task Backfill_never_overwrites_release_facts_that_already_arrived_from_the_live_event()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters).HandleAsync(LiveRelease(), CancellationToken.None);

        await HandleBackfillAsync(
            dbContext,
            Backfill(releasedAtUtc: ReleasedAtUtc.AddDays(-30)),
            deadLetters);

        var operation = await dbContext.PeriodicInspectionOperations.SingleAsync();
        Assert.Equal(ReleasedAtUtc.UtcDateTime, operation.ReleasedAtUtc);
        // 冲突的发布事实本该进死信；回填因为根本不碰已有行，所以既不覆盖也不产生死信。
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    /// <summary>
    /// 回填补的是发布事实，不是巡检历史：补投之前已经流走的产量不追认周期巡检窗口，
    /// 否则一次回填就为历史产量成批开出已过期任务；积压超过 <c>MaximumSupportedPendingQuantityWindows</c> 时
    /// <c>TakeDueQuantityWindows</c> 还会抛出，整张工单进死信、回填对它失效。
    /// 「不追认」不等于「不生成」——补投之后新报的量照常按间隔开出。
    /// </summary>
    [Fact]
    public async Task Backfill_does_not_open_periodic_tasks_for_quantity_produced_before_it_ran()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(PeriodicPlan());
        await dbContext.SaveChangesAsync();
        await HandleReportAsync(dbContext, ProductionReport());

        await HandleBackfillAsync(dbContext, Backfill());

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.RuntimeContexts)
            .SingleAsync();
        var runtimeContext = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(250m, runtimeContext.QuantityHighWater);
        Assert.Equal("EA", runtimeContext.UomCode);
        // 250 件 / 间隔 100 = 2 扇历史窗口，全部记为已生成，一张任务都不开。
        Assert.Equal(2, runtimeContext.LastGeneratedQuantityWindowSequence);
        Assert.Empty(await dbContext.InspectionTasks.ToArrayAsync());

        // 补投之后再报 100 件：高水位 350、目标 3，只开出第 3 扇窗口这一张。
        await HandleReportAsync(dbContext, ProductionReport(reportNo: "RPT-002", goodQuantity: 100m));

        var task = Assert.Single(await dbContext.InspectionTasks.ToArrayAsync());
        Assert.EndsWith(":3", task.SourceDocumentLineId, StringComparison.Ordinal);
        Assert.Equal(300m, task.Quantity);
    }

    /// <summary>
    /// 时间间隔侧同理：回填时刻之前流逝的时间窗口一并记为已生成，时间调度器不会追认补开。
    /// </summary>
    [Fact]
    public async Task Backfill_does_not_open_periodic_tasks_for_time_elapsed_before_it_ran()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(TimeIntervalPlan());
        await dbContext.SaveChangesAsync();
        await HandleReportAsync(dbContext, ProductionReport());

        await HandleBackfillAsync(dbContext, Backfill());

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.RuntimeContexts)
            .SingleAsync();
        var runtimeContext = Assert.Single(operation.RuntimeContexts);
        // 首次活动 2026-08-02T00:00Z、间隔 2 小时、补投时刻 2026-09-01T00:00Z → 360 扇窗口已流走。
        Assert.Equal(360, runtimeContext.LastGeneratedTimeWindowSequence);
        Assert.Equal(
            DateTime.Parse("2026-08-02T00:00:00Z").ToUniversalTime(),
            runtimeContext.TimeScheduleAnchorAtUtc);
        Assert.Equal(
            DateTime.Parse("2026-09-01T02:00:00Z").ToUniversalTime(),
            runtimeContext.NextTimeWindowAtUtc);
        Assert.Empty(runtimeContext.TakeDueTimeWindows(
            DateTime.Parse("2026-09-01T00:00:00Z").ToUniversalTime(),
            maxWindows: 256));
    }

    /// <summary>
    /// R-c 端到端：存量在制工单里有一道工序已经有完工事实，且它的 <c>CompletionSkuCode</c> 与工单 SkuId 不等
    /// （MES 的 <c>OperationTask</c> 在未传 SKU 时把 <c>SkuCode</c> 回落成工单号，该值随完工事件进了 Quality）。
    /// 回填一次后，该工单**全部**工序读首件确认都不得是 <c>not-synchronized</c>——
    /// 一道工序的重建值与权威事实不一致，不能让同工单其余工序一起失去补投。
    /// </summary>
    [Fact]
    public async Task Backfill_covers_every_operation_even_when_one_carries_conflicting_completion_facts()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(FirstArticlePlan());
        // 巡检档配在**载荷 SKU** 上：让位后不得再拿它给 OP-10 建运行上下文，
        // 而没让位的 OP-20 必须照常建出来——后者是正对照，证明档确实取得到。
        dbContext.InspectionPlans.Add(PeriodicPlan());
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        // OP-10 先完工，完工事实带的是回落成工单号的 junk SKU。
        await HandleCompletionAsync(dbContext, OperationCompleted("OP-10", skuCode: "WO-001"), deadLetters);

        await HandleBackfillAsync(dbContext, Backfill(operationIds: ["OP-10", "OP-20"]), deadLetters);

        foreach (var operationId in new[] { "OP-10", "OP-20" })
        {
            var confirmation = await ConfirmAsync(dbContext, operationId);
            Assert.NotEqual(QualityFirstArticleConfirmationStatuses.NotSynchronized, confirmation.Status);
        }

        // 让位：冲突属性取既有权威事实，而不是丢弃该工序。
        var completed = await dbContext.PeriodicInspectionOperations
            .Include(x => x.RuntimeContexts)
            .SingleAsync(x => x.OperationId == "OP-10");
        Assert.Equal("WO-001", completed.SkuCode);
        Assert.Equal(ReleasedAtUtc.UtcDateTime, completed.ReleasedAtUtc);
        // 让位到权威 SKU 后不得依据**载荷 SKU** 的巡检档建上下文——
        // 一条声明着 WO-001、却绑着 SKU-FG-1000 那张档的运行上下文是真错。
        Assert.Empty(completed.RuntimeContexts);
        var untouched = await dbContext.PeriodicInspectionOperations
            .Include(x => x.RuntimeContexts)
            .SingleAsync(x => x.OperationId == "OP-20");
        Assert.Single(untouched.RuntimeContexts);
        // 差异不静默。
        var notice = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("backfill-release-fact-substituted", notice.FailureCode);
        Assert.Contains("sku-code", notice.FailureMessage, StringComparison.Ordinal);
        // 已按权威事实处置完，只是留痕；不该混进「待处理」队列。
        Assert.Equal(IntegrationEventDeadLetterStatus.Ignored, notice.Status);

        // 再跑一次：行数与内容不变，且不新增首件检验任务。
        var before = await SnapshotAsync(dbContext);
        await HandleBackfillAsync(
            dbContext,
            Backfill(eventId: "evt-backfill-second", operationIds: ["OP-10", "OP-20"]),
            deadLetters);

        Assert.Equal(before, await SnapshotAsync(dbContext));
        Assert.Empty(await dbContext.InspectionTasks
            .Where(x => x.SourceType == FirstArticleInspection.SourceType)
            .ToArrayAsync());
    }

    /// <summary>
    /// B2 单独承重：内存档筛的**工作中心**半边，与已闭合的 SKU 半边是同一行、同一族。
    /// 载荷把 OP-30 记在 <c>WC-MIX</c>，权威完工事实说它在 <c>WC-ALT</c>，
    /// 于是 <c>facts.WorkCenterId</c> 让位成 <c>WC-ALT</c>；此时只有 <c>WC-ALT</c> 那张周期档配得上它。
    /// 删掉 <c>plan.WorkCenterId == facts.WorkCenterId</c>，<c>WC-MIX</c> 的档会一并挂上去——
    /// 把**另一个工作中心**的巡检档挂到本工序，与 SKU 半边判为「真错」的情形完全同型。
    ///
    /// 两张档的量间隔取不同值（100 / 250），因此断言判的是「配上了哪一张」而不只是「配上了几张」。
    /// OP-40 是正对照：它本来就在 <c>WC-ALT</c>、不让位，证明 <c>WC-ALT</c> 那张档在本夹具里确实取得到，
    /// 否则「只配上一张」可能只是因为根本没档。
    /// </summary>
    [Fact]
    public async Task Backfill_does_not_borrow_a_periodic_plan_from_another_work_center()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(PeriodicPlan());
        dbContext.InspectionPlans.Add(PeriodicPlan("PLAN-PERIODIC-ALT", "WC-ALT", quantityInterval: 250m));
        await dbContext.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        // 同 SKU、只有工作中心与载荷不符：走让位而不是拒绝。
        await HandleCompletionAsync(
            dbContext,
            OperationCompleted("OP-30", skuCode: "SKU-FG-1000", workCenterId: "WC-ALT"),
            deadLetters);

        await HandleBackfillAsync(
            dbContext,
            Backfill(operations:
            [
                new ReleasedOperationPayload("OP-30", 10, "WC-MIX"),
                new ReleasedOperationPayload("OP-40", 20, "WC-ALT"),
            ]),
            deadLetters);

        var yielded = await dbContext.PeriodicInspectionOperations
            .Include(x => x.RuntimeContexts)
            .SingleAsync(x => x.OperationId == "OP-30");
        Assert.Equal("WC-ALT", yielded.WorkCenterId);
        var yieldedContext = Assert.Single(yielded.RuntimeContexts);
        Assert.Equal(250m, yieldedContext.QuantityInterval);
        Assert.Equal("WC-ALT", yieldedContext.WorkCenterId);

        var untouched = await dbContext.PeriodicInspectionOperations
            .Include(x => x.RuntimeContexts)
            .SingleAsync(x => x.OperationId == "OP-40");
        var untouchedContext = Assert.Single(untouched.RuntimeContexts);
        Assert.Equal(250m, untouchedContext.QuantityInterval);

        // 让位不静默：工作中心那一项要留得下痕。
        var notice = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("backfill-release-fact-substituted", notice.FailureCode);
        Assert.Contains("work-center-id", notice.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-a 单独承重：失败粒度是工序，不是整封事件。这里让 OP-10 因「报工工作中心与发布事实冲突」
    /// 被 <c>ApplyRelease</c> 拒掉（不是让位能救的那一类），断言 OP-20 照常拿到发布事实，
    /// 且 OP-10 的失败有留痕。
    /// </summary>
    [Fact]
    public async Task One_operation_rejected_does_not_deny_the_rest_of_the_work_order()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        // 被拒的那道工序必须**排在后面**：ValidateReleasedOperations 按 OperationId 重排，
        // 拿 OP-10 当被拒工序时它恒排第一，前面没有任何已应用的改动可供丢失，
        // 「留痕不清变更跟踪」这条不变量就没有会失败的输入。这里让 OP-30 被拒、OP-20 先成功。
        await HandleReportAsync(
            dbContext,
            ProductionReport(operationId: "OP-30", workCenterId: "WC-OTHER"));

        await HandleBackfillAsync(dbContext, Backfill(operationIds: ["OP-20", "OP-30"]), deadLetters);

        var rejected = await dbContext.PeriodicInspectionOperations.SingleAsync(x => x.OperationId == "OP-30");
        Assert.Null(rejected.ReleasedAtUtc);
        var applied = await dbContext.PeriodicInspectionOperations.SingleAsync(x => x.OperationId == "OP-20");
        Assert.Equal(ReleasedAtUtc.UtcDateTime, applied.ReleasedAtUtc);
        Assert.Equal(
            QualityFirstArticleConfirmationStatuses.NotSynchronized,
            (await ConfirmAsync(dbContext, "OP-30")).Status);
        Assert.NotEqual(
            QualityFirstArticleConfirmationStatuses.NotSynchronized,
            (await ConfirmAsync(dbContext, "OP-20")).Status);
        // 幂等登记也在「已应用改动」之列：清变更跟踪会把它一起丢掉，重跑就不再是 no-op。
        Assert.Single(await dbContext.ProcessedIntegrationEvents
            .Where(x => x.ConsumerName
                == WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName)
            .ToArrayAsync());

        var notice = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("backfill-operation-rejected", notice.FailureCode);
        Assert.Equal(IntegrationEventDeadLetterStatus.Pending, notice.Status);
        Assert.Contains("OP-30", notice.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 直投侧不得跟着回填一起「跳过已有发布事实」：同一工序收到第二份**内容不同**的发布事实
    /// 是真实异常，必须判为冲突进死信。本 PR 把该判断做成了按调用点取值的参数，
    /// 因此直投那一半也要有断言承重，否则参数被翻反不会红。
    /// </summary>
    [Fact]
    public async Task Live_release_with_conflicting_facts_still_dead_letters()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await HandleLiveReleaseAsync(dbContext, LiveRelease(), deadLetters);

        // 新 EventId（inbox 挡不住），发布时刻不同（内容冲突）。
        await HandleLiveReleaseAsync(
            dbContext,
            LiveRelease(eventId: "evt-release-WO-001-second", releasedAtUtc: ReleasedAtUtc.AddHours(5)),
            deadLetters);

        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal(
            WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName,
            deadLetter.ConsumerName);
        var operation = await dbContext.PeriodicInspectionOperations.SingleAsync();
        Assert.Equal(ReleasedAtUtc.UtcDateTime, operation.ReleasedAtUtc);
    }

    /// <summary>
    /// 「独立 topic + 独立消费组」是本 PR 隔离主张的承重结构：回填消费组必须与直投消费组不同，
    /// 且回填的 CAP 订阅与消费者注册都必须指向回填事件。没有断言时这三处被改成直投的值不会红。
    /// </summary>
    [Fact]
    public void Backfill_consumer_is_registered_on_its_own_topic_and_group()
    {
        var backfill = typeof(WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts);
        var live = typeof(WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts);

        Assert.NotEqual(
            WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName,
            WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName);

        var backfillTopic = nameof(WorkOrderReleaseProjectionBackfilledIntegrationEvent);
        Assert.Equal(backfillTopic, ReadCapSubscribeTopic(backfill));
        Assert.Equal(
            WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName,
            ReadCapSubscribeGroup(backfill));
        Assert.Equal(backfillTopic, ReadIntegrationEventConsumerEventName(backfill));
        Assert.Equal(
            WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName,
            ReadIntegrationEventConsumerName(backfill));

        // 直投侧同时钉住，避免「两边都被改成同一个值」这种改法悄悄通过。
        Assert.Equal(nameof(WorkOrderReleasedIntegrationEvent), ReadCapSubscribeTopic(live));
        Assert.Equal(
            WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts.ConsumerName,
            ReadCapSubscribeGroup(live));
    }

    private static string ReadCapSubscribeTopic(Type handler) =>
        ReadCapSubscribeProperty(handler, "Name");

    private static string ReadCapSubscribeGroup(Type handler) =>
        ReadCapSubscribeProperty(handler, "Group");

    private static string ReadCapSubscribeProperty(Type handler, string propertyName)
    {
        var attribute = handler
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes())
            .Single(x => string.Equals(x.GetType().Name, "CapSubscribeAttribute", StringComparison.Ordinal));
        return (string)attribute.GetType().GetProperty(propertyName)!.GetValue(attribute)!;
    }

    private static string ReadIntegrationEventConsumerEventName(Type handler) =>
        ReadIntegrationEventConsumerField(handler, 0);

    private static string ReadIntegrationEventConsumerName(Type handler) =>
        ReadIntegrationEventConsumerField(handler, 1);

    private static string ReadIntegrationEventConsumerField(Type handler, int index)
    {
        var attribute = handler
            .GetCustomAttributesData()
            .Single(x => string.Equals(
                x.AttributeType.Name,
                "IntegrationEventConsumerAttribute",
                StringComparison.Ordinal));
        return (string)attribute.ConstructorArguments[index].Value!;
    }

    private static async Task HandleReportAsync(
        ApplicationDbContext dbContext,
        ProductionReportRecordedIntegrationEvent integrationEvent) =>
        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(integrationEvent, CancellationToken.None);

    private static async Task HandleLiveReleaseAsync(
        ApplicationDbContext dbContext,
        WorkOrderReleasedIntegrationEvent integrationEvent,
        InMemoryIntegrationEventDeadLetterStore deadLetters) =>
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters).HandleAsync(integrationEvent, CancellationToken.None);

    private static async Task HandleBackfillAsync(
        ApplicationDbContext dbContext,
        WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent,
        InMemoryIntegrationEventDeadLetterStore? deadLetters = null) =>
        await new WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters ?? new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(integrationEvent, CancellationToken.None);

    private static Task<FirstArticleConfirmationResponse> ConfirmAsync(
        ApplicationDbContext dbContext,
        string operationId = "OP-10") =>
        new GetFirstArticleConfirmationQueryHandler(dbContext).Handle(
            new GetFirstArticleConfirmationQuery("org-001", "env-dev", "WO-001", operationId),
            CancellationToken.None);

    private static async Task HandleCompletionAsync(
        ApplicationDbContext dbContext,
        MesOperationTaskCompletedIntegrationEvent integrationEvent,
        InMemoryIntegrationEventDeadLetterStore deadLetters) =>
        await new MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters).HandleAsync(integrationEvent, CancellationToken.None);

    private static MesOperationTaskCompletedIntegrationEvent OperationCompleted(
        string operationId,
        string skuCode,
        string workCenterId = "WC-MIX") => new(
        $"evt-complete-{operationId}",
        MesIntegrationEventTypes.OperationTaskCompleted,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-20T00:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        $"corr-complete-{operationId}",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        $"mes:operation-completed:org-001:env-dev:WO-001:{operationId}",
        new OperationTaskCompletedPayload(
            "WO-001", operationId, skuCode, 10, workCenterId, 1000m, "EA", false,
            DateTimeOffset.Parse("2026-08-20T00:00:00Z")));

    private static async Task<string[]> SnapshotAsync(ApplicationDbContext dbContext) =>
        await dbContext.PeriodicInspectionOperations
            .AsNoTracking()
            .OrderBy(x => x.WorkOrderId)
            .ThenBy(x => x.OperationId)
            .Select(x => x.WorkOrderId + "|" + x.OperationId + "|" + x.SkuCode + "|" + x.OperationSequence
                + "|" + x.WorkCenterId + "|" + x.ReleasedAtUtc)
            .ToArrayAsync();

    private static WorkOrderReleaseProjectionBackfilledIntegrationEvent Backfill(
        string eventId = "evt-backfill-first",
        DateTimeOffset? releasedAtUtc = null,
        IReadOnlyCollection<string>? operationIds = null,
        IReadOnlyCollection<ReleasedOperationPayload>? operations = null) => new(
        eventId,
        MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "mes:work-order-release-projection-backfill:org-001:env-dev:WO-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:work-order-release-projection-backfill:org-001:env-dev:WO-001",
        new WorkOrderReleasedPayload(
            "WO-001",
            "SKU-FG-1000",
            1000m,
            releasedAtUtc ?? ReleasedAtUtc,
            operations?.ToArray()
                ?? (operationIds ?? ["OP-10"])
                    .Select(operationId => new ReleasedOperationPayload(operationId, 10, "WC-MIX"))
                    .ToArray()));

    private static WorkOrderReleasedIntegrationEvent LiveRelease(
        string eventId = "evt-release-WO-001",
        DateTimeOffset? releasedAtUtc = null) => new(
        eventId,
        MesIntegrationEventTypes.WorkOrderReleased,
        MesIntegrationEventVersions.V1,
        ReleasedAtUtc,
        MesIntegrationEventSources.BusinessMes,
        "mes:work-order-released:org-001:env-dev:WO-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:work-order-released:org-001:env-dev:WO-001",
        new WorkOrderReleasedPayload(
            "WO-001",
            "SKU-FG-1000",
            1000m,
            releasedAtUtc ?? ReleasedAtUtc,
            [new ReleasedOperationPayload("OP-10", 10, "WC-MIX")]));

    private static ProductionReportRecordedIntegrationEvent ProductionReport(
        string reportNo = "RPT-001",
        decimal goodQuantity = 250m,
        string workCenterId = "WC-MIX",
        string operationId = "OP-10") => new(
        $"evt-report-{reportNo}",
        MesIntegrationEventTypes.ProductionReportRecorded,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        $"corr-report-{reportNo}",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        $"mes:production-report-recorded:org-001:env-dev:{reportNo}",
        new ProductionReportRecordedPayload(
            reportNo, "WO-001", operationId, workCenterId, null, goodQuantity, 0m, 0m, "EA", null,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"), false));

    private static InspectionPlan FirstArticlePlan()
    {
        var plan = InspectionPlan.Create(
            "org-001", "env-dev", "PLAN-FA-1000", "first-article", "SKU-FG-1000", null, "WC-MIX", null, null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private static InspectionPlan PeriodicPlan(
        string planCode = "PLAN-PERIODIC-1000",
        string workCenterId = "WC-MIX",
        decimal quantityInterval = 100m)
    {
        var plan = InspectionPlan.Create(
            "org-001", "env-dev", planCode, "operation", "SKU-FG-1000", null, workCenterId, null, "mes-operation",
            quantityInterval: quantityInterval);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private static InspectionPlan TimeIntervalPlan()
    {
        var plan = InspectionPlan.Create(
            "org-001", "env-dev", "PLAN-TIME-1000", "operation", "SKU-FG-1000", null, "WC-MIX", null, "mes-operation",
            timeIntervalHours: 2m);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"quality-release-projection-backfill-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
