using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MasterProductionScheduleAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **计划域侧规格**：
/// 把 ERP/MES 共享的 <see cref="WorldHistorySpec.BuildOrderPlans"/> 订单计划表
/// 投影成需求来源 / 预测 / 主生产计划（MPS）/ MRP 运行与计划建议的确定性事实流。
///
/// 与 ERP/MES 的一致性约定：
/// - 需求来源的 <c>(SourceReference, SourceLineReference)</c> = <c>(SO-2026-#####, "10")</c>，
///   MRP pegging 与 MES 工单 <c>SourcePlanReference.SourceDemandReference = "SO-2026-#####"</c> 对上，
///   行号仍由需求来源自己的 <c>SourceLineReference</c> 保留；
/// - 已接受建议的下游单据号 = <see cref="WorldHistorySpec.WorkOrderNo"/> 同一公式，
///   即 MES 侧真实存在的 <c>WO-2026-#####</c>；
/// - 销售订单建议 ID 由两侧重复声明的确定性算法计算；DemandPlanning 实际落库建议，MES
///   仅引用同一 ID，不凭空制造没有计划建议的来源；
/// - 两侧不通信、不跨库查询、不建跨 schema 外键。
///
/// 计划部人物（4 人）按 MasterData <c>WorldBibleSpec.BuildEmployees()</c> 的同一公式复算：
/// 生产部 3+6+19=28 人在前，计划部从序号 28（0 基）开始 → <c>user-emp-029..032</c>。
/// </summary>
public static class WorldHistoryPlanningSpec
{
    public const string SiteCode = WorldHistorySpec.SiteCode;
    public const string UomCode = WorldHistorySpec.UomCode;
    public const string SalesOrderLineNo = "10";

    /// <summary>需求回看窗口（设定集 §7：近 8–12 周取 10 周）。</summary>
    public const int DemandWindowWeeks = 10;

    /// <summary>每次 MRP 运行的计划展望期（4 周滚动）。</summary>
    public const int MrpHorizonDays = 28;

    /// <summary>建议报废率区间（百分比整数，闭开区间 [2,6) → 2%–5%）。</summary>
    public const int MinScrapPercent = 2;
    public const int MaxScrapPercentExclusive = 6;

    #region 计划部人物（复算 MasterData WorldBibleSpec 公式）

    /// <summary>计划部在员工计划表里的起始序号（0 基）：生产部 3+6+19 = 28 人在前。</summary>
    public const int PlanningDepartmentOrdinalStart = 28;

    private static readonly string[] Surnames =
    [
        "王", "李", "张", "刘", "陈", "杨", "赵", "黄", "周", "吴",
        "徐", "孙", "胡", "朱", "高", "林", "何", "郭", "马", "罗",
    ];

    private static readonly string[] GivenNames =
    [
        "建国", "秀英", "志强", "桂芳", "海涛", "丽娟", "文斌", "春梅", "国庆", "晓东",
        "淑芬", "永强", "秀兰", "俊杰", "玉兰", "小磊", "凤霞", "明辉", "雅琴", "浩然",
        "美玲", "立新", "婷婷", "德华", "红梅", "天宇", "金花", "伟东", "雪梅", "宏伟",
    ];

    /// <summary>计划部 4 人：[0] 计划主管，[1..3] 计划员。</summary>
    public static readonly IReadOnlyList<WorldHistoryPlanner> Planners = BuildPlanners();

    public static WorldHistoryPlanner PlanningSupervisor => Planners[0];

    private static IReadOnlyList<WorldHistoryPlanner> BuildPlanners()
    {
        var planners = new List<WorldHistoryPlanner>(4);
        for (var index = 0; index < 4; index++)
        {
            var ordinal = PlanningDepartmentOrdinalStart + index;
            planners.Add(new WorldHistoryPlanner(
                UserId: $"user-emp-{ordinal + 1:D3}",
                EmployeeNo: $"EMP-{ordinal + 1:D3}",
                Name: $"{Surnames[ordinal % Surnames.Length]}{GivenNames[(ordinal * 7) % GivenNames.Length]}",
                RoleName: index == 0 ? "计划主管" : "计划员"));
        }

        return planners;
    }

