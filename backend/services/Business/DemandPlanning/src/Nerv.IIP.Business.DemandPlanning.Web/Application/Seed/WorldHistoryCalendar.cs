namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§1 工作制 + §7 历史节奏的工作日历。
///
/// - 上线日 2026-01-05（周一），电子历史自此开始；
/// - 周日停产保养（设定集 §1），周一至周六为工作日；
/// - 春节 2026-02-09–02-22 低谷；
/// - 月末冲量（每月最后 5 个自然日所在周放大）；
/// - 早班 08:00–16:00 / 中班 16:00–24:00（Asia/Shanghai，UTC+8），
///   即 UTC 的 00:00–08:00 与 08:00–16:00——同一 UTC 自然日内，跨时区不会把工序甩到班次外。
///
/// ERP 与 MES 两侧按同一字面量重复声明，两侧各有黄金向量测试防止漂移。
/// </summary>
public static class WorldHistoryCalendar
{
    /// <summary>平台上线日（设定集 §1），与 L0 <c>WorldBibleSpec.GoLiveDate</c> 同值。</summary>
    public static readonly DateOnly GoLiveDate = new(2026, 1, 5);

    /// <summary>春节低谷区间（含端点），设定集 §7。</summary>
    public static readonly DateOnly SpringFestivalStart = new(2026, 2, 9);
    public static readonly DateOnly SpringFestivalEnd = new(2026, 2, 22);

    /// <summary>周均销售订单量（设定集 §7：105±30）。</summary>
    public const int BaseWeeklyOrders = 105;
    public const int WeeklyJitter = 30;

    /// <summary>春节周产出系数与月末冲量系数（设定集 §7 的「低谷 / 冲量」）。</summary>
    public const double SpringFestivalFactor = 0.35;
    public const double MonthEndSurgeFactor = 1.35;

    /// <summary>厂区时区偏移（Asia/Shanghai 全年 UTC+8，无夏令时）。</summary>
    public static readonly TimeSpan SiteUtcOffset = TimeSpan.FromHours(8);

    /// <summary>早班 08:00–16:00（本地），中班 16:00–24:00（本地）。</summary>
    public const int EarlyShiftStartLocalHour = 8;
    public const int MiddleShiftStartLocalHour = 16;
    public const int ShiftLengthHours = 8;

    /// <summary>周日停产保养。</summary>
    public static bool IsWorkingDay(DateOnly date) => date.DayOfWeek != DayOfWeek.Sunday;

    /// <summary>落到当天或其后的第一个工作日。</summary>
    public static DateOnly SnapToWorkingDay(DateOnly date)
    {
        var cursor = date;
        while (!IsWorkingDay(cursor))
        {
            cursor = cursor.AddDays(1);
        }

        return cursor;
    }

    /// <summary>推进 <paramref name="workingDays"/> 个工作日（0 表示落到当天或其后第一个工作日）。</summary>
    public static DateOnly AddWorkingDays(DateOnly date, int workingDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(workingDays);
        var cursor = SnapToWorkingDay(date);
        for (var step = 0; step < workingDays; step++)
        {
            cursor = SnapToWorkingDay(cursor.AddDays(1));
        }

        return cursor;
    }

    /// <summary>是否落在春节低谷区间内。</summary>
    public static bool IsSpringFestival(DateOnly date) => date >= SpringFestivalStart && date <= SpringFestivalEnd;

    /// <summary>是否属于月末冲量窗口（当月最后 5 个自然日）。</summary>
    public static bool IsMonthEndSurge(DateOnly date) =>
        date.Day > DateTime.DaysInMonth(date.Year, date.Month) - 5;

    /// <summary>第 <paramref name="weekIndex"/> 周（0 基）的周一。</summary>
    public static DateOnly WeekStart(int weekIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(weekIndex);
        return GoLiveDate.AddDays(7 * weekIndex);
    }

    /// <summary>上线日到 <paramref name="today"/> 之间已完整或部分开始的周数。</summary>
    public static int WeekCount(DateOnly today)
    {
        if (today < GoLiveDate)
        {
            return 0;
        }

        return ((today.DayNumber - GoLiveDate.DayNumber) / 7) + 1;
    }

    /// <summary>
    /// 第 <paramref name="weekIndex"/> 周的销售订单量：基准 105，叠加春节低谷 / 月末冲量系数与 ±30 抖动，
    /// 再按 <paramref name="scale"/> 缩放。缩放后至少 1 单，保证 <c>Scale=0.1</c> 的快速验证不出现空周。
    /// </summary>
    public static int WeeklyOrderVolume(int weekIndex, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var weekStart = WeekStart(weekIndex);
        var factor = 1.0;
        if (WeekOverlapsSpringFestival(weekStart))
        {
            factor *= SpringFestivalFactor;
        }

        if (WeekContainsMonthEnd(weekStart))
        {
            factor *= MonthEndSurgeFactor;
        }

        var random = new WorldHistoryRandom($"week:{weekIndex:D3}");
        var jitter = random.NextInt(-WeeklyJitter, WeeklyJitter + 1);
        var shaped = (int)Math.Round((BaseWeeklyOrders * factor) + jitter, MidpointRounding.AwayFromZero);
        var scaled = (int)Math.Round(Math.Max(shaped, 10) * scale, MidpointRounding.AwayFromZero);
        return Math.Max(scaled, 1);
    }

    /// <summary>本周（周一起 6 个工作日）是否与春节低谷区间相交。</summary>
    public static bool WeekOverlapsSpringFestival(DateOnly weekStart) =>
        weekStart <= SpringFestivalEnd && weekStart.AddDays(6) >= SpringFestivalStart;

    /// <summary>本周是否包含某个月的月末冲量窗口。</summary>
    public static bool WeekContainsMonthEnd(DateOnly weekStart)
    {
        for (var offset = 0; offset < 7; offset++)
        {
            if (IsMonthEndSurge(weekStart.AddDays(offset)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 把「工作日 + 班次 + 班内分钟」映射为 UTC 时刻。
    /// <paramref name="shiftIndex"/>：0 = 早班（本地 08:00 起），1 = 中班（本地 16:00 起）。
    /// </summary>
    public static DateTimeOffset ShiftMoment(DateOnly workingDay, int shiftIndex, int minutesIntoShift)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(shiftIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(shiftIndex, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(minutesIntoShift);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minutesIntoShift, ShiftLengthHours * 60);

        var localStartHour = shiftIndex == 0 ? EarlyShiftStartLocalHour : MiddleShiftStartLocalHour;
        var localStart = workingDay.ToDateTime(new TimeOnly(localStartHour, 0));
        return new DateTimeOffset(localStart, SiteUtcOffset).AddMinutes(minutesIntoShift).ToUniversalTime();
    }

    /// <summary>该工作日的班次收班时刻（UTC），用于给工序时间设上界。</summary>
    public static DateTimeOffset ShiftEnd(DateOnly workingDay, int shiftIndex) =>
        ShiftMoment(workingDay, shiftIndex, (ShiftLengthHours * 60) - 1);
}
