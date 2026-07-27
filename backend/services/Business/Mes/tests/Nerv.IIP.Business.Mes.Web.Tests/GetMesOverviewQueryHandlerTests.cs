using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Seed;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 生产驾驶舱概览（演示走查缺口 #8）：Blockers 与 PendingWork 必须来自真实执行事实，
/// 不再硬编码空数组 / 恒 "Ready"。
/// </summary>
public sealed class GetMesOverviewQueryHandlerTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";

    /// <summary>
    /// L1 历史种子跑完后，驾驶舱必须能算出真实待办（历史形状必含排队未派工与在制工序），
    /// 且不虚报阻塞（历史数据没有挂起/排程失效/过账失败）。多日期扫描防单日期盲区。
    /// </summary>
    [Theory]
    [InlineData(2026, 7, 27)]
    [InlineData(2026, 7, 26)]
    [InlineData(2026, 2, 16)]
    public async Task Overview_reports_real_pending_work_after_history_seed(int year, int month, int day)
    {
        await using var dbContext = CreateDbContext();
        await new WorldHistorySeedService(dbContext, new StubProductionVersionResolver())
            .SeedAsync(Org, Env, new DateOnly(year, month, day), 0.02d);

        var response = await new GetMesOverviewQueryHandler(dbContext)
            .Handle(new GetMesOverviewQuery(Org, Env), CancellationToken.None);

        var expectedDispatch = await dbContext.OperationTasks
            .CountAsync(x => x.Status == OperationTaskLifecycleStatus.Queued && x.AssignedUserId == null);
        var expectedReport = await dbContext.OperationTasks
            .CountAsync(x => x.Status == OperationTaskLifecycleStatus.InProgress);
        Assert.True(expectedDispatch > 0);
        Assert.True(expectedReport > 0);

        Assert.Equal(expectedDispatch, response.PendingWork.Single(x => x.WorkType == "dispatch-operation-tasks").Count);
        Assert.Equal(expectedReport, response.PendingWork.Single(x => x.WorkType == "report-production").Count);

        // 历史数据没有挂起/排程失效/过账失败/缺料——不许虚报阻塞。
        Assert.Empty(response.Blockers);
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

    private sealed class StubProductionVersionResolver : IWorldHistoryProductionVersionResolver
    {
        public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<string> skuCodes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                skuCodes.ToDictionary(x => x, x => $"PV-{x}", StringComparer.Ordinal));
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