    #endregion

    /// <summary>月度预测参考号（自然键）：<c>FC-2026-07-FG-QJ-P1-L</c>。</summary>
    public static string ForecastReference(int year, int month, string skuCode) =>
        string.Create(CultureInfo.InvariantCulture, $"FC-{year}-{month:D2}-{skuCode}");

    /// <summary>
    /// 跨服务历史种子的销售订单计划建议公共 ID。
    /// 两侧必须保持字面量、哈希输入与 GUID 字节处理完全一致。
    /// </summary>
    public static Guid PlanningSuggestionIdForSalesOrder(string salesOrderNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salesOrderNo);
        return StablePlanningSuggestionId(salesOrderNo);
    }

    private static Guid PlanningSuggestionIdForForecast(string forecastReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(forecastReference);
        return StablePlanningSuggestionId($"forecast:{forecastReference}");
    }

    private static Guid StablePlanningSuggestionId(string sourceReference)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes($"nerv-iip:world-history:planning-suggestion:{sourceReference}"));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes.AsSpan(0, 16));
    }

    /// <summary>热销平台（P1/S1）的 8 款成品参与月度预测。</summary>
    public static readonly IReadOnlyList<string> ForecastSkus =
        [.. WorldHistorySpec.FinishedGoodSkus.Where(sku =>
            WorldHistorySpec.HotPlatformCodes.Any(platform => sku.Contains($"-{platform}-", StringComparison.Ordinal)))];

    /// <summary>生成计划域全量事实流：需求来源、预测、MPS 桶、MRP 运行（含建议）。</summary>
    public static WorldHistoryPlanningFacts BuildPlanningFacts(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        if (asOfDate < WorldHistoryCalendar.GoLiveDate)
        {
            asOfDate = WorldHistoryCalendar.GoLiveDate;
        }

        var windowStart = ResolveWindowStart(asOfDate);
        var allPlans = WorldHistorySpec.BuildOrderPlans(asOfDate, scale);
        var plans = allPlans
            .Where(plan => plan.OrderDate >= windowStart)
            .ToArray();
        var historicalPlans = allPlans
            .Where(plan => plan.OrderDate < windowStart)
            .ToArray();

        var demands = BuildDemandFacts(plans);
        var forecasts = BuildForecastFacts(windowStart, asOfDate, scale);
        var mpsBuckets = BuildMpsFacts(plans, asOfDate);
        var mrpRuns = BuildMrpRunFacts(plans, forecasts, asOfDate);
        // 历史批次只补历史订单的 MRP 运行/建议，不把历史需求、MPS 混入当前 10 周活动窗口。
        var historicalMrpRuns = BuildMrpRunFacts(historicalPlans, [], asOfDate);

        return new WorldHistoryPlanningFacts(windowStart, demands, forecasts, mpsBuckets, mrpRuns, historicalMrpRuns);
    }

    /// <summary>窗口起点：包含 asOfDate 的那一周往回 <see cref="DemandWindowWeeks"/> 周，夹到上线日。</summary>
    public static DateOnly ResolveWindowStart(DateOnly asOfDate)
    {
        var currentWeekStart = WeekStartOf(asOfDate);
        var windowStart = currentWeekStart.AddDays(-7 * (DemandWindowWeeks - 1));
        return windowStart < WorldHistoryCalendar.GoLiveDate ? WorldHistoryCalendar.GoLiveDate : windowStart;
    }

    /// <summary>以上线日（周一）为锚的所在周周一。</summary>
    public static DateOnly WeekStartOf(DateOnly date)
    {
        var offset = date.DayNumber - WorldHistoryCalendar.GoLiveDate.DayNumber;
        if (offset < 0)
        {
            return WorldHistoryCalendar.GoLiveDate;
        }

        return WorldHistoryCalendar.GoLiveDate.AddDays((offset / 7) * 7);
    }

    #region 需求来源

    private static IReadOnlyList<WorldHistoryDemandFact> BuildDemandFacts(IReadOnlyList<WorldHistoryOrderPlan> plans)
    {
        var facts = new List<WorldHistoryDemandFact>(plans.Count);
        foreach (var plan in plans)
        {
            var random = new WorldHistoryRandom($"planning-demand:{plan.SalesOrderNo}");
            var createdAtUtc = WorldHistoryCalendar.ShiftMoment(
                WorldHistoryCalendar.SnapToWorkingDay(plan.OrderDate),
                0,
                random.NextInt(0, 480));
            facts.Add(new WorldHistoryDemandFact(plan, createdAtUtc, plan.Stage == WorldHistoryOrderStage.Cancelled));
        }

        return facts;
    }

    #endregion

    #region 月度预测

    private static IReadOnlyList<WorldHistoryForecastFact> BuildForecastFacts(
        DateOnly windowStart,
        DateOnly asOfDate,
        double scale)
    {
        var facts = new List<WorldHistoryForecastFact>();
        // 覆盖窗口起点所在月到 asOfDate 次月：既有历史预测，也有支撑「待处理建议」的未来预测。
        var cursor = new DateOnly(windowStart.Year, windowStart.Month, 1);
        var lastMonth = new DateOnly(asOfDate.Year, asOfDate.Month, 1).AddMonths(1);
        while (cursor <= lastMonth)
        {
            foreach (var skuCode in ForecastSkus)
            {
                var reference = ForecastReference(cursor.Year, cursor.Month, skuCode);
                var random = new WorldHistoryRandom($"planning-forecast:{reference}");
                var baseQuantity = random.NextQuantity(300, 700, 20);
                var quantity = Math.Max(20m, decimal.Round(baseQuantity * (decimal)scale / 10m, 0, MidpointRounding.AwayFromZero) * 10m);
                var periodStart = cursor < WorldHistoryCalendar.GoLiveDate ? WorldHistoryCalendar.GoLiveDate : cursor;
                var periodEnd = cursor.AddMonths(1).AddDays(-1);
                // 预测在上月末由计划员录入；不越过 asOfDate。
                var entryDay = ClampToWindow(cursor.AddDays(-5), asOfDate);
                var createdAtUtc = WorldHistoryCalendar.ShiftMoment(entryDay, 0, random.NextInt(0, 480));
                facts.Add(new WorldHistoryForecastFact(reference, skuCode, periodStart, periodEnd, quantity, createdAtUtc));
            }

            cursor = cursor.AddMonths(1);
        }

        return facts;
    }

    #endregion

    #region MPS 桶

    private static IReadOnlyList<WorldHistoryMpsFact> BuildMpsFacts(
        IReadOnlyList<WorldHistoryOrderPlan> plans,
        DateOnly asOfDate)
    {
        var facts = new List<WorldHistoryMpsFact>();
        foreach (var group in plans
                     .Where(plan => plan.Stage != WorldHistoryOrderStage.Cancelled)
                     .GroupBy(plan => (plan.SkuCode, BucketDate: WeekStartOf(plan.RequiredDate)))
                     .OrderBy(group => group.Key.BucketDate)
                     .ThenBy(group => group.Key.SkuCode, StringComparer.Ordinal))
        {
            var (skuCode, bucketDate) = group.Key;
            var quantity = group.Sum(plan => plan.Quantity);
            var random = new WorldHistoryRandom($"planning-mps:{bucketDate:yyyy-MM-dd}:{skuCode}");

            // 计划提前两周编制该桶；夹到 [上线日, asOfDate] 的工作日。
            var planningDay = ClampToWindow(bucketDate.AddDays(-14), asOfDate);
            var createdAtUtc = WorldHistoryCalendar.ShiftMoment(planningDay, 0, random.NextInt(0, 300));

            // 桶周已开始 → 已发布；下一周 → 已评审；更远 → 草稿。
            var status = bucketDate <= asOfDate
                ? MasterProductionScheduleStatus.Released
                : bucketDate <= asOfDate.AddDays(7)
                    ? MasterProductionScheduleStatus.Reviewed
                    : MasterProductionScheduleStatus.Draft;

            var reviewer = Planners[1 + random.NextInt(0, 3)];
            var reviewedAtUtc = status >= MasterProductionScheduleStatus.Reviewed ? createdAtUtc.AddHours(2) : (DateTimeOffset?)null;
            var releasedAtUtc = status == MasterProductionScheduleStatus.Released ? createdAtUtc.AddHours(4) : (DateTimeOffset?)null;

            facts.Add(new WorldHistoryMpsFact(
                skuCode,
                bucketDate,
                quantity,
                status,
                status >= MasterProductionScheduleStatus.Reviewed ? reviewer.Name : null,
                reviewedAtUtc,
                status == MasterProductionScheduleStatus.Released ? PlanningSupervisor.Name : null,
                releasedAtUtc,
                createdAtUtc));
        }

        return facts;
    }

    #endregion

    #region MRP 运行与建议

    private static IReadOnlyList<WorldHistoryMrpRunFact> BuildMrpRunFacts(
        IReadOnlyList<WorldHistoryOrderPlan> plans,
        IReadOnlyList<WorldHistoryForecastFact> forecasts,
        DateOnly asOfDate)
    {
        var runs = new List<WorldHistoryMrpRunFact>();
        var groups = plans
            .GroupBy(plan => WeekStartOf(plan.OrderDate))
            .OrderBy(group => group.Key)
            .ToArray();
        var latestWeekStart = groups.Length == 0 ? (DateOnly?)null : groups[^1].Key;

        foreach (var group in groups)
        {
            var weekStart = group.Key;
            var runDay = ClampToWindow(group.Max(plan => plan.OrderDate), asOfDate);
            var random = new WorldHistoryRandom($"planning-run:{weekStart:yyyy-MM-dd}");
            var createdAtUtc = WorldHistoryCalendar.ShiftMoment(runDay, 1, random.NextInt(0, 430));
            var startedAtUtc = createdAtUtc.AddMinutes(2);
            var completedAtUtc = createdAtUtc.AddMinutes(17);

            var suggestions = new List<WorldHistorySuggestionFact>();
            foreach (var plan in group.Where(plan => plan.Stage != WorldHistoryOrderStage.Cancelled).OrderBy(plan => plan.Index))
            {
                suggestions.Add(BuildSalesSuggestion(plan, asOfDate, completedAtUtc));
            }

            // 最近一次运行额外产出预测驱动的「待处理」建议（少量未来需求）。
            if (weekStart == latestWeekStart)
            {
                suggestions.AddRange(BuildForecastSuggestions(forecasts, asOfDate, completedAtUtc));
            }

            var activeDemands = group.Where(plan => plan.Stage != WorldHistoryOrderStage.Cancelled).ToArray();
            var inputSources = suggestions
                .Select(suggestion => suggestion.SourceType == "forecast" ? "forecast" : "sales-order")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(source => source, StringComparer.Ordinal)
                .ToArray();

            runs.Add(new WorldHistoryMrpRunFact(
                HorizonStart: weekStart,
                HorizonEnd: weekStart.AddDays(MrpHorizonDays - 1),
                CreatedAtUtc: createdAtUtc,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: completedAtUtc,
                ProductionEngineeringSnapshotSource:
                    string.Create(CultureInfo.InvariantCulture, $"product-engineering-http:{activeDemands.Select(plan => plan.SkuCode).Distinct(StringComparer.Ordinal).Count()}"),
                InventorySnapshotSource:
                    string.Create(CultureInfo.InvariantCulture, $"inventory-availability-http:{activeDemands.Select(plan => plan.SkuCode).Distinct(StringComparer.Ordinal).Count()};scheduled-receipts:none;master-data-planning-parameters:none"),
                InputSources: inputSources,
                InputCoverageStart: activeDemands.Length == 0 ? null : activeDemands.Min(plan => plan.RequiredDate),
                InputCoverageEnd: activeDemands.Length == 0 ? null : activeDemands.Max(plan => plan.RequiredDate),
                DemandCount: activeDemands.Length,
                AvailabilityCount: activeDemands.Select(plan => plan.SkuCode).Distinct(StringComparer.Ordinal).Count(),
                Suggestions: suggestions));
        }

        return runs;
    }

    private static WorldHistorySuggestionFact BuildSalesSuggestion(
        WorldHistoryOrderPlan plan,
        DateOnly asOfDate,
        DateTimeOffset runCompletedAtUtc)
    {
        var random = new WorldHistoryRandom($"planning-suggestion:{plan.SalesOrderNo}");
        var scrapRate = random.NextInt(MinScrapPercent, MaxScrapPercentExclusive) / 100m;
        var onHand = decimal.Round(plan.Quantity * random.NextInt(0, 26) / 100m, 0, MidpointRounding.AwayFromZero);
        var net = plan.Quantity - onHand;
        var planned = Math.Max(1m, decimal.Ceiling(net * (1m + scrapRate)));

        var timeline = WorldHistoryTimeline.For(plan, asOfDate);
        var releaseMoment = WorldHistoryCalendar.ShiftMoment(timeline.WorkOrderReleaseDate, 1, random.NextInt(0, 430));
        var acceptedAtUtc = releaseMoment > runCompletedAtUtc ? releaseMoment : runCompletedAtUtc.AddMinutes(30);

        return new WorldHistorySuggestionFact(
            SuggestionId: PlanningSuggestionIdForSalesOrder(plan.SalesOrderNo),
            DemandSourceReference: plan.SalesOrderNo,
            SourceType: "sales",
            SkuCode: plan.SkuCode,
            GrossQuantity: plan.Quantity,
            OnHandQuantity: onHand,
            NetQuantity: net,
            PlannedQuantity: planned,
            ScrapRate: scrapRate,
            RequiredDate: plan.RequiredDate,
            ReleaseDate: timeline.WorkOrderReleaseDate,
            IsAccepted: true,
            DownstreamDocumentId: plan.WorkOrderNo,
            CreatedAtUtc: runCompletedAtUtc,
            AcceptedAtUtc: acceptedAtUtc);
    }

    private static IEnumerable<WorldHistorySuggestionFact> BuildForecastSuggestions(
        IReadOnlyList<WorldHistoryForecastFact> forecasts,
        DateOnly asOfDate,
        DateTimeOffset runCompletedAtUtc)
    {
        var requiredDate = WorldHistoryCalendar.SnapToWorkingDay(asOfDate.AddDays(14));
        var releaseDate = WorldHistoryCalendar.SnapToWorkingDay(asOfDate.AddDays(7));
        foreach (var skuCode in ForecastSkus)
        {
            var forecast = forecasts.LastOrDefault(candidate =>
                string.Equals(candidate.SkuCode, skuCode, StringComparison.Ordinal) &&
                candidate.PeriodEndDate >= requiredDate);
            if (forecast is null)
            {
                continue;
            }

            var random = new WorldHistoryRandom($"planning-open-suggestion:{forecast.ForecastReference}");
            var planned = Math.Max(20m, decimal.Round(forecast.Quantity * random.NextInt(20, 36) / 100m / 10m, 0, MidpointRounding.AwayFromZero) * 10m);
            yield return new WorldHistorySuggestionFact(
                SuggestionId: PlanningSuggestionIdForForecast(forecast.ForecastReference),
                DemandSourceReference: forecast.ForecastReference,
                SourceType: "forecast",
                SkuCode: skuCode,
                GrossQuantity: planned,
                OnHandQuantity: 0m,
                NetQuantity: planned,
                PlannedQuantity: planned,
                ScrapRate: 0m,
                RequiredDate: requiredDate,
                ReleaseDate: releaseDate,
                IsAccepted: false,
                DownstreamDocumentId: null,
                CreatedAtUtc: runCompletedAtUtc,
                AcceptedAtUtc: null);
        }
    }

    #endregion

    /// <summary>把日期夹到 [上线日, asOfDate] 并落在工作日（越界向前回退）。</summary>
    public static DateOnly ClampToWindow(DateOnly candidate, DateOnly asOfDate)
    {
        var cursor = candidate < WorldHistoryCalendar.GoLiveDate ? WorldHistoryCalendar.GoLiveDate : candidate;
        if (cursor > asOfDate)
        {
            cursor = asOfDate;
        }

        while (!WorldHistoryCalendar.IsWorkingDay(cursor) && cursor > WorldHistoryCalendar.GoLiveDate)
        {
            cursor = cursor.AddDays(-1);
        }

        return WorldHistoryCalendar.SnapToWorkingDay(cursor) <= asOfDate
            ? WorldHistoryCalendar.SnapToWorkingDay(cursor)
            : cursor;
    }
}

