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

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 直投发布时刻下界（#3117）的**聚合查询**必须由真实 provider 执行。
/// 其余用例跑在 EF Core InMemory 上：它不翻译，<c>Min</c> 的可空提升与三分量归属谓词
/// 都由客户端求值放行——真翻译不出来（或翻错分组）也照绿，只有到生产库才炸。
/// 本用例让 PostgreSQL 自己算这个下界。
/// </summary>
[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class WorkOrderReleaseFactTimePostgresTests
{
    private const string Organization = "org-001";
    private const string Environment = "env-dev";
    private static readonly DateTimeOffset ReleaseRequestedAtUtc = DateTimeOffset.Parse("2026-09-02T10:00:00Z");
    private static readonly DateTimeOffset EarliestReportedAtUtc = DateTimeOffset.Parse("2026-08-20T06:00:00Z");

    [MesRealPostgresFact(Timeout = 30_000)]
    public async Task Release_of_an_already_reporting_work_order_takes_the_earliest_report_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        AddReleasableWorkOrder(dbContext, "WO-PG-TARGET");
        AddReport(dbContext, "RPT-PG-LATER", "WO-PG-TARGET", DateTimeOffset.Parse("2026-08-25T06:00:00Z"));
        AddReport(dbContext, "RPT-PG-EARLIEST", "WO-PG-TARGET", EarliestReportedAtUtc);
        // 三个对照分别只在工单号、组织、环境上不同，都带一条 2020 年的报工：
        // 归属谓词少任何一条合取项，下界都会被拉到 2020。
        // 报工行受 fk_production_reports_work_orders 与 fk_production_reports_operation_tasks 约束，
        // 所以每个对照都要有自己那一套工单与工序行——真实 provider 上不存在「悬空的对照报工」。
        var ancient = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        AddReleasableWorkOrder(dbContext, "WO-PG-OTHER");
        AddReleasableWorkOrder(dbContext, "WO-PG-TARGET", organizationId: "org-002");
        AddReleasableWorkOrder(dbContext, "WO-PG-TARGET", environmentId: "env-prod");
        AddReport(dbContext, "RPT-PG-OTHER-WO", "WO-PG-OTHER", ancient);
        AddReport(dbContext, "RPT-PG-OTHER-ORG", "WO-PG-TARGET", ancient, organizationId: "org-002");
        AddReport(dbContext, "RPT-PG-OTHER-ENV", "WO-PG-TARGET", ancient, environmentId: "env-prod");
        await dbContext.SaveChangesAsync();

        await new ReleaseWorkOrderCommandHandler(dbContext).Handle(
            new ReleaseWorkOrderCommand(Organization, Environment, "WO-PG-TARGET", ReleaseRequestedAtUtc),
            CancellationToken.None);

        var workOrder = await dbContext.WorkOrders.SingleAsync(x =>
            x.OrganizationId == Organization
            && x.EnvironmentId == Environment
            && x.WorkOrderIdValue == "WO-PG-TARGET");
        var domainEvent = Assert.IsType<WorkOrderReleasedDomainEvent>(
            Assert.Single(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent));
        var integrationEvent = new WorkOrderReleasedIntegrationEventConverter().Convert(domainEvent);
        Assert.Equal(EarliestReportedAtUtc, integrationEvent.Payload.ReleasedAtUtc);
    }

    /// <summary>
    /// #3129：下达载荷必须按**工序**带出「下达动作那一刻已经存在的净良品量」。
    /// 这条查询换了分组键（工单级 <c>Min</c> → 按 <c>OperationTaskId</c> 的 <c>Min</c> + 条件 <c>Sum</c>），
    /// 分组键与 <c>SUM(CASE WHEN ...)</c> 都必须由真实 provider 翻译；EF Core InMemory 上
    /// 分错组或翻不出条件求和都会被客户端求值兜住、照绿。
    /// </summary>
    [MesRealPostgresFact(Timeout = 30_000)]
    public async Task Release_carries_each_operations_pre_release_good_quantity_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        AddReleasableWorkOrder(dbContext, "WO-PG-MULTI", additionalOperationSequences: [20]);
        // OP-10 报 250（两笔 150 + 100），OP-20 报 250（一笔）。两道工序的报工都发生在下达动作之前。
        AddReport(dbContext, "RPT-PG-M10-A", "WO-PG-MULTI", DateTimeOffset.Parse("2026-08-20T06:00:00Z"), goodQuantity: 150m);
        AddReport(dbContext, "RPT-PG-M10-B", "WO-PG-MULTI", DateTimeOffset.Parse("2026-08-21T06:00:00Z"), goodQuantity: 100m);
        AddReport(
            dbContext, "RPT-PG-M20-A", "WO-PG-MULTI", DateTimeOffset.Parse("2026-08-22T06:00:00Z"),
            goodQuantity: 250m, operationSequence: 20);
        // 冲销行：按 Quality 侧 QuantityHighWater 的口径（只累计非冲销行），它既不被计入、
        // 也不把被冲销那一笔扣回去。这一格是**换键之外**的第二条鉴别力：
        // 把条件求和写成无条件 Sum，OP-10 会读成 250 + (-150) = 100。
        var reversed = ProductionReport.Reverse(
            dbContext.ProductionReports.Local.Single(x => x.ReportNo == "RPT-PG-M10-A"),
            "RPT-PG-M10-A-REV",
            DateTimeOffset.Parse("2026-08-23T06:00:00Z"),
            "试制批次作废",
            "user:tester");
        dbContext.ProductionReports.Add(reversed);
        // 另一张工单的报工：分组键漏掉工单归属时会被并进来。
        AddReleasableWorkOrder(dbContext, "WO-PG-MULTI-OTHER");
        AddReport(dbContext, "RPT-PG-OTHERWO", "WO-PG-MULTI-OTHER", DateTimeOffset.Parse("2026-08-24T06:00:00Z"), goodQuantity: 999m);
        await dbContext.SaveChangesAsync();

        await new ReleaseWorkOrderCommandHandler(dbContext).Handle(
            new ReleaseWorkOrderCommand(Organization, Environment, "WO-PG-MULTI", ReleaseRequestedAtUtc),
            CancellationToken.None);

        var workOrder = await dbContext.WorkOrders.SingleAsync(x =>
            x.OrganizationId == Organization
            && x.EnvironmentId == Environment
            && x.WorkOrderIdValue == "WO-PG-MULTI");
        var domainEvent = Assert.IsType<WorkOrderReleasedDomainEvent>(
            Assert.Single(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent));
        var integrationEvent = new WorkOrderReleasedIntegrationEventConverter().Convert(domainEvent);

        Assert.Equal(
            ["OP-WO-PG-MULTI-10", "OP-WO-PG-MULTI-20"],
            integrationEvent.Payload.Operations.Select(x => x.OperationId));
        Assert.Equal(
            [250m, 250m],
            integrationEvent.Payload.Operations.Select(x => x.PreReleaseGoodQuantity));
        // 工单级下界仍由全部工序的最早报工取 Min——换键不得改动这一条（#3117）。
        Assert.Equal(DateTimeOffset.Parse("2026-08-20T06:00:00Z"), integrationEvent.Payload.ReleasedAtUtc);
    }

    /// <summary>
    /// #3129：一条报工都没有的工序，载荷带 <c>0</c>，**不是** <c>null</c>。
    /// null 在契约上专留给本次发布之前入队的旧消息（见 <c>ReleasedOperationPayload.PreReleaseGoodQuantity</c>），
    /// 生产者这条路径永远不发 null；发成 null 会让消费侧落回「不跳过」的老行为而无人察觉。
    /// </summary>
    [MesRealPostgresFact(Timeout = 30_000)]
    public async Task Release_of_an_operation_without_any_report_carries_zero_not_null_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        AddReleasableWorkOrder(dbContext, "WO-PG-QUIET", additionalOperationSequences: [20]);
        AddReport(dbContext, "RPT-PG-QUIET-10", "WO-PG-QUIET", DateTimeOffset.Parse("2026-08-20T06:00:00Z"), goodQuantity: 250m);
        await dbContext.SaveChangesAsync();

        await new ReleaseWorkOrderCommandHandler(dbContext).Handle(
            new ReleaseWorkOrderCommand(Organization, Environment, "WO-PG-QUIET", ReleaseRequestedAtUtc),
            CancellationToken.None);

        var workOrder = await dbContext.WorkOrders.SingleAsync(x =>
            x.OrganizationId == Organization
            && x.EnvironmentId == Environment
            && x.WorkOrderIdValue == "WO-PG-QUIET");
        var integrationEvent = new WorkOrderReleasedIntegrationEventConverter().Convert(
            Assert.IsType<WorkOrderReleasedDomainEvent>(
                Assert.Single(workOrder.GetDomainEvents(), x => x is WorkOrderReleasedDomainEvent)));

        Assert.Equal(
            [250m, 0m],
            integrationEvent.Payload.Operations.Select(x => x.PreReleaseGoodQuantity));
        Assert.All(
            integrationEvent.Payload.Operations,
            operation => Assert.NotNull(operation.PreReleaseGoodQuantity));
    }

    private static void AddReleasableWorkOrder(
        ApplicationDbContext dbContext,
        string workOrderId,
        string organizationId = Organization,
        string environmentId = Environment,
        IReadOnlyCollection<int>? additionalOperationSequences = null)
    {
        dbContext.WorkOrders.Add(WorkOrder.Create(
            organizationId, environmentId, workOrderId, "SKU-FG-1000", "PV-FG-1000",
            quantity: 1000m, priority: 1, dueUtc: ReleaseRequestedAtUtc.AddDays(3)));
        foreach (var sequence in new[] { 10 }.Concat(additionalOperationSequences ?? []))
        {
            dbContext.OperationTasks.Add(OperationTask.Create(
                organizationId, environmentId, workOrderId, $"OP-{workOrderId}-{sequence}",
                OperationTaskLifecycleStatus.InProgress, sequence, "WC-MIX", [],
                ReleaseRequestedAtUtc.AddDays(-20), TimeSpan.FromHours(1), null, null,
                "SKU-FG-1000", "EA", 1000m));
            dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
                organizationId, environmentId, workOrderId, $"OP-{workOrderId}-{sequence}", "MAT-OIL", null,
                requiredQuantity: 10m, availableQuantity: 10m, stagedQuantity: 0m,
                sourceSystem: "Inventory", sourceSnapshotId: $"inv-ready-{workOrderId}-{sequence}",
                capturedAtUtc: ReleaseRequestedAtUtc.AddDays(-20), substituteMaterialIds: []));
        }
    }

    private static void AddReport(
        ApplicationDbContext dbContext,
        string reportNo,
        string workOrderId,
        DateTimeOffset reportedAtUtc,
        string organizationId = Organization,
        string environmentId = Environment,
        decimal goodQuantity = 3m,
        int operationSequence = 10) =>
        dbContext.ProductionReports.Add(ProductionReport.Record(
            organizationId, environmentId, reportNo, workOrderId, $"OP-{workOrderId}-{operationSequence}",
            goodQuantity: goodQuantity, scrapQuantity: 0m, completesOperation: false, reportedAtUtc: reportedAtUtc));

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
