using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Approvals;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// MES 准入守卫（#3119）：<c>created</c> 工单既不能开工也不能被报工受理。
/// </summary>
public sealed class MesCreatedWorkOrderAdmissionGuardTests
{
    private const string Organization = "org-001";
    private const string Environment = "env-dev";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T08:00:00Z");

    /// <summary>
    /// 开工侧：readiness 给出 <c>WORK_ORDER_NOT_RELEASED</c> 阻断，<c>start</c> 不再出现在允许动作里。
    ///
    /// <para>夹具**其余条件全绿**（齐套快照已证、无缺料、无停机、无质量保留、生产版本已绑、无前序工序），
    /// 所以这条阻断是唯一的那条：断言里 <c>Assert.Equal</c> 整个集合，多一条少一条都红，
    /// 不会被别的阻断顺带兜住。</para>
    /// </summary>
    [Fact]
    public async Task Queued_operation_of_a_created_work_order_is_not_startable()
    {
        await using var dbContext = CreateDbContext();
        var task = AddWorkOrderWithQueuedOperation(dbContext, "WO-CREATED");
        await dbContext.SaveChangesAsync();

        var readiness = await new MesOperationTaskActionReadinessEvaluator(dbContext)
            .EvaluateAsync(task, Now, CancellationToken.None);

        Assert.Equal([MesReadinessReasonCodes.WorkOrderNotReleasedReason], readiness.BlockReasons);
        Assert.Empty(readiness.AllowedActions);
    }

    /// <summary>
    /// 同一张工单一旦下达，那条阻断就消失、<c>start</c> 回来——
    /// 证明上一条用例红的原因是「工单状态」，不是夹具本身缺了别的什么。
    /// </summary>
    [Fact]
    public async Task Releasing_the_work_order_restores_the_start_action()
    {
        await using var dbContext = CreateDbContext();
        var task = AddWorkOrderWithQueuedOperation(dbContext, "WO-RELEASED", release: true);
        await dbContext.SaveChangesAsync();

        var readiness = await new MesOperationTaskActionReadinessEvaluator(dbContext)
            .EvaluateAsync(task, Now, CancellationToken.None);

        Assert.Empty(readiness.BlockReasons);
        Assert.Equal(["start"], readiness.AllowedActions);
    }

    /// <summary>
    /// 开工命令拒绝时给出的是**中文**，英文码由 <c>DescribeForUser</c> 剥掉——
    /// 该文案经分层透传直接上屏，界面不许出现英文错误码。
    /// </summary>
    [Fact]
    public async Task Starting_an_operation_of_a_created_work_order_is_rejected_in_chinese()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrderWithQueuedOperation(dbContext, "WO-CREATED");
        await dbContext.SaveChangesAsync();

        var rejection = await Assert.ThrowsAsync<KnownException>(() =>
            new ChangeOperationTaskStateCommandHandler(dbContext).Handle(
                new ChangeOperationTaskStateCommand(
                    Organization, Environment, "OP-WO-CREATED-10", "start", Now, "start-wo-created-10"),
                CancellationToken.None));

