using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 生产驾驶舱概览（演示走查缺口 #8）：Blockers 与 PendingWork 必须来自真实执行事实，
/// 不再硬编码空数组 / 恒 "Ready"。
/// </summary>
public sealed class GetMesOverviewQueryHandlerTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";

    [Fact]
    public async Task Overview_counts_pending_work_and_material_shortages_from_execution_facts()
    {
        await using var dbContext = CreateDbContext();
        var at = new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);
        var order = WorkOrder.Create(Org, Env, "WO-TEST-0002", "SKU-TEST-001", null, 10m, 1, at.AddDays(3));
        order.MarkReleased();
        dbContext.WorkOrders.Add(order);

        var queued = OperationTask.Queue(
            Org, Env, "WO-TEST-0002", "WO-TEST-0002-OP-10", 10, "WC-TEST-01", [],
            at, TimeSpan.FromHours(1), "SKU-TEST-001", "pcs", 10m, false, "OP-TEST");
        var inProgress = OperationTask.Queue(
            Org, Env, "WO-TEST-0002", "WO-TEST-0002-OP-20", 20, "WC-TEST-01", [],
            at, TimeSpan.FromHours(1), "SKU-TEST-001", "pcs", 10m, false, "OP-TEST");
        inProgress.Start(at);
        dbContext.OperationTasks.AddRange(queued, inProgress);
        dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
            Org, Env, "WO-TEST-0002", null, "RM-TEST-001", null, 10m, 3m, 2m,
            "inventory", "snapshot-001", at, [], "pcs"));
        await dbContext.SaveChangesAsync();

        var response = await new GetMesOverviewQueryHandler(dbContext)
            .Handle(new GetMesOverviewQuery(Org, Env), CancellationToken.None);

        Assert.Equal(1, response.PendingWork.Single(x => x.WorkType == "dispatch-operation-tasks").Count);
        Assert.Equal(1, response.PendingWork.Single(x => x.WorkType == "report-production").Count);
        var shortage = Assert.Single(response.Blockers);
        Assert.Equal("MATERIAL_SHORTAGE", shortage.Code);
        Assert.Equal("material-kitting", shortage.AreaCode);
        Assert.Equal(1, shortage.Count);
        Assert.All(response.Counts, count => Assert.Equal("Ready", count.Status));
    }

    [Fact]
    public async Task Overview_surfaces_hold_and_schedule_invalidation_blockers()
    {
        await using var dbContext = CreateDbContext();

        var held = WorkOrder.Create(Org, Env, "WO-TEST-0001", "SKU-TEST-001", null, 10m, 1, DateTimeOffset.UtcNow.AddDays(3));
        held.MarkReleased();
        held.Hold("质量待确认");
        dbContext.WorkOrders.Add(held);

        var invalidated = OperationTask.Queue(
            Org, Env, "WO-TEST-0001", "WO-TEST-0001-OP-10", 10, "WC-TEST-01", [],
            DateTimeOffset.UtcNow, TimeSpan.FromHours(1), "SKU-TEST-001", "pcs", 10m, false, "OP-TEST");
        invalidated.MarkScheduleInvalidated("SCHEDULE_PLAN_REVOKED");
        dbContext.OperationTasks.Add(invalidated);
        await dbContext.SaveChangesAsync();

        var response = await new GetMesOverviewQueryHandler(dbContext)
            .Handle(new GetMesOverviewQuery(Org, Env), CancellationToken.None);

        Assert.Equal(1, response.Blockers.Single(x => x.Code == "WORK_ORDER_ON_HOLD").Count);
        Assert.Equal(1, response.Blockers.Single(x => x.Code == "SCHEDULE_INVALIDATED").Count);
        Assert.Equal("Attention", response.Counts.Single(x => x.Key == "work-orders").Status);
        Assert.Equal("Attention", response.Counts.Single(x => x.Key == "operation-tasks").Status);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-overview-{Guid.CreateVersion7():N}")
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

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
