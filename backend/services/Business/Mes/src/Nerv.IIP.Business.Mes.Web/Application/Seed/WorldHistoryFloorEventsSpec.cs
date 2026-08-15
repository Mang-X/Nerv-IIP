namespace Nerv.IIP.Business.Mes.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史的**现场异常与协同**形状：停机事件、班次交接、车间不良。
///
/// 这三张表对应业务前端「异常与协同」四页（质量与不良 / 设备与停机 / 产能与异常 / 班次交接）。
/// 与 <see cref="WorldHistorySpec"/>、<see cref="WorldHistoryMesSpec"/> 同一套约定：
/// **确定性纯函数**，只受 <c>(asOfDate, scale)</c> 两个参数控制，随机源一律走
/// <see cref="WorldHistoryRandom"/> 的业务单号流键，不用 <c>Random.Shared</c> / <c>Guid.NewGuid()</c>。
///
/// 工作中心、设备段、班组编码全部**引用** L0 既有实体（见 <see cref="WorldHistoryMesSpec"/>），
/// 本规格不新建任何主数据。
/// </summary>
public static class WorldHistoryFloorEventsSpec
{
    #region 号段（设定集 §9 的现场事件补充段）

    /// <summary>停机事件号段。</summary>
    public static string DowntimeEventNo(int ordinal) => $"DT-2026-{ordinal:D4}";

    /// <summary>班次交接号段。</summary>
    public static string ShiftHandoverNo(int ordinal) => $"HO-2026-{ordinal:D5}";

    /// <summary>车间不良号段。</summary>
    public static string DefectNo(int ordinal) => $"DEF-2026-{ordinal:D5}";

    /// <summary>
    /// 车间不良直判处置引用的 NCR 号。
    ///
    /// 质量域自己的检验 NCR 走 <c>NCR-2026-####</c>（四位纯数字，由 Quality 侧
    /// <c>WorldHistoryQualitySpec</c> 独占编号）；车间自判处置在其后加 <c>D</c> 前缀分段，
    /// 两段永不相交，避免 MES 侧凭空占用质量域的号码。
    /// </summary>
    public static string DefectNcrReferenceNo(int ordinal) => $"NCR-2026-D{ordinal:D4}";

    /// <summary>本规格产出的全部单号前缀，供隔离性回归测试断言不与 L2/规模块相交。</summary>
    public static readonly string[] NumberSegmentPrefixes = ["DT-2026-", "HO-2026-", "DEF-2026-"];

    #endregion

    #region 生产日历

    /// <summary>上线日到 <paramref name="asOfDate"/> 之间的全部生产日（周日停产保养，设定集 §1）。</summary>
    public static IReadOnlyList<DateOnly> ProductionDays(DateOnly asOfDate)
    {
        if (asOfDate < WorldHistoryCalendar.GoLiveDate)
        {
            return [];
        }

        var days = new List<DateOnly>();
        for (var cursor = WorldHistoryCalendar.GoLiveDate; cursor <= asOfDate; cursor = cursor.AddDays(1))
        {
            if (WorldHistoryCalendar.IsWorkingDay(cursor))
            {
                days.Add(cursor);
            }
        }

        return days;
    }

