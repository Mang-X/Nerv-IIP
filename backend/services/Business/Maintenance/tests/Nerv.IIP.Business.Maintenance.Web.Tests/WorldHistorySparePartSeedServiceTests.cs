using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Seed;
using Xunit.Abstractions;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// L1 背景历史 **四期**（维修备件消耗行 / 设备状态投影）的门禁测试。
///
/// 关键约束：任意 asOfDate 都必须成立——演示日期一改，备件行规模、金额对账、
/// 设备状态覆盖与「设备状态 ↔ 在办工单」一致性都不能塌。因此规模 / 分布 / 一致性类断言
/// 一律走 5 日期 <c>[Theory]</c>。
/// </summary>
public sealed class WorldHistorySparePartSeedServiceTests(ITestOutputHelper output)
{
    /// <summary>五个演示候选日期：周日后首日 / 常规日 / 月初 / 春节段 / 月末。</summary>
    public static TheoryData<int, int, int> AsOfDates =>
        new() { { 2026, 7, 27 }, { 2026, 7, 24 }, { 2026, 8, 3 }, { 2026, 2, 16 }, { 2026, 7, 31 } };

    /// <summary>库写入类用例的规模：全量 29 周 × 46 台在 InMemory 上过慢，0.35 仍能出 30+ 张完工单。</summary>
    private const double SmallScale = 0.35d;

    #region 备件消耗行（纯函数 Spec）

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Spare_part_issues_keep_their_shape_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var completed = WorldHistoryDeviceSpec.BuildAlarmPlans(asOfDate, 1.0)
            .Where(x => x.HasWorkOrder && x.CompletedAtUtc is not null)
            .ToArray();
        var issuesByWorkOrder = completed
            .ToDictionary(
                x => x.WorkOrderNo!,
                x => WorldHistorySparePartSpec.BuildIssues(x.WorkOrderNo!, x.FailureCauseCode),
                StringComparer.Ordinal);
        var lines = issuesByWorkOrder.Values.SelectMany(x => x).ToArray();

