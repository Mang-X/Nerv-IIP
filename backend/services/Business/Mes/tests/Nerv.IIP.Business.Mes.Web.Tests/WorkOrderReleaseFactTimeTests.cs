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
        string environmentId = Environment)
    {
        dbContext.WorkOrders.Add(WorkOrder.Create(
            organizationId, environmentId, workOrderId, "SKU-FG-1000", "PV-FG-1000",
            quantity: 1000m, priority: 1, dueUtc: ReleaseRequestedAtUtc.AddDays(3)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            organizationId,
            environmentId,
            workOrderId,
            $"OP-{workOrderId}-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-MIX",
            [],
            ReleaseRequestedAtUtc.AddDays(-20),
            TimeSpan.FromHours(1),
            null,
            null,
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
