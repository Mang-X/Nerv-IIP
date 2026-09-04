using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Messaging.CAP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 直投路径（#3117）的发布时刻口径。工单在 <c>created</c> 状态就能开工报工（#3113），
/// 下达因此可能发生在已有报工之后；发给 Quality 的发布时刻若取下达那一刻，
/// <c>PeriodicInspectionOperation.ApplyRelease</c> 会判「报工早于发布」把整封事件打进死信，
/// 该工单的发布投影永远补不上、首件门禁永久拒。
/// </summary>
public sealed class WorkOrderReleaseFactTimeTests
{
    private const string Organization = "org-001";
    private const string Environment = "env-dev";
    private static readonly DateTimeOffset ReleaseRequestedAtUtc = DateTimeOffset.Parse("2026-09-01T10:00:00Z");

    /// <summary>
    /// 本票的验收路径：先报工、后补下达。发布时刻必须落到**最早**那条报工上，
    /// 不是最晚那条、也不是调用方给的下达时刻。
    /// </summary>
    [Fact]
    public async Task Release_after_production_already_started_dates_the_release_fact_at_the_earliest_report()
    {
        await using var dbContext = CreateDbContext();
        AddReleasableWorkOrder(dbContext, "WO-LATE-RELEASE");
        var earliest = DateTimeOffset.Parse("2026-08-20T06:00:00Z");
        AddReport(dbContext, "RPT-2", "WO-LATE-RELEASE", DateTimeOffset.Parse("2026-08-25T06:00:00Z"));
        AddReport(dbContext, "RPT-1", "WO-LATE-RELEASE", earliest);
        await dbContext.SaveChangesAsync();

        var releasedAtUtc = await ReleaseAsync(dbContext, "WO-LATE-RELEASE");

        Assert.Equal(earliest, releasedAtUtc);
    }

    /// <summary>
    /// 没有报工时不得把发布时刻往前拉：下界只由既有报工构成，调用方给的下达时刻本身是权威的。
    /// </summary>
    [Fact]
    public async Task Release_keeps_the_caller_supplied_moment_when_nothing_was_reported()
    {
        await using var dbContext = CreateDbContext();
        AddReleasableWorkOrder(dbContext, "WO-NO-REPORT");
        await dbContext.SaveChangesAsync();

        var releasedAtUtc = await ReleaseAsync(dbContext, "WO-NO-REPORT");

        Assert.Equal(ReleaseRequestedAtUtc, releasedAtUtc);
    }

    /// <summary>
    /// 取的是「更早者」，不是「有报工就取报工」：报工晚于下达时刻时压到报工上会把发布时刻推后，
    /// 那是凭空改写一个已经确定的业务事实。
    /// </summary>
    [Fact]
    public async Task Release_keeps_the_caller_supplied_moment_when_every_report_is_later()
    {
        await using var dbContext = CreateDbContext();
        AddReleasableWorkOrder(dbContext, "WO-LATE-REPORT");
        AddReport(dbContext, "RPT-LATE", "WO-LATE-REPORT", ReleaseRequestedAtUtc.AddHours(5));
        await dbContext.SaveChangesAsync();

        var releasedAtUtc = await ReleaseAsync(dbContext, "WO-LATE-REPORT");

        Assert.Equal(ReleaseRequestedAtUtc, releasedAtUtc);
    }

    /// <summary>
    /// 下界只能来自**这一张**工单自己的报工。三个对照分别只在工单号、组织、环境上与被测工单不同，
    /// 且都带一条远早的报工：归属谓词少了任何一条合取项，发布时刻都会被拉到 2020 年。
    /// </summary>
    [Fact]
    public async Task Release_never_borrows_another_scopes_earliest_report()
    {
        await using var dbContext = CreateDbContext();
        AddReleasableWorkOrder(dbContext, "WO-TARGET");
        // 对照报工要挂在自己那一套工单与工序行上：production_reports 对两者都有外键，
        // 悬空的对照报工在真实 provider 上根本插不进去（InMemory 不校验外键，会放行一个不可达夹具）。
        AddReleasableWorkOrder(dbContext, "WO-OTHER");
        AddReleasableWorkOrder(dbContext, "WO-TARGET", organizationId: "org-002");
        AddReleasableWorkOrder(dbContext, "WO-TARGET", environmentId: "env-prod");
        var ancient = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        AddReport(dbContext, "RPT-OTHER-WORK-ORDER", "WO-OTHER", ancient);
        AddReport(dbContext, "RPT-OTHER-ORG", "WO-TARGET", ancient, organizationId: "org-002");
        AddReport(dbContext, "RPT-OTHER-ENV", "WO-TARGET", ancient, environmentId: "env-prod");
        await dbContext.SaveChangesAsync();

        var releasedAtUtc = await ReleaseAsync(dbContext, "WO-TARGET");

        Assert.Equal(ReleaseRequestedAtUtc, releasedAtUtc);
    }

