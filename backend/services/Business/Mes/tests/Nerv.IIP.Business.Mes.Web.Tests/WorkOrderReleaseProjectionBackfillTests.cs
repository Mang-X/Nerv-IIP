using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 存量在制工单发布投影回填（#3000）的选取判据与补投载荷。
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WorkOrderReleaseProjectionBackfillTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T00:00:00Z");

    [Fact]
    public async Task Only_work_orders_whose_release_already_happened_and_still_have_live_operations_are_backfilled()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-RELEASED", Released);
        AddWorkOrder(dbContext, "WO-STARTED", Started);
        AddWorkOrder(dbContext, "WO-HOLD", workOrder => { Started(workOrder); workOrder.Hold("待料"); });
        AddWorkOrder(dbContext, "WO-CREATED", static _ => { });
        AddWorkOrder(dbContext, "WO-COMPLETED", Completed);
        AddWorkOrder(dbContext, "WO-CLOSED", workOrder => { Completed(workOrder); workOrder.Close(Now); });
        AddWorkOrder(dbContext, "WO-CANCELLED", static workOrder => workOrder.Cancel("客户取消", Now));
        AddWorkOrder(dbContext, "WO-SPLIT", workOrder => { Released(workOrder); workOrder.MarkSplit(); });
        AddWorkOrder(dbContext, "WO-MERGED", workOrder => { Released(workOrder); workOrder.MarkMerged(); });
        // 全部工序都已取消：Cancelled 无回流路径，工序不会再复活，也就没有需要补的发布事实。
        AddWorkOrder(
            dbContext,
            "WO-ALL-CANCELLED",
            Started,
            [
                (OperationTaskLifecycleStatus.Cancelled, 10),
                (OperationTaskLifecycleStatus.Cancelled, 20),
            ]);
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var report = await CreateHandler(dbContext, publisher).Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        // completed 工单在内：报工命令的准入只看工序 InProgress，工单状态不参与判断，
        // 而 RecordProductionProgress 不拒 completed（超收容差为「已达量后继续报工」留了空间）。
        Assert.Equal(5, report.WorkOrdersScanned);
        Assert.Equal(4, report.WorkOrdersPublished);
        Assert.Equal(
            new[] { "WO-COMPLETED", "WO-HOLD", "WO-RELEASED", "WO-STARTED" },
            publisher.Published.Select(x => x.Payload.WorkOrderId).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// S1 的会失败状态：末工序报满计划量后工单被 <c>RecordProductionProgress</c> 自动翻成 completed，
    /// 工序仍是 InProgress、还能继续报工（尾数/报废/完工那几笔），因此仍会撞首件门禁。
    /// 判据一旦改回工单状态白名单，这张工单被整张跳过、门禁永久拒。
    /// </summary>
    [Fact]
    public async Task Work_order_that_reached_planned_quantity_is_still_backfilled_while_its_operation_runs()
    {
        await using var dbContext = CreateDbContext();
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-AT-QUANTITY", "SKU-FG-1000", null,
            quantity: 100m, priority: 1, dueUtc: Now.AddDays(3));
        workOrder.MarkReleased();
        workOrder.Start(Now);
        var task = OperationTask.Create(
            "org-001", "env-dev", "WO-AT-QUANTITY", "OP-AT-QUANTITY-10",
            OperationTaskLifecycleStatus.InProgress, 10, "WC-010", [],
            Now, TimeSpan.FromHours(1), null, null, "SKU-FG-1000", "EA", 100m);
        dbContext.OperationTasks.Add(task);
        dbContext.WorkOrders.Add(workOrder);
        // 报满计划量：工单翻 completed，工序不动。
        workOrder.RecordProductionProgress(goodQuantity: 100m, scrapQuantity: 0m, reportedAtUtc: Now);
        await dbContext.SaveChangesAsync();
        Assert.Equal(WorkOrder.CompletedStatus, workOrder.Status);
        Assert.Equal(OperationTaskLifecycleStatus.InProgress, task.Status);

        var publisher = new RecordingPublisher();
        await CreateHandler(dbContext, publisher).Handle(
            new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal("WO-AT-QUANTITY", published.Payload.WorkOrderId);
        Assert.Equal("OP-AT-QUANTITY-10", Assert.Single(published.Payload.Operations).OperationId);
    }

    /// <summary>
    /// 工序过滤只排除 <c>Cancelled</c>。<c>Completed</c> 必须在内：它不是终态——
    /// 报工冲销经 <c>ReopenAfterReportReversal</c> 把它改回 <c>InProgress</c>
    /// （该转换本身由 <c>OperationActualTimeSettlementTests</c> 钉住），复活后照样撞门禁；
    /// 而回填是一次性动作、存量工单的直投发布事件不会再来，漏掉它就是永久拒。
    /// </summary>
    [Fact]
    public async Task Backfilled_payload_carries_every_operation_that_is_not_cancelled()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(
            dbContext,
            "WO-MIXED",
            Started,
            // 乱序插入：断言里的 20/30/40/60 只有在载荷真的排过序时才成立。
            [
                (OperationTaskLifecycleStatus.Queued, 60),
                (OperationTaskLifecycleStatus.InProgress, 20),
                (OperationTaskLifecycleStatus.Cancelled, 50),
                (OperationTaskLifecycleStatus.ScheduleInvalidated, 40),
                (OperationTaskLifecycleStatus.Completed, 10),
                (OperationTaskLifecycleStatus.Paused, 30),
            ]);
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var report = await CreateHandler(dbContext, publisher).Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(5, report.OperationsPublished);
        Assert.Equal(
            new[] { 10, 20, 30, 40, 60 },
            published.Payload.Operations.Select(x => x.OperationSequence).ToArray());
        Assert.Equal(MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled, published.EventType);
        Assert.Equal(MesIntegrationEventVersions.V1, published.EventVersion);
        Assert.Equal("SKU-FG-1000", published.Payload.SkuCode);
        Assert.Equal("WC-010", published.Payload.Operations.First().WorkCenterId);
    }

    [Fact]
    public async Task Release_time_falls_back_to_the_earliest_backdated_report_of_the_work_order()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-BACKDATED", Started);
        // 报工时刻由调用方填，可以早于工序建单时刻。Quality 的聚合拒绝「报工早于发布」的组合，
        // 所以补投的发布时刻必须压到这条最早报工之前，否则整张工单进死信、回填对它无效。
        var backdated = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        dbContext.ProductionReports.Add(ProductionReport.Record(
            "org-001", "env-dev", "RPT-OLD", "WO-BACKDATED", "OP-WO-BACKDATED-10",
            goodQuantity: 3m, scrapQuantity: 0m, completesOperation: false, reportedAtUtc: backdated));
        dbContext.ProductionReports.Add(ProductionReport.Record(
            "org-001", "env-dev", "RPT-NEW", "WO-BACKDATED", "OP-WO-BACKDATED-10",
            goodQuantity: 3m, scrapQuantity: 0m, completesOperation: false, reportedAtUtc: Now));
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        await CreateHandler(dbContext, publisher).Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(backdated, published.Payload.ReleasedAtUtc);
    }

    /// <summary>
    /// 发布时刻的下界只能取**本工单自己**的最早报工。同一页里另有一张带远早报工的工单时，
    /// 归属谓词一旦失效，轻则取到别人的时刻，重则 <c>SingleOrDefault</c> 撞多组直接把整次回填打崩。
    /// </summary>
    [Fact]
    public async Task Release_time_never_borrows_another_work_orders_earliest_report()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-OLD", Started);
        AddWorkOrder(dbContext, "WO-NEW", Started);
        var ancient = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        var recent = DateTimeOffset.Parse("2026-08-30T00:00:00Z");
        dbContext.ProductionReports.Add(ProductionReport.Record(
            "org-001", "env-dev", "RPT-OLD", "WO-OLD", "OP-WO-OLD-10",
            goodQuantity: 3m, scrapQuantity: 0m, completesOperation: false, reportedAtUtc: ancient));
        dbContext.ProductionReports.Add(ProductionReport.Record(
            "org-001", "env-dev", "RPT-NEW", "WO-NEW", "OP-WO-NEW-10",
            goodQuantity: 3m, scrapQuantity: 0m, completesOperation: false, reportedAtUtc: recent));
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        await CreateHandler(dbContext, publisher).Handle(
            new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var published = publisher.Published.ToDictionary(x => x.Payload.WorkOrderId, x => x.Payload.ReleasedAtUtc);
        Assert.Equal(ancient, published["WO-OLD"]);
        Assert.Equal(recent, published["WO-NEW"]);
    }

    /// <summary>
    /// 下界取的是「更早者」，不是「有报工就取报工」：报工晚于工序建单时，
    /// 下界仍须落在工序建单那一支，否则该工单的发布时刻会被推后到报工之后。
    /// </summary>
    /// <summary>
    /// 发布时刻必须是该工单**全部**工序建单时刻的下界，不是任意一条：Quality 的 <c>ApplyRelease</c>
    /// 拿它判「报工早于发布 = 冲突事实」，取到较晚那条就会把工单打进死信。
    /// 同一工单的工序并不保证同时建出——<c>CreatedAtUtc</c> 每个实例各取一次 <c>UtcNow</c>，
    /// 而排程计划发布是按工序逐条 <c>Queue</c> 的，工序可以跨多次事件在不同时刻建出。
    /// </summary>
    [Fact]
    public async Task Release_time_is_the_lower_bound_across_all_operations_of_the_work_order()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(
            dbContext,
            "WO-SPREAD",
            Started,
            [
                (OperationTaskLifecycleStatus.Queued, 10),
                (OperationTaskLifecycleStatus.Queued, 20),
            ]);
        await dbContext.SaveChangesAsync();
        // 存量库里两道工序的建单时刻本就不同；这里直接设定持久化值来复刻该状态，
        // 不去复刻「建单那一次调用」——构造函数取的是 UtcNow，没有注入点。
        SetCreatedAtUtc(dbContext, "OP-WO-SPREAD-10", Now);
        SetCreatedAtUtc(dbContext, "OP-WO-SPREAD-20", Now.AddHours(10));
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        await CreateHandler(dbContext, publisher).Handle(
            new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(Now, published.Payload.ReleasedAtUtc);
    }

    [Fact]
    public async Task Release_time_keeps_the_operation_creation_when_the_only_report_is_later()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-LATE-REPORT", Started);
        await dbContext.SaveChangesAsync();
        var earliestCreatedAtUtc = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.WorkOrderId == "WO-LATE-REPORT")
            .MinAsync(x => x.CreatedAtUtc);
        dbContext.ProductionReports.Add(ProductionReport.Record(
            "org-001", "env-dev", "RPT-LATE", "WO-LATE-REPORT", "OP-WO-LATE-REPORT-10",
            goodQuantity: 3m, scrapQuantity: 0m, completesOperation: false,
            reportedAtUtc: earliestCreatedAtUtc.AddHours(5)));
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        await CreateHandler(dbContext, publisher).Handle(
            new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(earliestCreatedAtUtc, published.Payload.ReleasedAtUtc);
    }

    [Fact]
    public async Task Release_time_falls_back_to_the_earliest_operation_creation_when_nothing_was_reported()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-NO-REPORT", Released);
        await dbContext.SaveChangesAsync();
        var earliestCreatedAtUtc = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.WorkOrderId == "WO-NO-REPORT")
            .MinAsync(x => x.CreatedAtUtc);

        var publisher = new RecordingPublisher();
        await CreateHandler(dbContext, publisher).Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(earliestCreatedAtUtc, published.Payload.ReleasedAtUtc);
    }

    [Fact]
    public async Task Rerunning_the_backfill_republishes_the_same_business_identity()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-RERUN", Started);
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var handler = CreateHandler(dbContext, publisher);
        await handler.Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);
        await handler.Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        Assert.Equal(2, publisher.Published.Count);
        Assert.Equal(
            "mes:work-order-release-projection-backfill:org-001:env-dev:WO-RERUN",
            publisher.Published[0].IdempotencyKey);
        Assert.Equal(publisher.Published[0].IdempotencyKey, publisher.Published[1].IdempotencyKey);
        Assert.Equal(publisher.Published[0].Payload.ReleasedAtUtc, publisher.Published[1].Payload.ReleasedAtUtc);
        Assert.Equal(
            publisher.Published[0].Payload.Operations,
            publisher.Published[1].Payload.Operations);
        Assert.NotEqual(publisher.Published[0].EventId, publisher.Published[1].EventId);
    }

    [Fact]
    public async Task Backfill_pages_beyond_a_single_database_round_trip()
    {
        await using var dbContext = CreateDbContext();
        for (var index = 0; index < 205; index++)
        {
            AddWorkOrder(dbContext, $"WO-{index:D4}", Released);
        }

        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var report = await CreateHandler(dbContext, publisher).Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        Assert.Equal(205, report.WorkOrdersPublished);
        Assert.Equal(205, publisher.Published.Select(x => x.Payload.WorkOrderId).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 筛选谓词是可变状态，回填又跑在活库上：扫描中途有工单退出集合（一次普通报工翻 completed、
    /// 或 Close/Cancel/MarkSplit/MarkMerged）时，按偏移量翻页会让后面的工单整段左移、被静默跳过，
    /// 而 <c>WorkOrdersScanned</c> 只累加实际取到的行数，看不出这个缺口。
    /// 这里在第一页处理途中把一张**已扫过**的工单移出集合，断言剩余符合判据的工单仍全部被补投。
    /// </summary>
    [Fact]
    public async Task Work_orders_are_not_skipped_when_the_scanned_set_shrinks_mid_scan()
    {
        await using var dbContext = CreateDbContext();
        for (var index = 0; index < 201; index++)
        {
            AddWorkOrder(dbContext, $"WO-{index:D4}", Released);
        }

        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        publisher.OnFirstPublish = () =>
        {
            var leaving = dbContext.WorkOrders.Single(x => x.WorkOrderIdValue == "WO-0000");
            leaving.Cancel("扫描途中退出集合", Now);
            dbContext.SaveChanges();
        };

        var report = await CreateHandler(dbContext, publisher).Handle(
            new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var publishedIds = publisher.Published.Select(x => x.Payload.WorkOrderId).ToArray();
        Assert.Contains("WO-0200", publishedIds);
        Assert.Equal(201, publishedIds.Length);
        Assert.Equal(201, report.WorkOrdersPublished);
    }

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
                    services.AddSingleton<ISender>(new BackfillReportSender());
                });
            });
        using var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);

        using var anonymous = await client.PostAsync(
            "/internal/business-mes/v1/work-order-release-projection-backfill",
            content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        using var response = await client.PostAsync(
            "/internal/business-mes/v1/work-order-release-projection-backfill",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 端点走的是 ISender，本用例把它换成了桩；真实 handler 是否被 MediatR 发现要单独钉住，
        // 否则「命令改成 internal」这类改动会让端点在运行时找不到 handler 而用例照绿。
        Assert.True(factory.Services
            .GetRequiredService<IServiceProviderIsService>()
            .IsService(typeof(IRequestHandler<
                BackfillWorkOrderReleaseProjectionCommand,
                WorkOrderReleaseProjectionBackfillReport>)));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(7, body.RootElement.GetProperty("workOrdersScanned").GetInt32());
        Assert.Equal(5, body.RootElement.GetProperty("workOrdersPublished").GetInt32());
        Assert.Equal(9, body.RootElement.GetProperty("operationsPublished").GetInt32());
    }

    /// <summary>
    /// 发布侧的投递通道。与消费侧订阅是**同一个事实**：契约类型短名。
    /// Quality 的回填消费组用 <c>[CapSubscribe(nameof(WorkOrderReleaseProjectionBackfilledIntegrationEvent))]</c> 订阅，
    /// 仓库级 <c>CapSubscribeTopicConventionTests.CapSubscribe_topics_match_the_event_short_name</c> 又把
    /// 「订阅 topic == 事件短名」钉死；因此这里断言「投递 topic == 实际投出去那个事件的类型短名」，
    /// 两侧就锁在同一个短名上，无需跨服务引用 Quality 程序集。
    ///
    /// 通道一旦退回直投标识，后果不是「少一条断言」：回填消费组收不到（回填整体失效），
    /// 同时 Scheduling 的发布事件消费者会对每一张在制工单触发排程计划失效、
    /// Quality 直投组把重建下界判为冲突事实成批进死信——正是契约注释里自己列出的那两条。
    /// </summary>
    [Fact]
    public async Task Backfill_publishes_on_the_channel_named_after_the_backfill_event_type()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-CHANNEL", Started);
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        await CreateHandler(dbContext, publisher).Handle(
            new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var topic = Assert.Single(publisher.Topics);
        Assert.Single(publisher.Published);
        // nameof 解析出的就是契约类型短名，消费侧 [CapSubscribe] 订阅的是同一个 nameof。
        Assert.Equal(nameof(WorkOrderReleaseProjectionBackfilledIntegrationEvent), topic);
    }

    /// <summary>
    /// 续扫查询必须能被真实 provider 翻译，且翻出来的是 keyset seek。EF Core InMemory 不做翻译：
    /// 不可翻译的谓词会被它客户端求值放行，退化成 OFFSET 也照样绿，两种都要到生产库才炸/才漏。
    /// 这里用 Npgsql 的 <c>ToQueryString()</c>（只生成 SQL、不连库）钉住**生产代码那条查询本身**。
    /// </summary>
    [Fact]
    public void Page_query_translates_to_a_keyset_seek_on_the_real_provider()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .Options;
        using var dbContext = new ApplicationDbContext(options, new NoopMediator());

        var firstPage = BackfillWorkOrderReleaseProjectionCommandHandler
            .BuildPageQuery(dbContext, null, null, null)
            .ToQueryString();
        var nextPage = BackfillWorkOrderReleaseProjectionCommandHandler
            .BuildPageQuery(dbContext, "org-001", "env-dev", "WO-0199")
            .ToQueryString();

        // 判据集合直接落成 NOT IN，completed 不在其中。
        Assert.Contains("NOT IN ('created', 'cancelled', 'closed', 'scrapped', 'split', 'merged')", firstPage, StringComparison.Ordinal);
        // 续扫是身份比较，不是偏移量；两页都必须带与 seek 同序的 ORDER BY。
        Assert.Contains("ORDER BY w.organization_id, w.environment_id, w.work_order_id", nextPage, StringComparison.Ordinal);
        Assert.Contains("w.work_order_id > ", nextPage, StringComparison.Ordinal);
        Assert.DoesNotContain("OFFSET", firstPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OFFSET", nextPage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 直接改持久化值。<c>OperationTask.CreatedAtUtc</c> 是私有 setter、构造函数里取 <c>UtcNow</c>，
    /// 没有注入点；存量行的建单时刻本来就是各不相同的历史值，用变更跟踪设定它即可复刻，
    /// 不必依赖两次 <c>UtcNow</c> 读数的亚微秒差（那是已知易抖的写法）。
    /// </summary>
    private static void SetCreatedAtUtc(ApplicationDbContext dbContext, string operationTaskId, DateTimeOffset createdAtUtc)
    {
        var task = dbContext.OperationTasks.Single(x => x.OperationTaskIdValue == operationTaskId);
        dbContext.Entry(task).Property(x => x.CreatedAtUtc).CurrentValue = createdAtUtc;
    }

    private static BackfillWorkOrderReleaseProjectionCommandHandler CreateHandler(
        ApplicationDbContext dbContext,
        RecordingPublisher publisher) =>
        new(dbContext, publisher, new FakeTimeProvider(Now));

    private static void Released(WorkOrder workOrder) => workOrder.MarkReleased();

    private static void Started(WorkOrder workOrder)
    {
        workOrder.MarkReleased();
        workOrder.Start(Now);
    }

    private static void Completed(WorkOrder workOrder)
    {
        Started(workOrder);
        workOrder.RecordProductionProgress(goodQuantity: 1000m, scrapQuantity: 0m, reportedAtUtc: Now);
    }

    private static void AddWorkOrder(
        ApplicationDbContext dbContext,
        string workOrderId,
        Action<WorkOrder> advanceToStatus,
        IReadOnlyCollection<(OperationTaskLifecycleStatus Status, int Sequence)>? operations = null)
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            workOrderId,
            "SKU-FG-1000",
            null,
            quantity: 1000m,
            priority: 1,
            dueUtc: Now.AddDays(3));
        advanceToStatus(workOrder);
        dbContext.WorkOrders.Add(workOrder);
        foreach (var (operationStatus, sequence) in operations ?? [(OperationTaskLifecycleStatus.Queued, 10)])
        {
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                workOrderId,
                $"OP-{workOrderId}-{sequence}",
                operationStatus,
                sequence,
                $"WC-{sequence:D3}",
                [],
                Now,
                TimeSpan.FromHours(1),
                null,
                null,
                "SKU-FG-1000",
                "EA",
                1000m));
        }
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-release-projection-backfill-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class BackfillReportSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = Assert.IsType<BackfillWorkOrderReleaseProjectionCommand>(request);
            return Task.FromResult((TResponse)(object)new WorkOrderReleaseProjectionBackfillReport(7, 5, 9));
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

    private sealed class RecordingPublisher : IMesIntegrationEventOutboxPublisher
    {
        public List<WorkOrderReleaseProjectionBackfilledIntegrationEvent> Published { get; } = [];

        public List<string> Topics { get; } = [];

        /// <summary>扫描途中改库的钩子：补投第一张工单时触发一次。</summary>
        public Action? OnFirstPublish { get; set; }

        public Task PublishAsync<T>(string topic, T integrationEvent)
        {
            Topics.Add(topic);
            Published.Add((WorkOrderReleaseProjectionBackfilledIntegrationEvent)(object)integrationEvent!);
            if (Published.Count == 1)
            {
                OnFirstPublish?.Invoke();
            }

            return Task.CompletedTask;
        }
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
