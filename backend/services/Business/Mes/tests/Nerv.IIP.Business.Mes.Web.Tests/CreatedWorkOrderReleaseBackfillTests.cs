using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.Remediation;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Testing;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// <c>created</c> 存量工单一次性补下达（#3119）的选人判据、发布事实时刻与重跑行为。
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class CreatedWorkOrderReleaseBackfillTests
{
    private const string Organization = "org-001";
    private const string Environment = "env-dev";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T00:00:00Z");

    /// <summary>
    /// 选人判据：<c>created</c> ∧ 至少一道工序已有执行事实（InProgress / Paused / Completed）。
    ///
    /// <para>两侧都要有对照：<b>选中侧</b>覆盖三种执行态；<b>不选侧</b>既有「还没开工的 created 工单」
    /// （工序全 Queued / ScheduleInvalidated / 没有工序），也有「已经下达过的工单」。
    /// 不选侧不是可有可无的装饰——票面原口径是「有非 Cancelled 工序」，那个口径会把
    /// <c>WO-QUEUED-ONLY</c> 这类**正等着人工下达**的工单也强制发布，替用户绕过三道 readiness。</para>
    /// </summary>
    [Fact]
    public async Task Only_created_work_orders_that_already_carry_execution_facts_are_released()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-CREATED-RUNNING", Created, [(OperationTaskLifecycleStatus.InProgress, 10)]);
        AddWorkOrder(dbContext, "WO-CREATED-PAUSED", Created, [(OperationTaskLifecycleStatus.Paused, 10)]);
        AddWorkOrder(dbContext, "WO-CREATED-DONE", Created, [(OperationTaskLifecycleStatus.Completed, 10)]);
        AddWorkOrder(dbContext, "WO-QUEUED-ONLY", Created, [(OperationTaskLifecycleStatus.Queued, 10)]);
        AddWorkOrder(dbContext, "WO-SCHEDULE-INVALIDATED", Created, [(OperationTaskLifecycleStatus.ScheduleInvalidated, 10)]);
        AddWorkOrder(dbContext, "WO-NO-OPERATIONS", Created, []);
        AddWorkOrder(dbContext, "WO-ALREADY-RELEASED", Released, [(OperationTaskLifecycleStatus.InProgress, 10)]);
        AddWorkOrder(dbContext, "WO-STARTED", Started, [(OperationTaskLifecycleStatus.InProgress, 10)]);
        await dbContext.SaveChangesAsync();

        var report = await Backfill(dbContext);

        // 扫描面是全部 created 工单（6 张），选中面是其中带执行事实的 3 张。
        Assert.Equal(6, report.CreatedWorkOrdersScanned);
        Assert.Equal(3, report.WorkOrdersReleased);
        Assert.Equal(3, report.OperationsReleased);
        // InProgress + Paused = 2；Completed 不计入「此刻在跑」，它对应判定 SQL 之外的那一半。
        Assert.Equal(2, report.ExecutingOperationsRemediated);
        Assert.Equal(
            new[] { "WO-CREATED-DONE", "WO-CREATED-PAUSED", "WO-CREATED-RUNNING" },
            ReleasedWorkOrderIds(dbContext));
        Assert.Equal(
            WorkOrder.CreatedStatus,
            dbContext.WorkOrders.Single(x => x.WorkOrderIdValue == "WO-QUEUED-ONLY").Status);
        Assert.Equal(
            WorkOrder.CreatedStatus,
            dbContext.WorkOrders.Single(x => x.WorkOrderIdValue == "WO-SCHEDULE-INVALIDATED").Status);
        Assert.Equal(
            WorkOrder.CreatedStatus,
            dbContext.WorkOrders.Single(x => x.WorkOrderIdValue == "WO-NO-OPERATIONS").Status);
    }

    /// <summary>
    /// 补下达携带该工单的**全部**工序，不只是在跑的那几道：发布事实是工单维度的一件事，
    /// Quality 的 <c>PeriodicInspectionOperations</c> 按载荷里的 operations 建行，
    /// 漏掉尚未开工的兄弟工序会让它们此后第一次报工就读到 not-synchronized。
    /// </summary>
    [Fact]
    public async Task Released_payload_carries_every_operation_of_the_selected_work_order()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(
            dbContext,
            "WO-MIXED",
            Created,
            // 乱序插入：断言里的顺序只有在载荷真的排过序时才成立。
            [
                (OperationTaskLifecycleStatus.Queued, 30),
                (OperationTaskLifecycleStatus.InProgress, 10),
                (OperationTaskLifecycleStatus.Queued, 20),
            ]);
        await dbContext.SaveChangesAsync();

        var report = await Backfill(dbContext);

        Assert.Equal(1, report.WorkOrdersReleased);
        Assert.Equal(3, report.OperationsReleased);
        var integrationEvent = SingleReleasedIntegrationEvent(dbContext, "WO-MIXED");
        Assert.Equal(
            new[] { 10, 20, 30 },
            integrationEvent.Payload.Operations.Select(x => x.OperationSequence).ToArray());
    }

    /// <summary>
    /// 发布事实时刻按最早既有活动取下界，报工与工序完工两条**各自**都要压。
    ///
    /// <para><b>本用例的强度边界，如实写</b>：它只覆盖「某一条下界项**被删掉**」这一族变异
    /// （两张工单各自只带一条下界项：一张只有报工、一张只有完工、零报工）。
    /// 它**不覆盖**「两条下界项同时存在时怎么合并」——那条规则由
    /// <see cref="Release_fact_time_takes_the_earlier_of_report_and_completion_when_both_exist"/> 承担。
    /// 上一版这里写的是「删掉任何一条下界的取值都不一样」，读起来像把合并规则也包了进来，
    /// 而把 <c>min</c> 的比较符取反时全仓零红——那是护栏自称完备。</para>
    /// </summary>
    [Fact]
    public async Task Release_fact_time_is_pushed_down_to_the_earliest_report_or_completion()
    {
        await using var dbContext = CreateDbContext();
        var earliestCompletion = DateTimeOffset.Parse("2026-08-10T03:00:00Z");
        var earliestReport = DateTimeOffset.Parse("2026-08-12T03:00:00Z");

        AddWorkOrder(dbContext, "WO-REPORT-EARLIEST", Created, [(OperationTaskLifecycleStatus.InProgress, 10)]);
        dbContext.ProductionReports.Add(ProductionReport.Record(
            Organization, Environment, "RPT-EARLIEST", "WO-REPORT-EARLIEST", "OP-WO-REPORT-EARLIEST-10",
            goodQuantity: 1m, scrapQuantity: 0m, completesOperation: false, reportedAtUtc: earliestReport));
        dbContext.ProductionReports.Add(ProductionReport.Record(
            Organization, Environment, "RPT-LATER", "WO-REPORT-EARLIEST", "OP-WO-REPORT-EARLIEST-10",
            goodQuantity: 1m, scrapQuantity: 0m, completesOperation: false,
            reportedAtUtc: earliestReport.AddDays(3)));

        // 零报工、却已有完工：工序动作 "complete" 不产生任何报工行，
        // 只按报工取下界时这张工单会拿到「晚于完工」的发布时刻，被 Quality 的完工守卫整封打进死信。
        var completedTask = OperationTask.Queue(
            Organization, Environment, "WO-COMPLETION-ONLY", "OP-WO-COMPLETION-ONLY-10",
            10, "WC-010", [], Now, TimeSpan.FromHours(1), "SKU-FG-1000", "EA", 1000m);
        completedTask.Start(earliestCompletion.AddHours(-1));
        completedTask.Complete(earliestCompletion, []);
        dbContext.WorkOrders.Add(NewWorkOrder("WO-COMPLETION-ONLY"));
        dbContext.OperationTasks.Add(completedTask);
        await dbContext.SaveChangesAsync();

        // 前提自检：候选（工序建单时刻）确实晚于两条下界，否则下界不绑定、删掉它也照绿。
        Assert.True(completedTask.ExistingEndUtc < completedTask.CreatedAtUtc);
        Assert.Empty(dbContext.ProductionReports.Where(x => x.WorkOrderId == "WO-COMPLETION-ONLY"));

        await Backfill(dbContext);

        Assert.Equal(
            earliestReport,
            SingleReleasedIntegrationEvent(dbContext, "WO-REPORT-EARLIEST").Payload.ReleasedAtUtc);
        Assert.Equal(
            earliestCompletion,
            SingleReleasedIntegrationEvent(dbContext, "WO-COMPLETION-ONLY").Payload.ReleasedAtUtc);
    }

    /// <summary>
    /// **两条下界项同时存在时取更早的那个**（#3144 复审阻断 E1）。
    ///
    /// <para>这条规则此前零覆盖：既有夹具里报工与工序完工**从不共存**
    /// （有报工的那张工序是 <c>InProgress</c> 故 <c>ExistingEndUtc</c> 为空；有完工的那张零报工），
    /// 于是 <c>min</c> 那一步从未被求值检验，把比较符取反全仓 1080 条无一变红。
    /// 它可达：多工序工单 OP-10 走工序动作 "complete"（不落报工行）、OP-20 随后报工，就是这一格。</para>
    ///
    /// <para><b>两个方向都要在场</b>，否则只杀得掉比较符的一侧：
    /// 一张工单完工早于报工（期望取完工），另一张报工早于完工（期望取报工）。</para>
    /// </summary>
    [Fact]
    public async Task Release_fact_time_takes_the_earlier_of_report_and_completion_when_both_exist()
    {
        await using var dbContext = CreateDbContext();
        var early = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var late = DateTimeOffset.Parse("2026-08-20T00:00:00Z");

        AddWorkOrderWithCompletionAndReport(dbContext, "WO-DONE-FIRST", completedAtUtc: early, reportedAtUtc: late);
        AddWorkOrderWithCompletionAndReport(dbContext, "WO-REPORT-FIRST", completedAtUtc: late, reportedAtUtc: early);
        await dbContext.SaveChangesAsync();

        // 前提自检：两张工单都**同时**拥有两条下界项，否则本用例退化成上一条的等价输入。
        foreach (var workOrderId in new[] { "WO-DONE-FIRST", "WO-REPORT-FIRST" })
        {
            Assert.Contains(
                dbContext.OperationTasks.Local,
                x => x.WorkOrderId == workOrderId && x.ExistingEndUtc.HasValue);
            Assert.Contains(dbContext.ProductionReports.Local, x => x.WorkOrderId == workOrderId);
        }

        await Backfill(dbContext);

        Assert.Equal(early, SingleReleasedIntegrationEvent(dbContext, "WO-DONE-FIRST").Payload.ReleasedAtUtc);
        Assert.Equal(early, SingleReleasedIntegrationEvent(dbContext, "WO-REPORT-FIRST").Payload.ReleasedAtUtc);
    }

    /// <summary>重跑：工单已翻 <c>released</c>，第二次既不在扫描面里、也不再发第二封发布事实。</summary>
    [Fact]
    public async Task Rerunning_the_backfill_changes_nothing()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-CREATED-RUNNING", Created, [(OperationTaskLifecycleStatus.InProgress, 10)]);
        AddWorkOrder(dbContext, "WO-QUEUED-ONLY", Created, [(OperationTaskLifecycleStatus.Queued, 10)]);
        await dbContext.SaveChangesAsync();

        var first = await Backfill(dbContext);
        // 复刻真实的两次调用：命令跑在 UoW 里，第一次的状态变更提交之后第二次才发生。
        // 不提交就跑第二遍不是「重跑」，那是「同一个变更跟踪器里再来一遍」。
        await dbContext.SaveChangesAsync();
        // 第一轮的领域事件在真实 UoW 里会被派发后清掉；这里显式清掉，
        // 第二轮若又发一封，下面的断言就会看到它。
        foreach (var workOrder in dbContext.WorkOrders.Local)
        {
            workOrder.ClearDomainEvents();
        }

        var second = await Backfill(dbContext);

        Assert.Equal(1, first.WorkOrdersReleased);
        Assert.Equal(2, first.CreatedWorkOrdersScanned);
        Assert.Equal(0, second.WorkOrdersReleased);
        Assert.Equal(0, second.OperationsReleased);
        Assert.Equal(0, second.ExecutingOperationsRemediated);
        // 第二轮仍会扫到那张始终留在 created 的工单——扫描面不变，动作面为空。
        Assert.Equal(1, second.CreatedWorkOrdersScanned);
        Assert.Empty(dbContext.WorkOrders.Local.SelectMany(x => x.GetDomainEvents()));
    }

    /// <summary>
    /// ⛔ 硬约束：补救**不能**复用 <c>ReleaseWorkOrderCommand</c>。
    /// 那条路径要过设备 readiness，而设备停机正是这批工单当初没被下达的原因之一
    /// ——用它补救等于必然拒掉要救的人。同一张工单：直投路径抛 KnownException，补救路径照样把它救出来。
    /// </summary>
    [Fact]
    public async Task Backfill_releases_a_work_order_that_the_readiness_guarded_release_command_rejects()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-DOWNTIME", Created, [(OperationTaskLifecycleStatus.InProgress, 10)]);
        dbContext.WorkCenterUnavailabilities.Add(WorkCenterUnavailability.Open(
            Organization, Environment, "DT-3119-01", "WC-010",
            Now.AddDays(-1), null, EquipmentRuntimeReasonCodes.Downtime, "ASSET-010"));
        await dbContext.SaveChangesAsync();

        var rejection = await Assert.ThrowsAsync<KnownException>(() =>
            new ReleaseWorkOrderCommandHandler(dbContext).Handle(
                new ReleaseWorkOrderCommand(Organization, Environment, "WO-DOWNTIME", Now),
                CancellationToken.None));
        Assert.Contains(EquipmentRuntimeReasonCodes.Downtime, rejection.Message, StringComparison.Ordinal);
        Assert.Equal(
            WorkOrder.CreatedStatus,
            dbContext.WorkOrders.Single(x => x.WorkOrderIdValue == "WO-DOWNTIME").Status);

        var report = await Backfill(dbContext);

        Assert.Equal(1, report.WorkOrdersReleased);
        Assert.Equal(new[] { "WO-DOWNTIME" }, ReleasedWorkOrderIds(dbContext));
    }

    /// <summary>
    /// 续扫查询必须能被真实 provider 翻译，且翻出来的是 keyset seek。EF Core InMemory 不做翻译：
    /// 不可翻译的谓词会被它客户端求值放行，退化成 OFFSET 也照样绿，两种都要到生产库才炸/才漏。
    /// 这里用 Npgsql 的 <c>ToQueryString()</c>（只生成 SQL、不连库）钉住生产代码那条查询本身。
    /// </summary>
    [Fact]
    public void Page_query_translates_to_a_keyset_seek_on_the_real_provider()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using var dbContext = new ApplicationDbContext(options, new NoopMediator());

        var firstPage = BackfillCreatedWorkOrderReleaseCommandHandler
            .BuildPageQuery(dbContext, null, null, null)
            .ToQueryString();
        var nextPage = BackfillCreatedWorkOrderReleaseCommandHandler
            .BuildPageQuery(dbContext, Organization, Environment, "WO-0199")
            .ToQueryString();

        Assert.Contains("= 'created'", firstPage, StringComparison.Ordinal);
        Assert.Contains("ORDER BY w.organization_id, w.environment_id, w.work_order_id", nextPage, StringComparison.Ordinal);
        Assert.Contains("w.work_order_id > ", nextPage, StringComparison.Ordinal);
        Assert.DoesNotContain("OFFSET", firstPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OFFSET", nextPage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 端点这一面（#3144 复审阻断 3）。逐条对齐 canonical
    /// <c>WorkOrderReleaseProjectionBackfillTests.Backfill_endpoint_is_reachable_only_with_an_internal_service_token</c>：
    /// 匿名 401、带内部令牌 200、外加**真实 handler 是否被 MediatR 发现**的注册探针。
    ///
    /// <para>注册探针不是可选项：端点走 <see cref="ISender"/>，本用例把它换成了桩，
    /// 于是「命令与 handler 都是 <c>internal</c>」这类可见性/注册改动会让端点在运行时找不到 handler
    /// 而 HTTP 那两条断言照绿。本命令与 handler <b>恰恰都是 <c>internal</c></b>
    /// （<c>CreatedWorkOrderReleaseBackfill.cs</c>），正是 canonical 那条注释点名警告的形态。</para>
    ///
    /// <para>这个端点的能力是**绕开设备/质量/齐套三道 readiness 批量强制发布工单**，
    /// 并对每一张发出 <c>WorkOrderReleasedIntegrationEvent</c>。
    /// 少了这条用例，删掉 <c>Policies(...)</c> 一行不会让任何门禁红。</para>
    /// </summary>
    [Fact]
    public async Task Backfill_endpoint_is_reachable_only_with_an_internal_service_token()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new CreatedReleaseBackfillReportSender());
                });
            });
        using var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);

        using var anonymous = await client.PostAsync(
            "/internal/business-mes/v1/created-work-order-release-backfill",
            content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        using var response = await client.PostAsync(
            "/internal/business-mes/v1/created-work-order-release-backfill",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(factory.Services
            .GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(IRequestHandler<
                BackfillCreatedWorkOrderReleaseCommand,
                CreatedWorkOrderReleaseBackfillReport>)));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(11, body.RootElement.GetProperty("createdWorkOrdersScanned").GetInt32());
        Assert.Equal(3, body.RootElement.GetProperty("workOrdersReleased").GetInt32());
        Assert.Equal(7, body.RootElement.GetProperty("operationsReleased").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("executingOperationsRemediated").GetInt32());
    }

    private static async Task<CreatedWorkOrderReleaseBackfillReport> Backfill(ApplicationDbContext dbContext) =>
        await new BackfillCreatedWorkOrderReleaseCommandHandler(dbContext).Handle(
            new BackfillCreatedWorkOrderReleaseCommand(),
            CancellationToken.None);

    /// <summary>
    /// 按「本次补救发出的发布事实」点名，不按 <c>Status == released</c>：
    /// 夹具里本来就有已下达的对照工单，按状态数会把它们一起数进来，
    /// 于是「补救多救了谁 / 少救了谁」这条断言反而失去鉴别力。
    /// 夹具在 <see cref="AddWorkOrder"/> 里已清掉建单与既有发布留下的领域事件。
    /// </summary>
    private static string[] ReleasedWorkOrderIds(ApplicationDbContext dbContext) =>
        dbContext.WorkOrders.Local
            .Where(x => x.GetDomainEvents().Any(e => e is WorkOrderReleasedDomainEvent))
            .Select(x => x.WorkOrderIdValue)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static WorkOrderReleasedIntegrationEvent SingleReleasedIntegrationEvent(
        ApplicationDbContext dbContext,
        string workOrderId)
    {
        var workOrder = dbContext.WorkOrders.Local.Single(x => x.WorkOrderIdValue == workOrderId);
        var domainEvent = Assert.IsType<WorkOrderReleasedDomainEvent>(
            Assert.Single(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent));
        return new WorkOrderReleasedIntegrationEventConverter().Convert(domainEvent);
    }

    /// <summary>
    /// 一张 <c>created</c> 工单，**同时**带一道走聚合方法完工的工序（不落报工行）与一条报工。
    /// 完工走 <c>Queue → Start → Complete</c>，是域可达状态，不是手搓的
    /// 「InProgress + 非空 ExistingEndUtc」。
    /// </summary>
    private static void AddWorkOrderWithCompletionAndReport(
        ApplicationDbContext dbContext,
        string workOrderId,
        DateTimeOffset completedAtUtc,
        DateTimeOffset reportedAtUtc)
    {
        dbContext.WorkOrders.Add(NewWorkOrder(workOrderId));
        var completed = OperationTask.Queue(
            Organization, Environment, workOrderId, $"OP-{workOrderId}-10",
            10, "WC-010", [], Now, TimeSpan.FromHours(1), "SKU-FG-1000", "EA", 1000m);
        completed.Start(completedAtUtc.AddHours(-1));
        completed.Complete(completedAtUtc, []);
        dbContext.OperationTasks.Add(completed);
        dbContext.OperationTasks.Add(OperationTask.Create(
            Organization, Environment, workOrderId, $"OP-{workOrderId}-20",
            OperationTaskLifecycleStatus.InProgress, 20, "WC-020", [],
            Now, TimeSpan.FromHours(1), Now, null, "SKU-FG-1000", "EA", 1000m));
        dbContext.ProductionReports.Add(ProductionReport.Record(
            Organization, Environment, $"RPT-{workOrderId}", workOrderId, $"OP-{workOrderId}-20",
            goodQuantity: 1m, scrapQuantity: 0m, completesOperation: false, reportedAtUtc: reportedAtUtc));
    }

    private static void Created(WorkOrder workOrder)
    {
    }

    private static void Released(WorkOrder workOrder) => workOrder.MarkReleased();

    private static void Started(WorkOrder workOrder)
    {
        workOrder.MarkReleased();
        workOrder.Start(Now);
    }

    private static WorkOrder NewWorkOrder(string workOrderId) => WorkOrder.Create(
        Organization, Environment, workOrderId, "SKU-FG-1000", "PV-FG-1000",
        quantity: 1000m, priority: 1, dueUtc: Now.AddDays(3));

    private static void AddWorkOrder(
        ApplicationDbContext dbContext,
        string workOrderId,
        Action<WorkOrder> advanceToStatus,
        IReadOnlyCollection<(OperationTaskLifecycleStatus Status, int Sequence)> operations)
    {
        var workOrder = NewWorkOrder(workOrderId);
        advanceToStatus(workOrder);
        workOrder.ClearDomainEvents();
        dbContext.WorkOrders.Add(workOrder);
        foreach (var (operationStatus, sequence) in operations)
        {
            dbContext.OperationTasks.Add(OperationTask.Create(
                Organization, Environment, workOrderId, $"OP-{workOrderId}-{sequence}",
                operationStatus, sequence, $"WC-{sequence:D3}", [],
                Now, TimeSpan.FromHours(1), null, null, "SKU-FG-1000", "EA", 1000m));
        }
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-created-release-backfill-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class CreatedReleaseBackfillReportSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = Assert.IsType<BackfillCreatedWorkOrderReleaseCommand>(request);
            return Task.FromResult((TResponse)(object)new CreatedWorkOrderReleaseBackfillReport(11, 3, 7, 2));
        }

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