    /// <summary>
    /// **完工这一面**（#3117 第三轮补）：工序动作 <c>"complete"</c> 把 `pendingProductionReportNos` 传 `[]`、
    /// 完工时刻取 `ChangedAtUtc`，**不产生任何报工行**。于是「零报工、却已有完工」可达。
    ///
    /// <para>只按报工取下界时，这里查不到任何活动 → 发布事实取调用方时刻（≈now）→ Quality 的
    /// <c>ApplyRelease</c> 第二条守卫（<c>CompletedAtUtc &lt; releasedAtUtc</c>）判冲突、整封进死信，
    /// 工单继续 <c>not-synchronized</c>——与本票要修的缺陷同型，只是换了一面。</para>
    ///
    /// <para>本用例是这一面的鉴别力：夹具**一条报工都没有**，所以它只可能被完工下界杀掉。</para>
    /// </summary>
    [Fact]
    public async Task Release_after_an_operation_already_completed_dates_the_release_fact_at_that_completion()
    {
        await using var dbContext = CreateDbContext();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-18T09:00:00Z");
        AddReleasableWorkOrder(dbContext, "WO-COMPLETED", completedAtUtc: completedAtUtc);
        await dbContext.SaveChangesAsync();

        // 前提自检：这条路径上确实一条报工都没有，否则本用例会被报工下界顺带杀掉、失去针对性。
        Assert.Empty(dbContext.ProductionReports);

        var releasedAtUtc = await ReleaseAsync(dbContext, "WO-COMPLETED");

        Assert.Equal(completedAtUtc, releasedAtUtc);
    }

    /// <summary>
    /// 报工与完工同时存在时取**更早**的那个，不是固定取其中一类。
    /// 两行分别让报工更早、让完工更早，任一侧被漏掉都会红。
    /// </summary>
    [Theory]
    [InlineData("2026-08-15T06:00:00Z", "2026-08-20T09:00:00Z", "2026-08-15T06:00:00Z")]
    [InlineData("2026-08-25T06:00:00Z", "2026-08-18T09:00:00Z", "2026-08-18T09:00:00Z")]
    public async Task Release_dates_the_release_fact_at_the_earliest_of_report_and_completion(
        string reportedAtUtc,
        string completedAtUtc,
        string expected)
    {
        await using var dbContext = CreateDbContext();
        AddReleasableWorkOrder(
            dbContext,
            "WO-BOTH",
            completedAtUtc: DateTimeOffset.Parse(completedAtUtc));
        AddReport(dbContext, "RPT-BOTH", "WO-BOTH", DateTimeOffset.Parse(reportedAtUtc));
        await dbContext.SaveChangesAsync();

        var releasedAtUtc = await ReleaseAsync(dbContext, "WO-BOTH");

        Assert.Equal(DateTimeOffset.Parse(expected), releasedAtUtc);
    }

    /// <summary>
    /// 回执必须回**实际落到发布事实上的**时刻，不回调用方原样给的时刻。
    ///
    /// <para>调用方给 10:00、工单最早报工在 08-20T06:00，发布事实被压到 06:00——
    /// 回执若仍回 10:00，调用方**无从得知自己给的时刻已被改写**。
    /// 这条用例是该主张的唯一鉴别力：变异测试实测，把回执第三参改回 <c>request.ReleasedAtUtc</c>
    /// 在补它之前**全仓零红**（全仓对 <c>MesAcceptedResponse</c> 只有构造、没有一处断言下达回执的时刻字段）。</para>
    /// </summary>
    [Fact]
    public async Task Release_receipt_reports_the_moment_that_actually_landed_on_the_release_fact()
    {
        await using var dbContext = CreateDbContext();
        AddReleasableWorkOrder(dbContext, "WO-RECEIPT");
        var earliest = DateTimeOffset.Parse("2026-08-20T06:00:00Z");
        AddReport(dbContext, "RPT-RECEIPT", "WO-RECEIPT", earliest);
        await dbContext.SaveChangesAsync();

        var response = await new ReleaseWorkOrderCommandHandler(dbContext).Handle(
            new ReleaseWorkOrderCommand(Organization, Environment, "WO-RECEIPT", ReleaseRequestedAtUtc),
            CancellationToken.None);

        // 被夹紧的那一格：回执与调用方给的时刻**必须不同**，否则这条断言退化成同义反复。
        Assert.NotEqual(ReleaseRequestedAtUtc, response.AcceptedAtUtc);
        Assert.Equal(earliest, response.AcceptedAtUtc);
        Assert.Equal("WO-RECEIPT", response.ReferenceId);
    }