/// <summary>计划部人物（复算 MasterData <c>WorldBibleSpec</c> 公式）。</summary>
public sealed record WorldHistoryPlanner(string UserId, string EmployeeNo, string Name, string RoleName);

/// <summary>计划域全量事实流。</summary>
public sealed record WorldHistoryPlanningFacts(
    DateOnly WindowStart,
    IReadOnlyList<WorldHistoryDemandFact> Demands,
    IReadOnlyList<WorldHistoryForecastFact> Forecasts,
    IReadOnlyList<WorldHistoryMpsFact> MpsBuckets,
    IReadOnlyList<WorldHistoryMrpRunFact> MrpRuns,
    IReadOnlyList<WorldHistoryMrpRunFact> HistoricalMrpRuns);

/// <summary>一条销售订单需求来源事实（与 ERP 订单同源）。</summary>
public sealed record WorldHistoryDemandFact(
    WorldHistoryOrderPlan Plan,
    DateTimeOffset CreatedAtUtc,
    bool IsCancelled);

/// <summary>一条月度预测事实。</summary>
public sealed record WorldHistoryForecastFact(
    string ForecastReference,
    string SkuCode,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    decimal Quantity,
    DateTimeOffset CreatedAtUtc);

/// <summary>一个 MPS 周桶事实。</summary>
public sealed record WorldHistoryMpsFact(
    string SkuCode,
    DateOnly BucketDate,
    decimal Quantity,
    MasterProductionScheduleStatus Status,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAtUtc,
    string? ReleasedBy,
    DateTimeOffset? ReleasedAtUtc,
    DateTimeOffset CreatedAtUtc);

/// <summary>一次周度 MRP 运行事实（自然键：HorizonStart）。</summary>
public sealed record WorldHistoryMrpRunFact(
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string ProductionEngineeringSnapshotSource,
    string InventorySnapshotSource,
    IReadOnlyList<string> InputSources,
    DateOnly? InputCoverageStart,
    DateOnly? InputCoverageEnd,
    int DemandCount,
    int AvailabilityCount,
    IReadOnlyList<WorldHistorySuggestionFact> Suggestions);

/// <summary>一条计划建议事实（planned-work-order）。</summary>
public sealed record WorldHistorySuggestionFact(
    Guid SuggestionId,
    string DemandSourceReference,
    string SourceType,
    string SkuCode,
    decimal GrossQuantity,
    decimal OnHandQuantity,
    decimal NetQuantity,
    decimal PlannedQuantity,
    decimal ScrapRate,
    DateOnly RequiredDate,
    DateOnly ReleaseDate,
    bool IsAccepted,
    string? DownstreamDocumentId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AcceptedAtUtc);
