using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;
using Nerv.IIP.Contracts.EquipmentRuntime;
using MaintenanceDbContext = Nerv.IIP.Business.Maintenance.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// 可用窗口读面的占用区间求交谓词跑在真实 PostgreSQL 上。
///
/// 这条谓词是「释放时刻（完工/取消/报警清除的合并值）与查询窗口比较」，带可空时间戳与区间比较 ——
/// InMemory provider 会把翻译不了的表达式在客户端算掉并照绿，只有真实 provider 才会因翻译失败抛错。
/// 因此在途工单与已取消工单能否被读到，必须在 Npgsql 上各证一次。
/// </summary>
[Collection(AcceptancePostgresLaneDatabase.CollectionName)]
public sealed class MaintenanceAvailabilityWindowPostgresAcceptanceTests
{
    [RealPostgresFact]
    public async Task Availability_windows_include_in_flight_and_cancelled_work_orders_on_postgres()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync(MaintenanceFacts.Schema);
        await using var dbContext = CreateMaintenanceDbContext(AcceptancePostgresLaneDatabase.ConnectionString);
        AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var windowStartUtc = now.AddHours(-3);
        var windowEndUtc = now.AddHours(3);

        var inFlight = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-PG-A", "high", "maintenance");
        inFlight.MarkAssetUnavailable(now.AddHours(-2), "repair downtime");
        inFlight.Accept("tech-001");
        inFlight.StartWork();

        var cancelled = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-PG-B", "high", "maintenance");
        cancelled.MarkAssetUnavailable(now.AddHours(-2), "repair downtime");
        cancelled.Cancel();

        // 释放早于查询窗口起点的工单不得进读面 —— 证明求交谓词的右边界那一支真的在 PostgreSQL 上生效。
        var releasedBeforeWindow = MaintenanceWorkOrder.OpenManual("org-001", "env-dev", "DEV-CNC-PG-C", "high", "maintenance");
        releasedBeforeWindow.MarkAssetUnavailable(now.AddHours(-9), "repair downtime");
        releasedBeforeWindow.Accept("tech-001");
        releasedBeforeWindow.StartWork();
        releasedBeforeWindow.Finish("已修复", "mechanical-failure", 30, spareParts: null, technicianUserId: "tech-001");

        dbContext.MaintenanceWorkOrders.AddRange(inFlight, cancelled, releasedBeforeWindow);
        dbContext.Entry(releasedBeforeWindow).Property(x => x.CompletedAtUtc).CurrentValue = now.AddHours(-8);
        await dbContext.SaveChangesAsync();

        var response = await new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext).Handle(
            new QueryMaintenanceAvailabilityWindowsQuery(new EquipmentRuntimeAvailabilityRequest(
                "org-001",
                "env-dev",
                windowStartUtc,
                windowEndUtc,
                ["DEV-CNC-PG-A", "DEV-CNC-PG-B", "DEV-CNC-PG-C"],
                null)),
            CancellationToken.None);

        var inFlightWindow = Assert.Single(response.Items, x => x.DeviceAssetId == "DEV-CNC-PG-A");
        Assert.Equal(windowEndUtc, inFlightWindow.EndUtc);

        var cancelledWindow = Assert.Single(response.Items, x => x.DeviceAssetId == "DEV-CNC-PG-B");
        Assert.Equal(cancelled.CancelledAtUtc, cancelledWindow.EndUtc);

        Assert.DoesNotContain(response.Items, x => x.DeviceAssetId == "DEV-CNC-PG-C");
    }

    private static MaintenanceDbContext CreateMaintenanceDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "maintenance"))
            .Options;
        return new MaintenanceDbContext(options, new AvailabilityNoopMediator());
    }

    private sealed class AvailabilityNoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No mediator requests are expected in this acceptance test.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException("No mediator requests are expected in this acceptance test.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No mediator requests are expected in this acceptance test.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No mediator streams are expected in this acceptance test.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No mediator streams are expected in this acceptance test.");
    }
}