    /// <summary>
    /// **「取消也算既有活动」这条明写裁定的鉴别力**（#3117 第四轮 / 复审 R6）。
    ///
    /// <para>命令层注释写「取消也会写 <c>ExistingEndUtc</c>；把它算进来只会把下界往早拉，
    /// 而 Quality 三条守卫都是『既有活动早于发布』才抛，往早拉恒安全，故**不再按状态过滤**」。
    /// 复审实测：加上 <c>Status == Completed</c> 过滤后**全仓零红**——也就是这条裁定此前**没有任何用例盯着**，
    /// 而当时看似有防线的那点红，只是因为夹具用了聚合造不出的 <c>InProgress + 非空 ExistingEndUtc</c>
    /// 组合（假鉴别力）。本用例把这条裁定钉住，夹具全程走**聚合方法**（Queue → Cancel），域上可达。</para>
    /// </summary>
    [Fact]
    public async Task Release_treats_a_cancelled_operations_end_time_as_existing_activity()
    {
        await using var dbContext = CreateDbContext();
        var cancelledAtUtc = DateTimeOffset.Parse("2026-08-12T04:00:00Z");
        AddReleasableWorkOrder(dbContext, "WO-CANCELLED");

        // 走聚合方法造出取消态：Queue 之后 Cancel，ExistingEndUtc 被置为取消时刻。
        var cancelled = OperationTask.Queue(
            Organization,
            Environment,
            "WO-CANCELLED",
            "OP-WO-CANCELLED-20",
            20,
            "WC-MIX",
            [],
            ReleaseRequestedAtUtc.AddDays(-20),
            TimeSpan.FromHours(1),
            "SKU-FG-1000",
            "EA",
            1000m);
        cancelled.Cancel(cancelledAtUtc);
        dbContext.OperationTasks.Add(cancelled);
        await dbContext.SaveChangesAsync();

        // 前提自检：取消确实写了 ExistingEndUtc，且状态是 Cancelled——否则本用例测的不是它声称的那件事。
        Assert.Equal(OperationTaskLifecycleStatus.Cancelled, cancelled.Status);
        Assert.Equal(cancelledAtUtc, cancelled.ExistingEndUtc);
        Assert.Empty(dbContext.ProductionReports);

        var releasedAtUtc = await ReleaseAsync(dbContext, "WO-CANCELLED");

        Assert.Equal(cancelledAtUtc, releasedAtUtc);
    }

    /// <summary>
    /// **第二条信任边界的接线**（#3117 第三轮补 / 复审 E1·Q4）。
    ///
    /// <para>`UntrustedCandidate` 这个方法本身有防线（`MesAggregateTests` 两行 theory + 端点侧 3 行），
    /// 但「NCR 返工消费者**确实调了它**」此前由**零条**用例盯着——审核把该调用点的包裹整个删掉，
    /// 全仓零红。把守卫抽成共享方法之后，**每个调用点是否真的接上，是一条独立的、必须各自承重的事实**。</para>
    ///
    /// <para>本用例走真实 handler：载荷带**未来** `RequestedAtUtc`，断言落到
    /// `WorkOrderReleasedDomainEvent.ReleasedAt` 的时刻被夹到服务端时钟。
    /// 不夹的话，该返工工序此后的每一条报工都会被 Quality 判「报工早于发布」进死信。</para>
    /// </summary>
    [Fact]
    public async Task Rework_consumer_clamps_a_future_payload_moment_to_the_server_clock()
    {
        var nowUtc = DateTimeOffset.Parse("2026-08-29T09:00:00Z");
        var futureRequestedAtUtc = nowUtc.AddDays(7);
        await using var provider = CreateReworkProvider();
        await NcrReworkRequestedPostgresFixtures.SeedSourceAsync(provider, Organization, Environment);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var handler = new NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder(
                db,
                new MesCodingService(),
                new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db),
                new NoRequirementSnapshotProvider(),
                new PassThroughReworkScopeCoordinator(),
                new FakeTimeProvider(nowUtc));

            await handler.HandleAsync(
                NcrReworkRequestedPostgresFixtures.CreateEvent(requestedAtUtc: futureRequestedAtUtc),
                CancellationToken.None);