    /// <summary>历史区间的 UTC 上界（含 <paramref name="asOfDate"/> 当天）。</summary>
    public static DateTimeOffset UpperBound(DateOnly asOfDate) =>
        new(asOfDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

    #endregion

    #region 停机事件（设备与停机页 / 产能与异常页）

    /// <summary>每个生产日平均停机次数——29 周 × 6 天 × 3.5 ≈ 600 起，落在设定集要求的 400–800 区间。</summary>
    public const double DowntimeEventsPerProductionDay = 3.5d;

    /// <summary>保持「进行中」（<c>ToUtc = null</c>）的最新若干起，页面才有「当前停机」可看。</summary>
    public const double OpenDowntimeShare = 0.01d;
    public const int MaxOpenDowntimeEvents = 8;

    /// <summary>L0 §2 的 14 个生产工作中心（规模块 <c>WC-SCALE-*</c> 与固定案例 <c>WC-CNC-DEMO</c> 不在内）。</summary>
    public static readonly IReadOnlyList<string> WorkCenterIds =
    [
        "WC-TUB-01", "WC-TUB-02", "WC-ROD-01", "WC-ROD-02", "WC-GRD-01",
        "WC-VA-01", "WC-FA-01", "WC-FA-02", "WC-FA-03", "WC-RA-01",
        "WC-RA-02", "WC-CT-01", "WC-TS-01", "WC-PK-01",
    ];

    /// <summary>停机原因与时长分布（分钟，闭区间）：换型最频繁、故障最长、保养按计划。</summary>
    public static readonly IReadOnlyList<WorldHistoryDowntimeReason> DowntimeReasons =
    [
        new("换型调整", 20, 60),
        new("设备故障", 60, 360),
        new("缺料待工", 15, 120),
        new("计划保养", 120, 240),
        new("质量停机", 30, 150),
    ];

    private static readonly IReadOnlyList<int> DowntimeReasonWeights = [35, 25, 20, 12, 8];

    /// <summary>本次生成的停机事件总数。</summary>
    public static int DowntimeEventCount(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var days = ProductionDays(asOfDate).Count;
        if (days == 0)
        {
            return 0;
        }

        var baseline = (int)Math.Round(days * DowntimeEventsPerProductionDay, MidpointRounding.AwayFromZero);
        return Math.Max(1, (int)Math.Round(baseline * scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>本次生成中保持进行中的停机起数。</summary>
    public static int OpenDowntimeEventCount(DateOnly asOfDate, double scale)
    {
        var total = DowntimeEventCount(asOfDate, scale);
        if (total == 0)
        {
            return 0;
        }

        var open = Math.Clamp((int)Math.Round(total * OpenDowntimeShare, MidpointRounding.AwayFromZero), 1, MaxOpenDowntimeEvents);
        return Math.Min(open, total);
    }

    /// <summary>生成全量停机事件：跨 29 周摊在 14 个工作中心上，绑设定集 §3 的设备段。</summary>
    public static IReadOnlyList<WorldHistoryDowntimeEvent> BuildDowntimeEvents(DateOnly asOfDate, double scale)
    {
        var days = ProductionDays(asOfDate);
        var total = DowntimeEventCount(asOfDate, scale);
        if (days.Count == 0 || total == 0)
        {
            return [];
        }

        var openCount = OpenDowntimeEventCount(asOfDate, scale);
        var closedThrough = total - openCount;
        var upperBound = UpperBound(asOfDate);
        var events = new List<WorldHistoryDowntimeEvent>(total);

        for (var ordinal = 1; ordinal <= total; ordinal++)
        {
            var downtimeEventNo = DowntimeEventNo(ordinal);
            var random = new WorldHistoryRandom($"downtime:{downtimeEventNo}");

            // 单号顺序即时间顺序：末尾若干起落在最近的生产日上，于是「当前停机」总是最新的。
            var dayIndex = total <= 1 ? days.Count - 1 : (ordinal - 1) * days.Count / total;
            var day = days[Math.Clamp(dayIndex, 0, days.Count - 1)];

            var workCenterId = random.Pick(WorkCenterIds);
            var deviceAssetId = WorldHistoryMesSpec.DeviceAssetCode(RoutingSequence(workCenterId), random);
            var reason = random.PickWeighted(DowntimeReasons, DowntimeReasonWeights);
            var shiftIndex = random.NextInt(0, 2);
            var minutesIntoShift = random.NextInt(0, WorldHistoryCalendar.ShiftLengthHours * 60);
            var fromUtc = WorldHistoryCalendar.ShiftMoment(day, shiftIndex, minutesIntoShift);
            var durationMinutes = random.NextInt(reason.MinimumMinutes, reason.MaximumMinutes + 1);

            DateTimeOffset? toUtc = null;
            if (ordinal <= closedThrough)
            {
                var restored = fromUtc.AddMinutes(durationMinutes);
                toUtc = restored > upperBound ? upperBound : restored;
            }

            events.Add(new WorldHistoryDowntimeEvent(
                downtimeEventNo,
                workCenterId,
                deviceAssetId,
                reason.Reason,
                fromUtc,
                toUtc));
        }

        return events;
    }

    /// <summary>工作中心 → 工艺序号（与 <see cref="WorldHistoryMesSpec.WorkCenterCode"/> 互为逆映射），决定可用设备段。</summary>
    public static int RoutingSequence(string workCenterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workCenterId);
        return workCenterId switch
        {
            "WC-TUB-01" or "WC-TUB-02" => 10,
            "WC-ROD-01" or "WC-ROD-02" => 20,
            "WC-GRD-01" => 30,
            "WC-VA-01" => 40,
            "WC-FA-01" or "WC-FA-02" or "WC-FA-03" or "WC-RA-01" or "WC-RA-02" => 50,
            "WC-CT-01" => 60,
            "WC-TS-01" => 70,
            "WC-PK-01" => 80,
            _ => throw new ArgumentOutOfRangeException(nameof(workCenterId), workCenterId, "Unknown world-bible work center."),
        };
    }

    #endregion

    #region 班次交接（班次交接页）

    /// <summary>
    /// L0 §5 的 6 个班组。班组与班次是**两个维度**：班组是「谁」（<c>TEAM-WB-*</c>），班次是「何时」
    /// （<c>EARLY</c> / <c>MIDDLE</c>），每个班组各自引用一个班次。历史工序任务的 <c>shift_id</c> 落班次编码、
    /// <c>team_id</c> 落班组编码，交接单据据此与工序任务对得上。
    /// </summary>
    public static readonly IReadOnlyList<WorldHistoryShiftTeam> Teams =
    [
        new("TEAM-WB-MC-A", "机加车间早班组", 0),
        new("TEAM-WB-MC-B", "机加车间中班组", 1),
        new("TEAM-WB-AS-A", "装配车间早班组", 0),
        new("TEAM-WB-AS-B", "装配车间中班组", 1),
        new("TEAM-WB-SP-A", "表面与包装车间早班组", 0),
        new("TEAM-WB-SP-B", "表面与包装车间中班组", 1),
    ];

    /// <summary>多数班次留 0–2 个未闭环问题，少数班次积压 3–6 个。</summary>
    public const double QuietHandoverProbability = 0.75d;

    /// <summary>
    /// 生成全量班次交接：每个生产日 × 6 个班组各一张。
    /// <paramref name="scale"/> &lt; 1 时按固定步长抽样生产日（末日必留，页面要看到「当班」）。
    /// </summary>
    public static IReadOnlyList<WorldHistoryShiftHandover> BuildShiftHandovers(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var days = ProductionDays(asOfDate);
        if (days.Count == 0)
        {
            return [];
        }

        var step = Math.Max(1, (int)Math.Round(1d / scale, MidpointRounding.AwayFromZero));
        var sampledDays = new List<DateOnly>();
        for (var index = 0; index < days.Count; index += step)
        {
            sampledDays.Add(days[index]);
        }

        if (sampledDays[^1] != days[^1])
        {
            sampledDays.Add(days[^1]);
        }

        var lastDay = sampledDays[^1];
        var handovers = new List<WorldHistoryShiftHandover>(sampledDays.Count * Teams.Count);
        var ordinal = 0;

        foreach (var day in sampledDays)
        {
            foreach (var team in Teams)
            {
                ordinal++;
                var handoverNo = ShiftHandoverNo(ordinal);
                var random = new WorldHistoryRandom($"handover:{handoverNo}");

                var openIssueCount = random.Chance(QuietHandoverProbability)
                    ? random.NextInt(0, 3)
                    : random.NextInt(3, 7);

                // 交接发生在班次收班后 5–15 分钟。
                var createdAtUtc = WorldHistoryCalendar
                    .ShiftEnd(day, team.ShiftIndex)
                    .AddMinutes(random.NextInt(5, 16));

                // 最近一班（末日中班）尚未接班，页面才有「待接班」；其余全部已接班。
                var isPending = day == lastDay && team.ShiftIndex == 1;
                var acceptedAtUtc = isPending
                    ? (DateTimeOffset?)null
                    : createdAtUtc.AddMinutes(random.NextInt(5, 26));

                handovers.Add(new WorldHistoryShiftHandover(
                    handoverNo,
                    ShiftId: WorldHistoryCalendar.ShiftCode(team.ShiftIndex),
                    TeamId: team.TeamCode,
                    openIssueCount,
                    createdAtUtc,
                    acceptedAtUtc,
                    TeamName: team.TeamName));
            }
        }

        return handovers;
    }

    #endregion

    #region 车间不良（质量与不良页 / 产能与异常页）

    /// <summary>每个生产日平均不良记录数——29 周 × 6 天 × 5 ≈ 870 条，落在设定集要求的 600–1200 区间。</summary>
    public const double DefectRecordsPerProductionDay = 5.0d;

    /// <summary>有处置结论的比例（其余留 Open，是车间当前待办）。</summary>
    public const double DisposedDefectProbability = 0.65d;

    /// <summary>L0 工艺对应的 6 类现场不良。</summary>
    public static readonly IReadOnlyList<string> DefectCodes =
    [
        "尺寸超差", "表面划伤", "渗漏", "焊缝缺陷", "涂层不均", "压装力不足",
    ];

    private static readonly IReadOnlyList<int> DefectCodeWeights = [30, 22, 16, 12, 12, 8];

    /// <summary>处置分布（设定集 §7）：返工 60% / 让步放行 + 筛选 25% / 报废 15%。</summary>
    public static readonly IReadOnlyList<string> DispositionTypes =
    [
        "rework", "conditional-release", "sort-and-screen", "scrap",
    ];

    private static readonly IReadOnlyList<int> DispositionWeights = [60, 13, 12, 15];

    private static readonly IReadOnlyList<decimal> DefectQuantities = [1m, 2m, 3m, 5m, 8m];

    private static readonly IReadOnlyList<int> DefectQuantityWeights = [40, 25, 20, 10, 5];

    /// <summary>本次生成的不良记录槽位数（实际落库条数还受可挂靠的工序任务数限制）。</summary>
    public static int DefectSlotCount(DateOnly asOfDate, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var days = ProductionDays(asOfDate).Count;
        if (days == 0)
        {
            return 0;
        }

        var baseline = (int)Math.Round(days * DefectRecordsPerProductionDay, MidpointRounding.AwayFromZero);
        return Math.Max(1, (int)Math.Round(baseline * scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// 生成不良记录的「槽位」：编号、不良代码、数量与处置结论。
    /// 槽位不含工单/工序引用——那必须绑到**库里真实存在**的工序任务上，由 SeedService 完成绑定。
    /// </summary>
    public static IReadOnlyList<WorldHistoryDefectSlot> BuildDefectSlots(DateOnly asOfDate, double scale)
    {
        var total = DefectSlotCount(asOfDate, scale);
        if (total == 0)
        {
            return [];
        }

        var slots = new List<WorldHistoryDefectSlot>(total);
        var ncrOrdinal = 0;

        for (var ordinal = 1; ordinal <= total; ordinal++)
        {
            var defectNo = DefectNo(ordinal);
            var random = new WorldHistoryRandom($"defect:{defectNo}");
            var defectCode = random.PickWeighted(DefectCodes, DefectCodeWeights);
            var quantity = random.PickWeighted(DefectQuantities, DefectQuantityWeights);
            var disposed = random.Chance(DisposedDefectProbability);
            var dispositionType = disposed ? random.PickWeighted(DispositionTypes, DispositionWeights) : null;
            var dispositionDelayMinutes = random.NextInt(60, 2881);
            var ncrCode = disposed ? DefectNcrReferenceNo(++ncrOrdinal) : null;

            slots.Add(new WorldHistoryDefectSlot(
                defectNo,
                defectCode,
                quantity,
                ncrCode,
                dispositionType,
                dispositionDelayMinutes));
        }

        return slots;
    }

    #endregion
}

/// <summary>停机原因及其时长分布（分钟，闭区间）。</summary>
public sealed record WorldHistoryDowntimeReason(string Reason, int MinimumMinutes, int MaximumMinutes);

/// <summary>一起历史停机事件。</summary>
public sealed record WorldHistoryDowntimeEvent(
    string DowntimeEventNo,
    string WorkCenterId,
    string DeviceAssetId,
    string Reason,
    DateTimeOffset FromUtc,
    DateTimeOffset? ToUtc)
{
    /// <summary>是否仍在停机中（页面的「当前停机」）。</summary>
    public bool IsOpen => ToUtc is null;
}

/// <summary>L0 §5 的一个班组（即本平台的班次维度）。</summary>
public sealed record WorldHistoryShiftTeam(string TeamCode, string TeamName, int ShiftIndex);

/// <summary>一张历史班次交接单。</summary>
public sealed record WorldHistoryShiftHandover(
    string HandoverNo,
    string ShiftId,
    string TeamId,
    int OpenIssueCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AcceptedAtUtc,
    string? TeamName = null)
{
    /// <summary>是否尚未接班。</summary>
    public bool IsPending => AcceptedAtUtc is null;
}

/// <summary>一条历史车间不良的确定性内容（工单/工序引用由 SeedService 绑到真实行上）。</summary>
public sealed record WorldHistoryDefectSlot(
    string DefectNo,
    string DefectCode,
    decimal Quantity,
    string? NcrCode,
    string? DispositionType,
    int DispositionDelayMinutes)
{
    /// <summary>是否已有处置结论。</summary>
    public bool IsDisposed => DispositionType is not null;
}
