using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Seed;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// L1 设备域历史（三期）Maintenance 侧：跨服务黄金向量（与 IndustrialTelemetry 侧
/// <c>WorldHistoryDeviceSeedServiceTests</c> 锁同一批字面量——两侧的报警/工单对账按构造成立）
/// + 种子幂等 + 截止日推进 catch-up + 计划游标不欠账。
/// </summary>
public sealed class WorldHistoryMaintenanceSeedServiceTests
{
    /// <summary>黄金向量锚定日与锚点值：必须与 IndustrialTelemetry 侧测试逐字面量相同。</summary>
    private static readonly DateOnly GoldenAsOfDate = new(2026, 7, 24);
    private const int GoldenPlanCount = 395;
    private const int GoldenWorkOrderCount = 116;
    private const string GoldenChainHash = "A021DFB4ED425CDB";

    [Fact]
    public void Alarm_plans_pin_the_same_cross_service_golden_vector_as_industrial_telemetry()
    {
        var plans = WorldHistoryDeviceSpec.BuildAlarmPlans(GoldenAsOfDate, 1.0);
        Assert.Equal(GoldenPlanCount, plans.Count);
        Assert.Equal(GoldenWorkOrderCount, plans.Count(x => x.HasWorkOrder));
        Assert.Equal(GoldenChainHash, ComputeChainHash(plans));

        // 完工工单齐备 MTBF/MTTR 输入：开单 / 开修 / 完工 / 停机分钟全在。
        Assert.All(plans.Where(x => x.HasWorkOrder && !x.IsOpenAtAsOf), plan =>
        {
            Assert.NotNull(plan.WorkOrderNo);
            Assert.NotNull(plan.RepairStartedAtUtc);
            Assert.NotNull(plan.CompletedAtUtc);
            Assert.True(plan.RepairStartedAtUtc > plan.RaisedAtUtc);
            Assert.True(plan.CompletedAtUtc > plan.RepairStartedAtUtc);
            Assert.True(plan.DowntimeMinutes >= 15);
        });
    }

    [Fact]
    public void Maintenance_plans_cover_all_devices_with_inspection_and_service_cadence()
    {
        var plans = WorldHistoryDeviceSpec.BuildMaintenancePlans();
        Assert.Equal(92, plans.Count);
        Assert.Equal(46, plans.Count(x => x.Kind == "inspection"));
        Assert.Equal(46, plans.Count(x => x.Kind == "service"));
        Assert.All(plans, plan => Assert.StartsWith("PM-WH-", plan.PlanCode, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Seed_is_idempotent_and_validator_passes()
    {
        await using var db = CreateDbContext();
        var asOfDate = new DateOnly(2026, 2, 4);
        var seed = new WorldHistorySeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, 1.0);
        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, 1.0);

        Assert.Equal(WorldHistoryDeviceSpec.DowntimeReasons.Length, first.DowntimeReasonsWritten);
        Assert.Equal(92, first.MaintenancePlansWritten);
        Assert.True(first.InspectionsWritten > 0);
        Assert.True(first.WorkOrdersWritten > 0);

        Assert.Equal(0, second.DowntimeReasonsWritten);
        Assert.Equal(0, second.MaintenancePlansWritten);
        Assert.Equal(0, second.InspectionsWritten);
        Assert.Equal(0, second.WorkOrdersWritten);

        // 计划游标推进到截止日之后：调度器不会做 29 周 catch-up 开单。
        Assert.False(await db.MaintenancePlans.AnyAsync(x =>
            x.PlanCode.StartsWith("PM-WH-") && x.NextDueOn != null && x.NextDueOn <= asOfDate));
    }

    [Fact]
    public async Task Seed_completes_open_tail_work_orders_when_as_of_date_advances()
    {
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);
        var firstAsOf = new DateOnly(2026, 1, 21);

        await seed.SeedAsync("org-001", "env-dev", firstAsOf, 1.0);
        await seed.SeedAsync("org-001", "env-dev", firstAsOf.AddDays(7), 1.0);

        var plans = WorldHistoryDeviceSpec.BuildAlarmPlans(firstAsOf.AddDays(7), 1.0);
        var expectedOpen = plans.Count(x => x.HasWorkOrder && x.CompletedAtUtc is null);
        var actualOpen = await db.MaintenanceWorkOrders.CountAsync(x =>
            x.SourceReferenceId != null && x.SourceReferenceId.StartsWith("MWO-2026-")
            && x.Status == MaintenanceWorkOrderStatus.Open);
        Assert.Equal(expectedOpen, actualOpen);
    }

    /// <summary>与 IndustrialTelemetry 侧完全相同的 FNV-1a 64 链哈希公式。</summary>
    private static string ComputeChainHash(IReadOnlyList<WorldHistoryAlarmPlan> plans)
    {
        var hash = 0xCBF29CE484222325UL;
        foreach (var plan in plans)
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{plan.ExternalAlarmId}|{plan.DeviceAssetId}|{plan.TagKey}|{plan.Severity}|{plan.RaisedAtUtc:O}|{plan.DurationMinutes}|{plan.WorkOrderNo}|{plan.DowntimeMinutes}|{plan.IsOpenAtAsOf}");
            foreach (var character in line)
            {
                hash ^= character;
                hash *= 0x100000001B3UL;
            }
        }

        return hash.ToString("X16", CultureInfo.InvariantCulture);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"maintenance-world-history-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new WorldHistoryTestMediator());
    }

    private sealed class WorldHistoryTestMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
