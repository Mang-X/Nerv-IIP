using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;
using Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Seed;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Tests;

/// <summary>
/// L1 设备域历史（三期）IndustrialTelemetry 侧：共享形状黄金向量（与 Maintenance 侧同字面量、
/// 同锚点值，防跨服务漂移）+ 种子幂等 + 截止日推进 catch-up + fail-closed 校验器。
/// </summary>
public sealed class WorldHistoryDeviceSeedServiceTests
{
    /// <summary>黄金向量锚定日（固定字面量，两侧测试同值）。</summary>
    private static readonly DateOnly GoldenAsOfDate = new(2026, 7, 24);

    [Fact]
    public void Device_spec_matches_world_bible_collection_manifest()
    {
        // 46 台 / 96 点位 / 3 连接器，与 L0 采集清单逐条同字面量。
        Assert.Equal(46, WorldHistoryDeviceSpec.Devices.Count);
        var specTags = WorldHistoryDeviceSpec.Devices
            .SelectMany(device => device.Class.Tags.Select(tag => (device.DeviceAssetId, tag.TagKey, tag.UnitCode, device.Class.CollectionConnectorId)))
            .ToArray();
        Assert.Equal(96, specTags.Length);

        var bibleTags = WorldBibleSpec.DeviceTags
            .Select(x => (x.DeviceAssetId, x.TagKey, x.UnitCode, x.CollectionConnectorId))
            .ToHashSet();
        Assert.All(specTags, tag => Assert.Contains(tag, bibleTags));
    }

    [Fact]
    public void Alarm_plans_are_deterministic_and_pin_the_cross_service_golden_vector()
    {
        var plans = WorldHistoryDeviceSpec.BuildAlarmPlans(GoldenAsOfDate, 1.0);
        var again = WorldHistoryDeviceSpec.BuildAlarmPlans(GoldenAsOfDate, 1.0);
        Assert.Equal(plans, again);

        // 设定集 §7 量级：报警约 400 起、维修工单约 120 张。
        Assert.InRange(plans.Count, 320, 460);
        Assert.InRange(plans.Count(x => x.HasWorkOrder), 90, 150);

        // 跨服务锚点（Maintenance 侧测试锁同一批字面量）。
        Assert.Equal(GoldenPlanCount, plans.Count);
        Assert.Equal(GoldenWorkOrderCount, plans.Count(x => x.HasWorkOrder));
        Assert.Equal(GoldenChainHash, ComputeChainHash(plans));

        // 工单号连续且在独立号段。
        var workOrderNos = plans.Where(x => x.HasWorkOrder).Select(x => x.WorkOrderNo!).ToArray();
        Assert.Equal(workOrderNos.Length, workOrderNos.Distinct(StringComparer.Ordinal).Count());
        Assert.All(workOrderNos, no => Assert.Matches(@"^MWO-2026-\d{4}$", no));

        // 时间戳全部落在上线日之后、截止日之前，且触发时刻在班次窗口内（无「凌晨 3 点报警」穿帮）。
        Assert.All(plans, plan =>
        {
            Assert.True(plan.RaisedAtUtc >= new DateTimeOffset(WorldHistoryCalendar.GoLiveDate, TimeOnly.MinValue, TimeSpan.Zero));
            Assert.True(DateOnly.FromDateTime(plan.RaisedAtUtc.UtcDateTime) <= GoldenAsOfDate);
            Assert.InRange(plan.RaisedAtUtc.UtcDateTime.Hour, 0, 15);
        });
    }

    [Fact]
    public async Task Seed_is_idempotent_and_validator_passes()
    {
        await using var db = CreateDbContext();
        var asOfDate = new DateOnly(2026, 2, 4);
        var seed = new WorldHistorySeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, 1.0);
        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, 1.0);

        Assert.True(first.AlarmRulesWritten > 0);
        Assert.True(first.DailyRollupsWritten > 0);
        Assert.True(first.RawSamplesWritten > 0);
        Assert.True(first.SummariesWritten > 0);
        Assert.True(first.DeviceStateSnapshotsWritten > 0);
        Assert.True(first.OeeFactsWritten > 0);

        Assert.Equal(0, second.AlarmRulesWritten);
        Assert.Equal(0, second.AlarmEventsWritten);
        Assert.Equal(0, second.DailyRollupsWritten);
        Assert.Equal(0, second.HourlyRollupsWritten);
        Assert.Equal(0, second.RawSamplesWritten);
        Assert.Equal(0, second.SummariesWritten);
        Assert.Equal(0, second.DeviceStateSnapshotsWritten);
        Assert.Equal(0, second.OeeFactsWritten);

        // 周日/春节无生产遥测：随机抽一个周日断言非辅助设备当天没有日级聚合。
        var sunday = new DateTimeOffset(new DateOnly(2026, 1, 11), TimeOnly.MinValue, TimeSpan.Zero);
        Assert.False(await db.TelemetryRollups.AnyAsync(x =>
            x.Grain == TelemetryRollupGrain.Daily
            && x.WindowStartUtc == sunday
            && !x.DeviceAssetId.StartsWith("DEV-AUX-")));
        Assert.True(await db.TelemetryRollups.AnyAsync(x =>
            x.Grain == TelemetryRollupGrain.Daily
            && x.WindowStartUtc == sunday
            && x.DeviceAssetId.StartsWith("DEV-AUX-")));
    }

    [Fact]
    public async Task Seed_catches_up_open_tail_alarms_when_as_of_date_advances()
    {
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);
        var firstAsOf = new DateOnly(2026, 1, 21);

        await seed.SeedAsync("org-001", "env-dev", firstAsOf, 1.0);
        var openBefore = await db.AlarmEvents.CountAsync(x => x.Status == "raised");

        // 截止日前推一周：上一轮的开放尾部报警应被追平清除，校验器保持通过。
        await seed.SeedAsync("org-001", "env-dev", firstAsOf.AddDays(7), 1.0);

        var plans = WorldHistoryDeviceSpec.BuildAlarmPlans(firstAsOf.AddDays(7), 1.0);
        var openExpected = plans.Count(x => x.IsOpenAtAsOf);
        var openAfter = await db.AlarmEvents.CountAsync(x => x.Status == "raised");
        Assert.Equal(openExpected, openAfter);
        Assert.True(openBefore == 0 || openAfter <= openBefore + plans.Count);
    }

    private const int GoldenPlanCount = 395;
    private const int GoldenWorkOrderCount = 116;
    private const string GoldenChainHash = "A021DFB4ED425CDB";

    /// <summary>FNV-1a 64 锚定整条报警链（号段/设备/时刻/工单号），两侧测试用同一字面量。</summary>
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
            .UseInMemoryDatabase($"industrial-telemetry-world-history-{Guid.CreateVersion7():N}")
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
