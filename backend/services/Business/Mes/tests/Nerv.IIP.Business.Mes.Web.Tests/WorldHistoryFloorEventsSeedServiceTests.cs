using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ShiftHandoverAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Seed;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 《工厂世界观设定集》L1「异常与协同」块（停机 / 班次交接 / 车间不良）的形状与幂等性证据。
///
/// 三张表此前恒为 0 行，业务前端「质量与不良 / 设备与停机 / 产能与异常 / 班次交接」四页全空。
/// 断言覆盖：条数区间、号段格式、状态分布、引用完整性、幂等，以及 **5 个 asOfDate 边界**
/// （上线日 / 上线日+1 / 年中 / 演示当天 / 未来日）——单日期测试假绿的教训见 #1151。
/// </summary>
public sealed class WorldHistoryFloorEventsSeedServiceTests
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 26);
    private const double TestScale = 0.02d;

    /// <summary>低于该条数不做分布类断言（上线日附近 × 0.02 缩放只有个位数事实）。</summary>
    private const int MinimumDistributionSample = 10;

    /// <summary>5 个 asOfDate 边界：上线日、上线日+1、年中、演示当天、未来日。</summary>
    public static TheoryData<int, int, int> AsOfDates =>
        new()
        {
            { 2026, 1, 5 },
            { 2026, 1, 6 },
            { 2026, 4, 15 },
            { 2026, 7, 27 },
            { 2026, 12, 31 },
        };

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Floor_event_seed_fills_all_three_tables_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = CreateDbContext();
        await SeedWorkOrderChainAsync(dbContext, asOfDate);

        var report = await new WorldHistoryFloorEventsSeedService(dbContext)
            .SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        // 三张表都不再是 0 行——这正是四页空白的直接原因。
        Assert.True(report.DowntimeEventsWritten > 0);
        Assert.True(report.ShiftHandoversWritten > 0);
        Assert.True(report.DefectRecordsWritten > 0);

        Assert.Equal(
            WorldHistoryFloorEventsSpec.DowntimeEventCount(asOfDate, TestScale),
            await dbContext.WorkCenterUnavailabilities.CountAsync());
        Assert.Equal(
            WorldHistoryFloorEventsSpec.BuildShiftHandovers(asOfDate, TestScale).Count,
            await dbContext.ShiftHandovers.CountAsync());
        Assert.Equal(report.DefectRecordsWritten, await dbContext.DefectRecords.CountAsync());

        // 校验器（fail-closed）跑过并如实回报条数。
        Assert.Equal(report.DowntimeEventsWritten, report.Validation.DowntimeEventsChecked);
        Assert.Equal(report.ShiftHandoversWritten, report.Validation.ShiftHandoversChecked);
        Assert.Equal(report.DefectRecordsWritten, report.Validation.DefectRecordsChecked);
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Floor_event_seed_keeps_number_segments_and_status_distribution_for_any_as_of_date(
        int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = CreateDbContext();
        await SeedWorkOrderChainAsync(dbContext, asOfDate);
        await new WorldHistoryFloorEventsSeedService(dbContext).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        // 号段格式（设定集 §9 的现场事件补充段），且不侵占 L2/规模块。
        var downtimeNos = await dbContext.WorkCenterUnavailabilities.Select(x => x.DowntimeEventNo).ToArrayAsync();
        Assert.All(downtimeNos, no => Assert.Matches(@"^DT-2026-\d{4}$", no));
        var handoverNos = await dbContext.ShiftHandovers.Select(x => x.HandoverNo).ToArrayAsync();
        Assert.All(handoverNos, no => Assert.Matches(@"^HO-2026-\d{5}$", no));
        var defectNos = await dbContext.DefectRecords.Select(x => x.DefectNo).ToArrayAsync();
        Assert.All(defectNos, no => Assert.Matches(@"^DEF-2026-\d{5}$", no));
        Assert.All(
            downtimeNos.Concat(handoverNos).Concat(defectNos),
            no =>
            {
                Assert.DoesNotContain("-DEMO-", no, StringComparison.Ordinal);
                Assert.DoesNotContain("-SCALE-", no, StringComparison.Ordinal);
            });

        // 停机：绝大多数已闭环，最近若干起进行中（页面的「当前停机」）。
        var downtime = await dbContext.WorkCenterUnavailabilities.ToArrayAsync();
        var openDowntime = downtime.Count(x => x.ToUtc is null);
        Assert.Equal(WorldHistoryFloorEventsSpec.OpenDowntimeEventCount(asOfDate, TestScale), openDowntime);
        Assert.True(openDowntime < downtime.Length || downtime.Length == 1);
        Assert.All(downtime, x =>
        {
            Assert.Contains(x.WorkCenterId, WorldHistoryFloorEventsSpec.WorkCenterIds);
            Assert.Matches(@"^DEV-(CNC|GRD|ASM|CTG|TST|PKG)-\d{2}$", x.DeviceAssetId!);
            Assert.Contains(x.Reason, WorldHistoryFloorEventsSpec.DowntimeReasons.Select(reason => reason.Reason));
            Assert.True(x.ToUtc is null || x.ToUtc >= x.FromUtc);
        });

        // 班次交接：除最近一班（末日中班的 3 个班组）外全部已接班。
        var handovers = await dbContext.ShiftHandovers.ToArrayAsync();
        var pending = handovers.Count(x => x.HandoverStatus == ShiftHandover.OpenStatus);
        Assert.Equal(WorldHistoryFloorEventsSpec.Teams.Count(team => team.ShiftIndex == 1), pending);
        Assert.Equal(handovers.Length - pending, handovers.Count(x => x.HandoverStatus == ShiftHandover.AcceptedStatus));
        Assert.All(handovers, x =>
        {
            // 班次与班组是两个维度：班次落 L0 班次编码，班组落班组**编码**（名称另有 TeamName 字段）。
            // 此处原先断言的是反过来的旧语义（班次域放 TeamCode、班组域放班组名称），等于给
            // 「字段装错东西」背书。
            Assert.Contains(
                x.ShiftId,
                WorldHistoryFloorEventsSpec.Teams.Select(team => WorldHistoryCalendar.ShiftCode(team.ShiftIndex)));
            Assert.Contains(x.TeamId, WorldHistoryFloorEventsSpec.Teams.Select(team => team.TeamCode));
            Assert.Contains(x.TeamName, WorldHistoryFloorEventsSpec.Teams.Select(team => team.TeamName));
            Assert.InRange(x.OpenIssueCount, 0, 6);
            Assert.True(x.AcceptedAtUtc is null || x.AcceptedAtUtc >= x.CreatedAtUtc);
        });

        // 车间不良：既有待处置（Open），也有返工/让步/报废的处置结论。
        var defects = await dbContext.DefectRecords.ToArrayAsync();
        if (defects.Length >= MinimumDistributionSample)
        {
            Assert.Contains(defects, x => x.Status == DefectRecord.OpenStatus);
            Assert.Contains(defects, x => x.Status != DefectRecord.OpenStatus);
        }

        Assert.All(defects, x =>
        {
            Assert.Contains(x.DefectCode, WorldHistoryFloorEventsSpec.DefectCodes);
            Assert.True(x.Quantity > 0m);
            Assert.True(
                x.Status is DefectRecord.OpenStatus or DefectRecord.ReworkPendingStatus
                    or DefectRecord.ScrapAcceptedStatus or DefectRecord.DispositionAcceptedStatus,
                $"Unexpected defect status '{x.Status}'.");
            if (x.NcrCode is not null)
            {
                Assert.Matches(@"^NCR-2026-D\d{4}$", x.NcrCode);
            }
        });
    }

    /// <summary>不良必须挂在**库里真实存在**的工单与工序任务上——这是四页数据可下钻的前提。</summary>
    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Defect_records_anchor_on_real_work_orders_and_operation_tasks(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = CreateDbContext();
        await SeedWorkOrderChainAsync(dbContext, asOfDate);
        await new WorldHistoryFloorEventsSeedService(dbContext).SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        var workOrderIds = await dbContext.WorkOrders.Select(x => x.WorkOrderIdValue).ToArrayAsync();
        var taskIds = await dbContext.OperationTasks.Select(x => x.OperationTaskIdValue).ToArrayAsync();
        var workOrderSet = workOrderIds.ToHashSet(StringComparer.Ordinal);
        var taskSet = taskIds.ToHashSet(StringComparer.Ordinal);

        var defects = await dbContext.DefectRecords.ToArrayAsync();
        Assert.NotEmpty(defects);
        Assert.All(defects, defect =>
        {
            Assert.Contains(defect.WorkOrderId, workOrderSet);
            Assert.NotNull(defect.OperationTaskId);
            Assert.Contains(defect.OperationTaskId!, taskSet);
        });

        // 不良沿历史铺开，不堆在少数几张工单上（极小样本除外）。
        if (defects.Length >= MinimumDistributionSample)
        {
            Assert.True(defects.Select(x => x.WorkOrderId).Distinct(StringComparer.Ordinal).Count() > 1);
        }
    }

    [Theory]
    [MemberData(nameof(AsOfDates))]
    public async Task Floor_event_seed_is_idempotent_for_any_as_of_date(int year, int month, int day)
    {
        var asOfDate = new DateOnly(year, month, day);
        await using var dbContext = CreateDbContext();
        await SeedWorkOrderChainAsync(dbContext, asOfDate);
        var seed = new WorldHistoryFloorEventsSeedService(dbContext);

        var first = await seed.SeedAsync("org-001", "env-dev", asOfDate, TestScale);
        var downtimeCount = await dbContext.WorkCenterUnavailabilities.CountAsync();
        var handoverCount = await dbContext.ShiftHandovers.CountAsync();
        var defectCount = await dbContext.DefectRecords.CountAsync();

        var second = await seed.SeedAsync("org-001", "env-dev", asOfDate, TestScale);

        Assert.Equal(0, second.DowntimeEventsWritten);
        Assert.Equal(0, second.ShiftHandoversWritten);
        Assert.Equal(0, second.DefectRecordsWritten);
        Assert.Equal(downtimeCount, await dbContext.WorkCenterUnavailabilities.CountAsync());
        Assert.Equal(handoverCount, await dbContext.ShiftHandovers.CountAsync());
        Assert.Equal(defectCount, await dbContext.DefectRecords.CountAsync());
        Assert.True(first.DowntimeEventsWritten > 0);
    }

    /// <summary>设定集 §7 要求的全量规模：停机 400–800、交接 900–1300、不良 600–1200（scale=1.0）。</summary>
    [Fact]
    public void Full_scale_volumes_match_the_world_bible_shape()
    {
        var asOfDate = new DateOnly(2026, 7, 27);

        Assert.InRange(WorldHistoryFloorEventsSpec.DowntimeEventCount(asOfDate, 1.0d), 400, 800);
        Assert.InRange(WorldHistoryFloorEventsSpec.BuildShiftHandovers(asOfDate, 1.0d).Count, 900, 1300);
        Assert.InRange(WorldHistoryFloorEventsSpec.DefectSlotCount(asOfDate, 1.0d), 600, 1200);

        // 全量时每个生产日 × 6 个班组各一张交接单。
        var productionDays = WorldHistoryFloorEventsSpec.ProductionDays(asOfDate);
        Assert.Equal(productionDays.Count * WorldHistoryFloorEventsSpec.Teams.Count,
            WorldHistoryFloorEventsSpec.BuildShiftHandovers(asOfDate, 1.0d).Count);
        Assert.DoesNotContain(productionDays, day => day.DayOfWeek == DayOfWeek.Sunday);
    }

    /// <summary>处置分布（返工 60 / 让步+筛选 25 / 报废 15）与「约六成有处置」的口径。</summary>
    [Fact]
    public void Defect_disposition_distribution_matches_the_world_bible()
    {
        var slots = WorldHistoryFloorEventsSpec.BuildDefectSlots(new DateOnly(2026, 7, 27), 1.0d);
        var disposed = slots.Where(x => x.IsDisposed).ToArray();

        Assert.InRange(disposed.Length * 100.0 / slots.Count, 55, 75);
        Assert.InRange(disposed.Count(x => x.DispositionType == "rework") * 100.0 / disposed.Length, 50, 70);
        Assert.InRange(disposed.Count(x => x.DispositionType == "scrap") * 100.0 / disposed.Length, 8, 22);
        Assert.InRange(
            disposed.Count(x => x.DispositionType is "conditional-release" or "sort-and-screen") * 100.0 / disposed.Length,
            15, 35);

        // NCR 引用号连续且与质量域的 NCR-2026-#### 段不相交。
        var ncrCodes = disposed.Select(x => x.NcrCode!).ToArray();
        Assert.Equal(ncrCodes.Length, ncrCodes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ncrCodes, code => Assert.Matches(@"^NCR-2026-D\d{4}$", code));
        Assert.All(ncrCodes, code => Assert.DoesNotMatch(@"^NCR-2026-\d{4}$", code));
    }

    /// <summary>班组编码必须与 L1 工单链派工用的班组池逐一对齐，否则交接页的工作中心筛选筛不出东西。</summary>
    [Fact]
    public void Handover_teams_match_the_l0_operator_team_codes()
    {
        var expected = WorldHistoryMesSpec.Operators
            .Select(x => x.TeamCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = WorldHistoryFloorEventsSpec.Teams
            .Select(x => x.TeamCode)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(6, actual.Length);

        // 班组的班次归属与派工池一致（偶数下标早班、奇数中班）。
        foreach (var team in WorldHistoryFloorEventsSpec.Teams)
        {
            var member = WorldHistoryMesSpec.Operators.First(x => x.TeamCode == team.TeamCode);
            Assert.Equal(member.ShiftIndex, team.ShiftIndex);
        }
    }

    /// <summary>工作中心 → 工艺序号映射必须是 <c>WorkCenterCode</c> 的逆映射，否则停机会绑错设备段。</summary>
    [Fact]
    public void Work_center_routing_sequence_is_the_inverse_of_the_l0_mapping()
    {
        Assert.Equal(10, WorldHistoryFloorEventsSpec.RoutingSequence("WC-TUB-01"));
        Assert.Equal(20, WorldHistoryFloorEventsSpec.RoutingSequence("WC-ROD-02"));
        Assert.Equal(30, WorldHistoryFloorEventsSpec.RoutingSequence("WC-GRD-01"));
        Assert.Equal(40, WorldHistoryFloorEventsSpec.RoutingSequence("WC-VA-01"));
        Assert.Equal(50, WorldHistoryFloorEventsSpec.RoutingSequence("WC-RA-02"));
        Assert.Equal(60, WorldHistoryFloorEventsSpec.RoutingSequence("WC-CT-01"));
        Assert.Equal(70, WorldHistoryFloorEventsSpec.RoutingSequence("WC-TS-01"));
        Assert.Equal(80, WorldHistoryFloorEventsSpec.RoutingSequence("WC-PK-01"));

        Assert.All(
            WorldHistoryFloorEventsSpec.WorkCenterIds,
            workCenterId => Assert.Equal(
                workCenterId,
                WorldHistoryMesSpec.WorkCenterCode(
                    SampleSkuFor(workCenterId),
                    WorldHistoryFloorEventsSpec.RoutingSequence(workCenterId))));
    }

    /// <summary>为逆映射断言挑一个会落在该工作中心的成品编码。</summary>
    private static string SampleSkuFor(string workCenterId)
    {
        var candidates = WorldHistorySpec.FinishedGoodSkus;
        foreach (var sku in candidates)
        {
            var sequence = WorldHistoryFloorEventsSpec.RoutingSequence(workCenterId);
            if (WorldHistoryMesSpec.WorkCenterCode(sku, sequence) == workCenterId)
            {
                return sku;
            }
        }

        throw new InvalidOperationException($"No world-bible SKU routes through {workCenterId}.");
    }

    /// <summary>不良必须挂真实工序：工单链没落库时宁可不写，也不造假工单号。</summary>
    [Fact]
    public async Task Defect_records_are_skipped_when_the_work_order_chain_is_missing()
    {
        await using var dbContext = CreateDbContext();

        var report = await new WorldHistoryFloorEventsSeedService(dbContext)
            .SeedAsync("org-001", "env-dev", AsOfDate, TestScale);

        Assert.Equal(0, report.DefectRecordsWritten);
        Assert.True(report.DowntimeEventsWritten > 0);
        Assert.True(report.ShiftHandoversWritten > 0);
    }

    private static async Task SeedWorkOrderChainAsync(ApplicationDbContext dbContext, DateOnly asOfDate) =>
        await new WorldHistorySeedService(dbContext, new StubProductionVersionResolver())
            .SeedAsync("org-001", "env-dev", asOfDate, TestScale);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-world-history-floor-{Guid.CreateVersion7():N}")
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

    private sealed class StubProductionVersionResolver : IWorldHistoryProductionVersionResolver
    {
        public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
            string organizationId,
            string environmentId,
            IReadOnlyCollection<string> skuCodes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                skuCodes.ToDictionary(sku => sku, sku => $"PV-{sku}", StringComparer.Ordinal));
    }
}
