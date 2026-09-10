using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Contracts.Quality;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// GitHub #3319：检验记录的链身份从「来源单据」扩到「来源单据 + 来源行」。
///
/// <para>改前 <c>InspectionTask</c> 会按触发幂等键的**文本形状**在两列里二选一，把其中一列搬成
/// 检验记录的来源单据身份；来源行本身没有落点，唯一键 <c>ux_inspection_records_source_attempt</c>
/// 也不含它。后果不是报错而是**静默复用**：第二张任务在
/// <c>CreateInspectionRecordFromTaskCommandHandler</c> 里命中第一张的记录，直接 Complete，
/// 两道工序／两行收货共用一条结论。</para>
///
/// <para><b>本类看守的是「命中既有记录」那一步的判据</b>（<c>FindBySourceDocumentAsync</c> 的谓词
/// 与记录对来源行的承接），用 InMemory provider 就够——它不需要数据库执行唯一索引。
/// <b>唯一索引本身</b>（含 <c>NULLS NOT DISTINCT</c>）由
/// <see cref="InspectionRecordSourceLinePostgresTests"/> 在真库上看守；InMemory 与 SQLite 都不执行
/// 唯一索引，只在这里断言会假绿。</para>
///
/// <para><b>行为变化（改前合并、改后拆开）在下面逐条声明</b>，每条都配一格：收货同单两行、
/// 终检同申请单两工单。工序检那一支既是缺陷也是行为变化。</para>
/// </summary>
public sealed class InspectionRecordSourceLineIdentityTests
{
    /// <summary>
    /// 工序检：链边界改前是 (工单, SKU)，与工序无关 ⇒ 同一工单两道工序做同一 SKU 检验，
    /// 第二道会静默复用第一道的结论。改后各成独立 attempt 链。
    ///
    /// <para>这一格**不依赖首件的 <c>{工单}:{工序}</c> 复合编码**：来源单据两条任务都是裸工单号，
    /// 区分只能来自来源行。</para>
    /// </summary>
    [Fact]
    public async Task Two_operations_of_the_same_work_order_and_sku_form_independent_attempt_chains()
    {
        await using var dbContext = CreateDbContext(nameof(Two_operations_of_the_same_work_order_and_sku_form_independent_attempt_chains));
        var plan = ActivePlan("PLAN-OP-1000", QualityInspectionSourceTypes.Operation, "SKU-FG-1000");
        var first = OperationTask(plan, "WO-001", "OP-10");
        var second = OperationTask(plan, "WO-001", "OP-20");
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionTasks.AddRange(first, second);
        await dbContext.SaveChangesAsync();

        var firstResult = await SubmitAsync(dbContext, first, "submit-op-10");
        var secondResult = await SubmitAsync(dbContext, second, "submit-op-20");

        Assert.NotEqual(firstResult.InspectionRecordId, secondResult.InspectionRecordId);
        var records = await dbContext.InspectionRecords.OrderBy(x => x.SourceDocumentLineId).ToListAsync();
        Assert.Equal(2, records.Count);
        // 两条记录的来源单据身份**相同**——区分完全来自来源行，去掉来源行维度这一格必红。
        Assert.All(records, record => Assert.Equal("WO-001", record.SourceDocumentId));
        Assert.Equal(["OP-10", "OP-20"], records.Select(x => x.SourceDocumentLineId));
        // 两条都是各自链的初检，而不是「同一条链的第二次尝试」。
        Assert.All(records, record => Assert.Equal(1, record.AttemptNumber));
        Assert.All(records, record => Assert.Null(record.ReinspectionOfInspectionRecordId));
        // 两张任务各自绑定自己的记录，第二张没有被指到第一张的结论上。
        var tasks = await dbContext.InspectionTasks.OrderBy(x => x.SourceDocumentLineId).ToListAsync();
        Assert.Equal(
            [firstResult.InspectionRecordId, secondResult.InspectionRecordId],
            tasks.Select(x => x.InspectionRecordId));
    }

