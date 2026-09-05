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

    private static void AddReleasableWorkOrder(
        ApplicationDbContext dbContext,
        string workOrderId,
        string organizationId = Organization,
        string environmentId = Environment)
    {
        dbContext.WorkOrders.Add(WorkOrder.Create(
            organizationId, environmentId, workOrderId, "SKU-FG-1000", "PV-FG-1000",
            quantity: 1000m, priority: 1, dueUtc: ReleaseRequestedAtUtc.AddDays(3)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            organizationId, environmentId, workOrderId, $"OP-{workOrderId}-10",
            OperationTaskLifecycleStatus.InProgress, 10, "WC-MIX", [],
            ReleaseRequestedAtUtc.AddDays(-20), TimeSpan.FromHours(1), null, null,
            "SKU-FG-1000", "EA", 1000m));
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            organizationId, environmentId, workOrderId, $"OP-{workOrderId}-10", "MAT-OIL", null,
            requiredQuantity: 10m, availableQuantity: 10m, stagedQuantity: 0m,
            sourceSystem: "Inventory", sourceSnapshotId: $"inv-ready-{workOrderId}",
            capturedAtUtc: ReleaseRequestedAtUtc.AddDays(-20), substituteMaterialIds: []));
    }

    private static void AddReport(
        ApplicationDbContext dbContext,
        string reportNo,
        string workOrderId,
        DateTimeOffset reportedAtUtc,
        string organizationId = Organization,
        string environmentId = Environment) =>
        dbContext.ProductionReports.Add(ProductionReport.Record(
            organizationId, environmentId, reportNo, workOrderId, $"OP-{workOrderId}-10",
            goodQuantity: 3m, scrapQuantity: 0m, completesOperation: false, reportedAtUtc: reportedAtUtc));

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