        Assert.Contains("尚未下达", rejection.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MesReadinessReasonCodes.WorkOrderNotReleased, rejection.Message, StringComparison.Ordinal);
        Assert.Equal(
            OperationTaskLifecycleStatus.Queued,
            dbContext.OperationTasks.Single(x => x.OperationTaskIdValue == "OP-WO-CREATED-10").Status);
    }

    /// <summary>
    /// **授权跳站**这第二个开工入口（票面交付范围 2 白纸黑字点名，且要求它**不得**进
    /// <c>nonPreviousBlockReasons</c> 的豁免集）。
    ///
    /// <para>两个开工入口共用同一处 readiness，因此实现上不需要第二处守卫——
    /// 但「共用真的覆盖到了第二个入口」这句话需要它自己的读数。
    /// <c>nonPreviousBlockReasons</c> 是一个**按字符串前缀**做豁免的集合：
    /// 将来任何一次「再豁免一个前缀」或改动本码的措辞，都可能把这一面静默放开，
    /// 而普通开工那条用例会继续绿。</para>
    ///
    /// <para>夹具用「前序工序未完成」这个授权跳站**唯一**成立的前提
    /// （handler 要求存在未完成的前序工序，否则先抛「不存在需要授权跳站的未完前序工序」），
    /// 所以这条用例真的走进了跳站分支，不是被别的守卫提前拦下。</para>
    /// </summary>
    [Fact]
    public async Task Authorized_skip_start_on_a_created_work_order_is_rejected()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrderWithTwoQueuedOperations(dbContext, "WO-CREATED");
        await dbContext.SaveChangesAsync();

        var rejection = await Assert.ThrowsAsync<KnownException>(() => AuthorizeAndStart(dbContext, "OP-WO-CREATED-20"));

        Assert.Contains("尚未下达", rejection.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MesReadinessReasonCodes.WorkOrderNotReleased, rejection.Message, StringComparison.Ordinal);
        Assert.Equal(
            OperationTaskLifecycleStatus.Queued,
            dbContext.OperationTasks.Single(x => x.OperationTaskIdValue == "OP-WO-CREATED-20").Status);
        Assert.Empty(dbContext.OperationTaskStartAuthorizations.Local);
    }

    /// <summary>
    /// 授权跳站的正向对照：同一夹具、工单已下达即放行。
    /// 它证明上一条的红因是工单状态，而不是「授权跳站在这个夹具上本来就走不通」。
    /// </summary>
    [Fact]
    public async Task Authorized_skip_start_succeeds_once_the_work_order_is_released()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrderWithTwoQueuedOperations(dbContext, "WO-RELEASED", release: true);
        await dbContext.SaveChangesAsync();

        var result = await AuthorizeAndStart(dbContext, "OP-WO-RELEASED-20");

        Assert.Equal(OperationTaskLifecycleStatus.InProgress.ToString(), result.Status);
    }

    /// <summary>
    /// 报工侧：守卫落在受理路径（<see cref="RecordProductionReportCommandHandler"/>）而不是
    /// <c>WorkOrder.RecordProductionProgress</c>。
    ///
    /// <para><b>本用例的分辨力就在这里</b>：夹具是**非产出工序**（同工单还有一道更靠后的工序），
    /// 而 <c>RecordProductionProgress</c> 只在产出工序上被调用。守卫若写进域方法，这一次报工会被受理，
    /// 本用例红。#3113 记下的最坏形态正是这一格：非末工序的报工既不翻工单状态、也拿不到任何拒绝。</para>
    /// </summary>
    [Fact]
    public async Task Reporting_a_non_output_operation_of_a_created_work_order_is_rejected()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrderWithQueuedOperation(dbContext, "WO-CREATED");
        var runningTask = OperationTask.Create(
            Organization, Environment, "WO-CREATED", "OP-WO-CREATED-20",
            OperationTaskLifecycleStatus.InProgress, 20, "WC-020", [],
            Now, TimeSpan.FromHours(1), Now, null, "SKU-FG-1000", "EA", 1000m);
        dbContext.OperationTasks.Add(runningTask);
        dbContext.OperationTasks.Add(OperationTask.Create(
            Organization, Environment, "WO-CREATED", "OP-WO-CREATED-30",
            OperationTaskLifecycleStatus.Queued, 30, "WC-030", [],
            Now, TimeSpan.FromHours(1), null, null, "SKU-FG-1000", "EA", 1000m));
        await dbContext.SaveChangesAsync();

        // 前提自检：被报工的这道不是产出工序（后面还有一道序号更大的），
        // 否则本用例会退化成「域方法也能拦住」的那一格、失去针对性。
        Assert.True(await dbContext.OperationTasks.AnyAsync(x =>
            x.WorkOrderId == "WO-CREATED" && x.OperationSequence > runningTask.OperationSequence));

        var rejection = await Assert.ThrowsAsync<KnownException>(() =>
            RecordReport(dbContext, "WO-CREATED", "OP-WO-CREATED-20"));

        Assert.Contains("尚未下达", rejection.Message, StringComparison.Ordinal);
        Assert.Empty(dbContext.ProductionReports);
    }

    /// <summary>同一次报工，工单下达后被受理——证明上一条的红因是工单状态。</summary>
    [Fact]
    public async Task Reporting_the_same_operation_is_accepted_once_the_work_order_is_released()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrderWithQueuedOperation(dbContext, "WO-RELEASED", release: true);
        dbContext.OperationTasks.Add(OperationTask.Create(
            Organization, Environment, "WO-RELEASED", "OP-WO-RELEASED-20",
            OperationTaskLifecycleStatus.InProgress, 20, "WC-020", [],
            Now, TimeSpan.FromHours(1), Now, null, "SKU-FG-1000", "EA", 1000m));
        dbContext.OperationTasks.Add(OperationTask.Create(
            Organization, Environment, "WO-RELEASED", "OP-WO-RELEASED-30",
            OperationTaskLifecycleStatus.Queued, 30, "WC-030", [],
            Now, TimeSpan.FromHours(1), null, null, "SKU-FG-1000", "EA", 1000m));
        await dbContext.SaveChangesAsync();

        await RecordReport(dbContext, "WO-RELEASED", "OP-WO-RELEASED-20");

        Assert.Single(dbContext.ProductionReports.Local);
    }

    /// <summary>
    /// <see cref="WorkOrder.NonExecutableStatuses"/> 保持不含 <c>created</c>。
    /// #3000 的 Quality 投影回填按该集合的**补集**选人，把 <c>created</c> 塞进去会同时改掉那份选人口径
    /// ——两处语义耦合正是本票要避开的（票面已写明）。
    /// </summary>
    [Fact]
    public void Created_stays_out_of_the_non_executable_status_set()
    {
        Assert.DoesNotContain(WorkOrder.CreatedStatus, WorkOrder.NonExecutableStatuses);
    }

    private static Task<MesOperationActionResponse> AuthorizeAndStart(
        ApplicationDbContext dbContext,
        string operationTaskId) =>
        new AuthorizeAndStartOperationTaskCommandHandler(
                dbContext,
                CreatedGuardApprovalClient.Instance,
                new FixedClock(Now))
            .Handle(
                new AuthorizeAndStartOperationTaskCommand(
                    Organization,
                    Environment,
                    operationTaskId,
                    "设备临时故障，先行处理后续工序",
                    "approval-3119-001",
                    "correlation-3119-001",
                    $"skip-start-{operationTaskId}"),
                CancellationToken.None);

    /// <summary>
    /// 授权跳站要求「存在未完成的前序工序」，所以夹具必须有两道 queued 工序。
    /// 其余条件与 <see cref="AddWorkOrderWithQueuedOperation"/> 同样全绿。
    /// </summary>
    private static void AddWorkOrderWithTwoQueuedOperations(
        ApplicationDbContext dbContext,
        string workOrderId,
        bool release = false)
    {
        AddWorkOrderWithQueuedOperation(dbContext, workOrderId, release);
        dbContext.OperationTasks.Add(OperationTask.Create(
            Organization, Environment, workOrderId, $"OP-{workOrderId}-20",
            OperationTaskLifecycleStatus.Queued, 20, "WC-020", [],
            Now, TimeSpan.FromHours(1), null, null, "SKU-FG-1000", "EA", 1000m));
    }

    private sealed class CreatedGuardApprovalClient : IMesOperationTaskStartApprovalClient
    {
        public static CreatedGuardApprovalClient Instance { get; } = new();

        public Task<MesOperationTaskStartApproval?> GetApprovedAsync(
            string approvalChainId,
            string organizationId,
            string environmentId,
            string operationTaskId,
            string workOrderId,
            CancellationToken cancellationToken) =>
            Task.FromResult<MesOperationTaskStartApproval?>(
                new MesOperationTaskStartApproval(approvalChainId, "user:supervisor-3119"));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static Task<ProductionReportCommandResult> RecordReport(
        ApplicationDbContext dbContext,
        string workOrderId,
        string operationTaskId) =>
        new RecordProductionReportCommandHandler(
                dbContext,
                TestProductionReportOeeDimensionSnapshotProvider.Instance,
                TestMesFirstArticleGate.Allowing)
            .Handle(
                new RecordProductionReportCommand(
                    Organization,
                    Environment,
                    workOrderId,
                    operationTaskId,
                    1m,
                    0m,
                    false,
                    Now.AddHours(1),
                    $"report-{operationTaskId}"),
                CancellationToken.None);

    private static OperationTask AddWorkOrderWithQueuedOperation(
        ApplicationDbContext dbContext,
        string workOrderId,
        bool release = false)
    {
        var workOrder = WorkOrder.Create(
            Organization, Environment, workOrderId, "SKU-FG-1000", "PV-FG-1000",
            quantity: 1000m, priority: 1, dueUtc: Now.AddDays(3));
        // 齐套需求为空且快照已证：readiness 的物料那一支全绿，剩下唯一可能的阻断就是工单状态。
        workOrder.RecordMaterialRequirementSnapshot(
            WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus,
            Now.AddHours(-1));
        if (release)
        {
            workOrder.MarkReleased();
        }

        dbContext.WorkOrders.Add(workOrder);
        var task = OperationTask.Create(
            Organization, Environment, workOrderId, $"OP-{workOrderId}-10",
            OperationTaskLifecycleStatus.Queued, 10, "WC-010", [],
            Now, TimeSpan.FromHours(1), null, null, "SKU-FG-1000", "EA", 1000m);
        dbContext.OperationTasks.Add(task);
        return task;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-created-admission-guard-{Guid.CreateVersion7():N}")
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