    /// <summary>
    /// 同一道工序重复提交仍然幂等：来源行相同 ⇒ 命中既有记录，不新建第二条。
    /// 没有这一格，把 <c>FindBySourceDocumentAsync</c> 整段删掉（永远新建）也能让上一格绿。
    /// </summary>
    [Fact]
    public async Task The_same_operation_line_still_reuses_the_existing_conclusion()
    {
        await using var dbContext = CreateDbContext(nameof(The_same_operation_line_still_reuses_the_existing_conclusion));
        var plan = ActivePlan("PLAN-OP-2000", QualityInspectionSourceTypes.Operation, "SKU-FG-2000");
        var first = OperationTask(plan, "WO-002", "OP-10", skuCode: "SKU-FG-2000");
        // 同工单同工序同 SKU 的第二张任务（例如上游重投后按另一个触发键开出）。
        var duplicate = OperationTask(plan, "WO-002", "OP-10", skuCode: "SKU-FG-2000", triggerSuffix: "retry");
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionTasks.AddRange(first, duplicate);
        await dbContext.SaveChangesAsync();

        var firstResult = await SubmitAsync(dbContext, first, "submit-dup-1");
        var duplicateResult = await SubmitAsync(dbContext, duplicate, "submit-dup-2");

        Assert.Equal(firstResult.InspectionRecordId, duplicateResult.InspectionRecordId);
        Assert.Single(await dbContext.InspectionRecords.ToListAsync());
    }

    /// <summary>
    /// <b>行为变化（收货）：改前合并、改后拆开。</b>收货任务的唯一键含来源行，同一收货单同一 SKU
    /// 的两行会开出两张任务；改前它们算出同一条记录身份，第二张直接复用第一张的结论。
    /// 改后两行各成独立链——这是修复，但落库事实确实变了。
    /// </summary>
    [Fact]
    public async Task Two_receiving_lines_of_the_same_receipt_and_sku_no_longer_share_one_conclusion()
    {
        await using var dbContext = CreateDbContext(nameof(Two_receiving_lines_of_the_same_receipt_and_sku_no_longer_share_one_conclusion));
        var plan = ActivePlan("PLAN-RCV-1000", QualityInspectionSourceTypes.Receiving, "SKU-RM-1000");
        var line1 = ReceivingTask(plan, "IN-001", "LINE-001");
        var line2 = ReceivingTask(plan, "IN-001", "LINE-002");
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionTasks.AddRange(line1, line2);
        await dbContext.SaveChangesAsync();

        var firstResult = await SubmitAsync(dbContext, line1, "submit-line-1");
        var secondResult = await SubmitAsync(dbContext, line2, "submit-line-2", InspectionLineResults.Failed);

        Assert.NotEqual(firstResult.InspectionRecordId, secondResult.InspectionRecordId);
        // 第二行的真实结论（不合格）不再被第一行的合格结论顶掉。
        Assert.Equal(InspectionRecordResults.Passed, firstResult.Result);
        Assert.Equal(InspectionRecordResults.Rejected, secondResult.Result);
        var records = await dbContext.InspectionRecords.OrderBy(x => x.SourceDocumentLineId).ToListAsync();
        Assert.Equal(["LINE-001", "LINE-002"], records.Select(x => x.SourceDocumentLineId));
        Assert.All(records, record => Assert.Equal("IN-001", record.SourceDocumentId));
    }

    /// <summary>
    /// <b>行为变化（终检）：改前合并、改后拆开。</b>终检的来源单据是入库申请单号，来源行是工单。
    /// 同一申请单下两张工单同一 SKU 若真的到达（MES 侧是否会发出这种事实未坐实，见 PR 正文），
    /// 改前会被判成同一条链；改后按工单拆开。
    /// </summary>
    [Fact]
    public async Task Two_work_orders_under_the_same_finished_goods_request_no_longer_share_one_conclusion()
    {
        await using var dbContext = CreateDbContext(nameof(Two_work_orders_under_the_same_finished_goods_request_no_longer_share_one_conclusion));
        var plan = ActivePlan("PLAN-FGR-1000", QualityInspectionSourceTypes.Final, "SKU-FG-3000");
        var fromWo1 = FinalTask(plan, "FGR-REQ-001", "WO-101");
        var fromWo2 = FinalTask(plan, "FGR-REQ-001", "WO-102");
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionTasks.AddRange(fromWo1, fromWo2);
        await dbContext.SaveChangesAsync();

        var firstResult = await SubmitAsync(dbContext, fromWo1, "submit-fgr-1");
        var secondResult = await SubmitAsync(dbContext, fromWo2, "submit-fgr-2");

        Assert.NotEqual(firstResult.InspectionRecordId, secondResult.InspectionRecordId);
        var records = await dbContext.InspectionRecords.OrderBy(x => x.SourceDocumentLineId).ToListAsync();
        Assert.Equal(["WO-101", "WO-102"], records.Select(x => x.SourceDocumentLineId));
        Assert.All(records, record => Assert.Equal("FGR-REQ-001", record.SourceDocumentId));
    }