            // 前提自检：这条路径必须真的走通到「建出返工工单」，没有被任何守卫提前死信掉；
            // 否则下面的断言会在一个根本没执行到夹紧那一行的场景上空转。
            Assert.Empty(await new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db).ListAsync(
                NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None));

            // 消费者按 netcorepal 姿势不自行 SaveChanges，返工工单此刻只在变更跟踪器里；
            // 领域事件也挂在这个被跟踪的实例上，所以从 ChangeTracker 取而不是查库。
            var rework = db.ChangeTracker.Entries<WorkOrder>()
                .Select(entry => entry.Entity)
                .Single(x => x.WorkOrderType == WorkOrder.ReworkType);
            var released = Assert.IsType<WorkOrderReleasedDomainEvent>(
                Assert.Single(rework.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent));

            // 夹到服务端时钟，而不是载荷给的未来时刻。两条断言方向相反，删掉夹紧时第一条红。
            Assert.Equal(nowUtc, released.ReleasedAt.Value);
            Assert.NotEqual(futureRequestedAtUtc, released.ReleasedAt.Value);
        }
    }

    private static ServiceProvider CreateReworkProvider()
    {
        // 库名必须在 lambda **外面**算：放进 lambda 会让每个 scope 各建一个 InMemory 库，
        // 于是 seed 与 handler 看到的是两个空间不同的库（实测症状是 sourceDefectMissing 死信）。
        var databaseName = $"mes-rework-clamp-{Guid.CreateVersion7():N}";
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, NoopMediator>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private sealed class PassThroughReworkScopeCoordinator : IMesReworkWorkOrderScopeCoordinator
    {
        public Task ExecuteAsync(
            string organizationId,
            string environmentId,
            string ncrId,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }

    /// <summary>下达并取回发给 Quality 的那份发布事实的时刻。</summary>
    private static async Task<DateTimeOffset> ReleaseAsync(ApplicationDbContext dbContext, string workOrderId)
    {
        await new ReleaseWorkOrderCommandHandler(dbContext).Handle(
            new ReleaseWorkOrderCommand(Organization, Environment, workOrderId, ReleaseRequestedAtUtc),
            CancellationToken.None);

        var workOrder = await dbContext.WorkOrders.SingleAsync(x =>
            x.OrganizationId == Organization
            && x.EnvironmentId == Environment
            && x.WorkOrderIdValue == workOrderId);
        var domainEvent = Assert.IsType<WorkOrderReleasedDomainEvent>(
            Assert.Single(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent));
        return new WorkOrderReleasedIntegrationEventConverter().Convert(domainEvent).Payload.ReleasedAtUtc;
    }

    /// <summary>
    /// 「计划转工单」留下的形态：工单仍是 <c>created</c>，工序已经建出并在制，齐套已证——
    /// 这正是 #3113 实测走通的那条主流程，也是唯一能在有报工之后再下达的形态。
    /// </summary>
    private static void AddReleasableWorkOrder(
        ApplicationDbContext dbContext,
        string workOrderId,
        string organizationId = Organization,
        string environmentId = Environment,
        DateTimeOffset? completedAtUtc = null)
    {
        dbContext.WorkOrders.Add(WorkOrder.Create(
            organizationId, environmentId, workOrderId, "SKU-FG-1000", "PV-FG-1000",
            quantity: 1000m, priority: 1, dueUtc: ReleaseRequestedAtUtc.AddDays(3)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            organizationId,
            environmentId,
            workOrderId,
            $"OP-{workOrderId}-10",
            // 状态必须与 ExistingEndUtc 一致：聚合造不出「InProgress + 非空 ExistingEndUtc」的组合
            // （Complete() 同时置 Completed 与 ExistingEndUtc；Start()/Resume() 把 ExistingEndUtc 置 null）。
            // 拿域造不出的状态当承重夹具，会让「按状态过滤」这类变异因为夹具不可达而假红。
            completedAtUtc is null ? OperationTaskLifecycleStatus.InProgress : OperationTaskLifecycleStatus.Completed,
            10,
            "WC-MIX",
            [],
            ReleaseRequestedAtUtc.AddDays(-20),
            TimeSpan.FromHours(1),
            completedAtUtc is null ? null : ReleaseRequestedAtUtc.AddDays(-20),
            completedAtUtc,
            "SKU-FG-1000",
            "EA",
            1000m));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            organizationId,
            environmentId,
            workOrderId,
            $"OP-{workOrderId}-10",
            "MAT-OIL",
            null,
            requiredQuantity: 10m,
            availableQuantity: 10m,
            stagedQuantity: 0m,
            sourceSystem: "Inventory",
            sourceSnapshotId: $"inv-ready-{workOrderId}",
            capturedAtUtc: ReleaseRequestedAtUtc.AddDays(-20),
            substituteMaterialIds: []));
    }

    private static void AddReport(
        ApplicationDbContext dbContext,
        string reportNo,
        string workOrderId,
        DateTimeOffset reportedAtUtc,
        string organizationId = Organization,
        string environmentId = Environment) =>
        dbContext.ProductionReports.Add(ProductionReport.Record(
            organizationId,
            environmentId,
            reportNo,
            workOrderId,
            $"OP-{workOrderId}-10",
            goodQuantity: 3m,
            scrapQuantity: 0m,
            completesOperation: false,
            reportedAtUtc: reportedAtUtc));

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-release-fact-time-{Guid.CreateVersion7():N}")
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