        var withParts = issuesByWorkOrder.Count(x => x.Value.Count > 0);
        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} completed-work-orders={completed.Length} "
            + $"spare-part-lines={lines.Length} work-orders-with-parts={withParts}");

        // 不是每张完工单都换件：换件比例落在 55%–90%（Spec 无备件概率 28%，样本抖动留边）。
        Assert.NotEmpty(lines);
        var withPartsRatio = (double)withParts / completed.Length;
        Assert.InRange(withPartsRatio, 0.55d, 0.90d);

        // 平均 1.0–1.5 行/完工单；单张最多 3 行。
        Assert.InRange((double)lines.Length / completed.Length, 0.80d, 1.50d);
        Assert.All(issuesByWorkOrder.Values, issues => Assert.InRange(issues.Count, 0, 3));

        // 一张工单内不重复领同一个备件（现场是一行一物料）。
        Assert.All(issuesByWorkOrder.Values, issues =>
            Assert.Equal(issues.Count, issues.Select(x => x.SkuCode).Distinct(StringComparer.Ordinal).Count()));

        // 号段格式 + 中文名 + 规格 + 正数量 + 行金额自洽。
        Assert.All(lines, issue =>
        {
            Assert.Matches(@"^MRO-[A-Z]{3}-\d{2}$", issue.SkuCode);
            Assert.Matches(@"\p{IsCJKUnifiedIdeographs}", issue.Part.Name);
            Assert.False(string.IsNullOrWhiteSpace(issue.Part.Specification));
            Assert.False(string.IsNullOrWhiteSpace(issue.UomCode));
            Assert.True(issue.Quantity > 0m);
            Assert.True(issue.Part.UnitPrice > 0m);
            Assert.Equal(decimal.Round(issue.Part.UnitPrice * issue.Quantity, 2), issue.Amount);
        });
    }

    /// <summary>每种故障原因允许出现的备件前缀（演示时「为什么这张单换了这个件」要能当场解释）。</summary>
    private static readonly Dictionary<string, string[]> AllowedPrefixesByCause = new(StringComparer.Ordinal)
    {
        ["bearing-wear"] = ["MRO-BRG", "MRO-GRS", "MRO-SEL"],
        ["fixture-loose"] = ["MRO-MEC", "MRO-PNE", "MRO-ORG"],
        ["lubrication"] = ["MRO-GRS", "MRO-OIL", "MRO-FLT", "MRO-SEL"],
        ["cooling"] = ["MRO-COL", "MRO-SNS", "MRO-OIL"],
        ["tooling-drift"] = ["MRO-TOL", "MRO-GRD", "MRO-MEC", "MRO-ORG"],
        ["electrical"] = ["MRO-ELC", "MRO-SNS", "MRO-BLT"],
        ["process-drift"] = ["MRO-PRC", "MRO-FLT", "MRO-ORG"],
        ["air-leak"] = ["MRO-PNE", "MRO-ORG", "MRO-FLT"],
        ["overload"] = ["MRO-ELC", "MRO-MEC", "MRO-BLT"],
    };

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public void Spare_part_issues_match_the_failure_cause(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        var completed = WorldHistoryDeviceSpec.BuildAlarmPlans(asOfDate, 1.0)
            .Where(x => x.HasWorkOrder && x.CompletedAtUtc is not null)
            .ToArray();

        var causesSeen = new HashSet<string>(StringComparer.Ordinal);
        var linesChecked = 0;
        foreach (var plan in completed)
        {
            var allowed = Assert.Contains(plan.FailureCauseCode, AllowedPrefixesByCause);
            foreach (var issue in WorldHistorySparePartSpec.BuildIssues(plan.WorkOrderNo!, plan.FailureCauseCode))
            {
                Assert.Contains(issue.SkuCode[..7], allowed, StringComparer.Ordinal);
                causesSeen.Add(plan.FailureCauseCode);
                linesChecked++;
            }
        }

        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} lines-checked={linesChecked} "
            + $"causes-exercised={causesSeen.Count}/{AllowedPrefixesByCause.Count}");
        Assert.True(linesChecked > 0);
    }

    /// <summary>全量 29 周历史（演示基准日）下九类故障原因全部被走到——候选池没有死条目。</summary>
    [Fact]
    public void Every_failure_cause_pool_is_exercised_over_the_full_history()
    {
        var completed = WorldHistoryDeviceSpec.BuildAlarmPlans(new DateOnly(2026, 7, 24), 1.0)
            .Where(x => x.HasWorkOrder && x.CompletedAtUtc is not null)
            .ToArray();
        var causesWithLines = completed
            .Where(x => WorldHistorySparePartSpec.BuildIssues(x.WorkOrderNo!, x.FailureCauseCode).Count > 0)
            .Select(x => x.FailureCauseCode)
            .ToHashSet(StringComparer.Ordinal);

        output.WriteLine($"causes-with-lines={string.Join(',', causesWithLines.OrderBy(x => x, StringComparer.Ordinal))}");
        Assert.Contains("bearing-wear", causesWithLines);
        Assert.Contains("electrical", causesWithLines);
        Assert.True(causesWithLines.Count >= 6);
    }

    [Fact]
    public void Spare_part_catalog_is_unique_and_deterministic()
    {
        var catalog = WorldHistorySparePartSpec.Catalog;
        Assert.Equal(catalog.Count, catalog.Select(x => x.SkuCode).Distinct(StringComparer.Ordinal).Count());
        Assert.All(catalog, part => Assert.Equal(part, WorldHistorySparePartSpec.Get(part.SkuCode)));

        // 同一工单号 + 同一原因码，两次调用逐字段相同（与调用顺序、其他工单无关）。
        var first = WorldHistorySparePartSpec.BuildIssues("MWO-2026-0123", "bearing-wear");
        var second = WorldHistorySparePartSpec.BuildIssues("MWO-2026-0123", "bearing-wear");
        Assert.Equal(first, second);
    }

    #endregion

    #region 库写入：幂等 / 金额对账 / 不触发库存流水

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Seed_writes_spare_part_lines_idempotently_and_reconciles_cost(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        var lineCount = await db.SparePartLines.CountAsync();
        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} written={first.SparePartLinesWritten} "
            + $"persisted={lineCount} validator={first.Validation.SparePartLinesChecked}");

        Assert.True(first.SparePartLinesWritten > 0);
        Assert.Equal(first.SparePartLinesWritten, lineCount);
        Assert.Equal(lineCount, first.Validation.SparePartLinesChecked);

        // 幂等：重跑写入量为 0，终态不变。
        Assert.Equal(0, second.SparePartLinesWritten);
        Assert.Equal(0, second.WorkOrdersWritten);
        Assert.Equal(lineCount, await db.SparePartLines.CountAsync());
        Assert.Equal(lineCount, second.Validation.SparePartLinesChecked);

        // 工单备件金额 = 行金额合计（校验器已 fail-closed，这里再抽查币种与非负）。
        var completed = await db.MaintenanceWorkOrders
            .AsNoTracking()
            .Where(x => x.SourceReferenceId != null && x.SourceReferenceId.StartsWith("MWO-2026-"))
            .Where(x => x.Status == MaintenanceWorkOrderStatus.Completed)
            .Select(x => new { x.SparePartCostAmount, x.CostCurrencyCode })
            .ToArrayAsync();
        Assert.NotEmpty(completed);
        Assert.All(completed, x =>
        {
            Assert.NotNull(x.SparePartCostAmount);
            Assert.True(x.SparePartCostAmount >= 0m);
            Assert.Equal(WorldHistorySparePartSpec.CurrencyCode, x.CostCurrencyCode);
        });
        Assert.Contains(completed, x => x.SparePartCostAmount > 0m);
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Seeded_work_orders_never_leave_domain_events_that_would_move_inventory(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);

        await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        // 备件领用会经 MaintenanceSparePartIssuedIntegrationEventConverter 转成 Inventory 出库集成事件；
        // 历史回填绝不能带出这批事件，否则库存域「现存量 = 世界观流水代数和」的恒等式当场破。
        var pending = db.ChangeTracker.Entries<MaintenanceWorkOrder>()
            .SelectMany(x => x.Entity.GetDomainEvents())
            .ToArray();
        Assert.Empty(pending);
    }

    #endregion

    #region 设备状态投影

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Device_state_projection_covers_every_device_and_agrees_with_open_work_orders(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var db = CreateDbContext();
        var seed = new WorldHistorySeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);
        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, SmallScale);

        var states = await db.MaintenanceDeviceStates.AsNoTracking().ToArrayAsync();
        output.WriteLine($"as-of={asOfDate:yyyy-MM-dd} device-states={states.Length} "
            + $"disabled={states.Count(x => x.Disabled)}");

        // §3 的 46 台设备全覆盖，且与 MasterData 一致——一台都不是停用态。
        Assert.Equal(46, WorldHistoryDeviceSpec.Devices.Count);
        Assert.Equal(46, first.DeviceStatesWritten);
        Assert.Equal(46, states.Length);
        Assert.Equal(46, first.Validation.DeviceStatesChecked);
        Assert.All(states, state => Assert.False(state.Disabled));
        Assert.All(states, state => Assert.Equal(
            WorldHistorySeedService.DeviceStateSourceEventId(state.DeviceAssetId), state.SourceEventId));
        Assert.Equal(
            WorldHistoryDeviceSpec.Devices.Select(x => x.DeviceAssetId).OrderBy(x => x, StringComparer.Ordinal),
            states.Select(x => x.DeviceAssetId).OrderBy(x => x, StringComparer.Ordinal));

        // 幂等：重跑写入量为 0，终态不变。
        Assert.Equal(0, second.DeviceStatesWritten);
        Assert.Equal(46, await db.MaintenanceDeviceStates.CountAsync());

        // 一致性：带在办维修工单的设备不得是停用态（否则其 PM 计划会被静默暂停）。
        var openDevices = await db.MaintenanceWorkOrders
            .AsNoTracking()
            .Where(x => x.Status == MaintenanceWorkOrderStatus.Open)
            .Select(x => x.DeviceAssetId)
            .Distinct()
            .ToArrayAsync();
        var disabledDevices = states.Where(x => x.Disabled).Select(x => x.DeviceAssetId).ToHashSet(StringComparer.Ordinal);
        Assert.All(openDevices, deviceAssetId => Assert.DoesNotContain(deviceAssetId, disabledDevices));
    }

    #endregion

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"maintenance-world-history-spares-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new SparePartTestMediator());
    }

    private sealed class SparePartTestMediator : IMediator
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