    /// <summary>
    /// 周期检：来源单据身份不再被换成复合窗口行号。改前记录的 <c>SourceDocumentId</c> 是
    /// <c>{工序}:{kind}:{ctx}:{seq}</c>，改后是工单，复合行号原样落在来源行列上。
    /// 同一工单两个窗口仍各成独立链——承担区分的从「被换掉的来源单据列」变成了来源行列。
    /// </summary>
    [Fact]
    public async Task Periodic_windows_keep_their_composite_identity_in_the_line_column()
    {
        await using var dbContext = CreateDbContext(nameof(Periodic_windows_keep_their_composite_identity_in_the_line_column));
        var plan = ActivePlan("PLAN-PERIODIC-1000", QualityInspectionSourceTypes.Operation, "SKU-FG-4000");
        var contextId = Guid.Parse("0f9c1a2b-3d4e-4f50-8617-2a3b4c5d6e7f");
        var window1 = PeriodicTask(plan, "WO-003", "OP-30", contextId, 1);
        var window2 = PeriodicTask(plan, "WO-003", "OP-30", contextId, 2);
        dbContext.InspectionPlans.Add(plan);
        dbContext.InspectionTasks.AddRange(window1, window2);
        await dbContext.SaveChangesAsync();

        var firstResult = await SubmitAsync(dbContext, window1, "submit-window-1");
        var secondResult = await SubmitAsync(dbContext, window2, "submit-window-2");

        Assert.NotEqual(firstResult.InspectionRecordId, secondResult.InspectionRecordId);
        var records = await dbContext.InspectionRecords.OrderBy(x => x.SourceDocumentLineId).ToListAsync();
        Assert.All(records, record => Assert.Equal("WO-003", record.SourceDocumentId));
        Assert.Equal(
            [
                PeriodicInspectionSourceLine.LineId("OP-30", PeriodicInspectionSourceLine.TimeKind, contextId, 1),
                PeriodicInspectionSourceLine.LineId("OP-30", PeriodicInspectionSourceLine.TimeKind, contextId, 2),
            ],
            records.Select(x => x.SourceDocumentLineId));
    }

    /// <summary>
    /// <b>行为变化（直录 × 行级任务）：改前合并、改后拆开。</b>直录检验命令的契约里没有来源行，
    /// 它建出的记录属于「整张来源单据」那条链；带来源行的任务提交因此不再命中它，而是在自己那条
    /// 行级链上新建记录。改前两者被判成同一条链，行级任务会直接复用整单级的结论。
    /// </summary>
    [Fact]
    public async Task A_line_scoped_task_no_longer_reuses_a_directly_recorded_document_level_conclusion()
    {
        await using var dbContext = CreateDbContext(nameof(A_line_scoped_task_no_longer_reuses_a_directly_recorded_document_level_conclusion));
        var plan = ActivePlan("PLAN-RCV-3000", QualityInspectionSourceTypes.Receiving, "SKU-RM-3000");
        dbContext.InspectionPlans.Add(plan);
        await dbContext.SaveChangesAsync();

        // 直录路径：命令契约里没有来源行 ⇒ 记录落在「整张收货单」那条链上。
        var documentLevelRecordId = await new CreateInspectionRecordCommandHandler(
                new InspectionRecordRepository(dbContext),
                new InspectionPlanRepository(dbContext),
                new InspectionTaskRepository(dbContext))
            .Handle(
                new CreateInspectionRecordCommand(
                    "org-001",
                    "env-dev",
                    plan.Id,
                    QualityInspectionSourceTypes.Receiving,
                    QualityInspectionSourceServices.Wms,
                    "IN-3000",
                    "SKU-RM-3000",
                    10m,
                    null,
                    null,
                    [new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
                    null,
                    []),
                CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var lineTask = InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            QualityInspectionSourceTypes.Receiving,
            QualityInspectionSourceServices.Wms,
            "IN-3000",
            "LINE-001",
            "SKU-RM-3000",
            10m,
            "kg",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            "wms:inbound-completed:org-001:env-dev:IN-3000:LINE-001");
        dbContext.InspectionTasks.Add(lineTask);
        await dbContext.SaveChangesAsync();

        var lineResult = await SubmitAsync(dbContext, lineTask, "submit-in-3000-line-1");

        Assert.NotEqual(documentLevelRecordId, lineResult.InspectionRecordId);
        var records = await dbContext.InspectionRecords.OrderBy(x => x.SourceDocumentLineId).ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.Equal([null, "LINE-001"], records.Select(x => x.SourceDocumentLineId));
        Assert.All(records, record => Assert.Equal("IN-3000", record.SourceDocumentId));
    }

    /// <summary>
    /// 复检沿用初检的来源行，因此复检链留在同一条来源链上（唯一键靠 <c>attempt_number</c> 区分）。
    /// 没有这一格，<c>Reinspect</c> 忘了拷贝来源行也不会红。
    /// </summary>
    [Fact]
    public void Reinspection_inherits_the_source_line_identity()
    {
        var initial = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            QualityInspectionSourceTypes.Operation,
            QualityInspectionSourceServices.Mes,
            "WO-004",
            sourceDocumentLineId: "OP-40",
            "SKU-FG-5000",
            10m,
            null,
            null,
            [InspectionResultLineInput.Fail("appearance", "scratch", "外观划伤", 1m, [])],
            "外观划伤",
            ["file-001"]);

        var reinspection = InspectionRecord.Reinspect(
            initial,
            null,
            [InspectionResultLineInput.Pass("appearance", "ok", null, [])],
            null,
            []);

        Assert.Equal("OP-40", reinspection.SourceDocumentLineId);
        Assert.Equal("WO-004", reinspection.SourceDocumentId);
        Assert.Equal(2, reinspection.AttemptNumber);
    }

    /// <summary>
    /// 存量周期检记录的形状 <c>(复合窗口身份, NULL)</c> 会被复检**原样拷到新记录上**——所以它不是
    /// 「只读不写」的历史数据，新代码会继续产出它并重新发布集成事件。这一格钉住可达性；
    /// 那个形状解出来是什么，由 <c>InspectionResultMesScopeTests</c> 的对应格看守。
    /// </summary>
    [Fact]
    public void Reinspecting_a_pre_migration_periodic_record_reproduces_the_legacy_shape()
    {
        var legacy = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            QualityInspectionSourceTypes.Operation,
            QualityInspectionSourceServices.Mes,
            // 改前周期检把复合窗口身份搬进了来源单据那一列，来源行那一列当时还不存在。
            PeriodicInspectionSourceLine.LineId(
                "OP-50",
                PeriodicInspectionSourceLine.TimeKind,
                Guid.Parse("0f9c1a2b-3d4e-4f50-8617-2a3b4c5d6e7f"),
                9),
            sourceDocumentLineId: null,
            "SKU-FG-6000",
            10m,
            null,
            null,
            [InspectionResultLineInput.Fail("appearance", "scratch", "外观划伤", 1m, [])],
            "外观划伤",
            ["file-001"]);

        var reinspection = InspectionRecord.Reinspect(
            legacy,
            null,
            [InspectionResultLineInput.Pass("appearance", "ok", null, [])],
            null,
            []);

        Assert.Null(reinspection.SourceDocumentLineId);
        Assert.Equal(legacy.SourceDocumentId, reinspection.SourceDocumentId);
        Assert.Equal(2, reinspection.AttemptNumber);
    }

    /// <summary>直录检验没有来源行：链身份只到来源单据一级，来源行列为空。</summary>
    [Fact]
    public void Directly_recorded_inspections_carry_no_source_line()
    {
        var record = InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            QualityInspectionSourceTypes.Receiving,
            QualityInspectionSourceServices.PurchaseReceipt,
            "RCV-900",
            sourceDocumentLineId: null,
            "SKU-RM-9000",
            5m,
            null,
            null,
            [InspectionResultLineInput.Pass("appearance", "ok", null, [])],
            null,
            []);

        Assert.Null(record.SourceDocumentLineId);
    }

    private static async Task<CreateInspectionRecordFromTaskResult> SubmitAsync(
        ApplicationDbContext dbContext,
        InspectionTask task,
        string idempotencyKey,
        string lineResult = InspectionLineResults.Passed)
    {
        if (task.Status == InspectionTaskStatuses.Pending)
        {
            task.Assign("qa-user-001", null, task.Version, DateTimeOffset.Parse("2026-07-05T08:10:00Z"));
            task.Claim("qa-user-001", [], task.Version, DateTimeOffset.Parse("2026-07-05T08:20:00Z"));
        }

        var handler = new CreateInspectionRecordFromTaskCommandHandler(
            new InspectionTaskRepository(dbContext),
            new InspectionRecordRepository(dbContext),
            new InspectionPlanRepository(dbContext),
            new NonconformanceReportRepository(dbContext),
            new NonconformanceReportCodeGenerator(),
            dbContext);
        var result = await handler.Handle(
            new CreateInspectionRecordFromTaskCommand(
                task.Id,
                "qa-user-001",
                [
                    lineResult == InspectionLineResults.Passed
                        ? new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])
                        : new InspectionResultLineCommandInput("appearance", "scratch", null, InspectionLineResults.Failed, "外观划伤", 1m, []),
                ],
                lineResult == InspectionLineResults.Passed ? null : "外观划伤",
                [],
                idempotencyKey,
                "org-001",
                "env-dev"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        return result;
    }

    private static InspectionTask OperationTask(
        InspectionPlan plan,
        string workOrderId,
        string operationTaskId,
        string skuCode = "SKU-FG-1000",
        string triggerSuffix = "")
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            QualityInspectionSourceTypes.Operation,
            QualityInspectionSourceServices.Mes,
            workOrderId,
            operationTaskId,
            skuCode,
            10m,
            "pcs",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            $"mes:operation-inspection:{workOrderId}:{operationTaskId}{triggerSuffix}");
    }

    private static InspectionTask ReceivingTask(InspectionPlan plan, string receiptNo, string lineReference)
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            QualityInspectionSourceTypes.Receiving,
            QualityInspectionSourceServices.Wms,
            receiptNo,
            lineReference,
            "SKU-RM-1000",
            10m,
            "kg",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            $"wms:inbound-completed:org-001:env-dev:{receiptNo}:{lineReference}");
    }

    private static InspectionTask FinalTask(InspectionPlan plan, string requestNo, string workOrderId)
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            QualityInspectionSourceTypes.Final,
            QualityInspectionSourceServices.Mes,
            requestNo,
            workOrderId,
            "SKU-FG-3000",
            10m,
            "pcs",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            $"mes:finished-goods-receipt:{requestNo}:{workOrderId}");
    }

    private static InspectionTask PeriodicTask(
        InspectionPlan plan,
        string workOrderId,
        string operationId,
        Guid runtimeContextId,
        long sequence)
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            plan.Id,
            QualityInspectionSourceTypes.Operation,
            QualityInspectionSourceServices.Mes,
            workOrderId,
            PeriodicInspectionSourceLine.LineId(
                operationId,
                PeriodicInspectionSourceLine.TimeKind,
                runtimeContextId,
                sequence),
            "SKU-FG-4000",
            10m,
            "pcs",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            PeriodicInspectionSourceLine.TriggerIdempotencyKey(
                PeriodicInspectionSourceLine.TimeKind,
                runtimeContextId,
                sequence));
    }

    private static InspectionPlan ActivePlan(string planCode, string category, string skuCode)
    {
        var plan = InspectionPlan.Create("org-001", "env-dev", planCode, category, skuCode, null, null, null, null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
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

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
