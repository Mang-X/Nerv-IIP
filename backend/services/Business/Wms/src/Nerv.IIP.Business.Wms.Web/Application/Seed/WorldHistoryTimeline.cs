namespace Nerv.IIP.Business.Wms.Web.Application.Seed;

/// <summary>
/// 单张历史订单的时间轴（设定集 §7「历史时间戳贯穿一致」）。
///
/// 这是 ERP 与 MES 的**第二个共享契约**：MES 的完工时间必须早于 ERP 的发货时间，
/// ERP 的收款时间必须晚于发货时间，全链单调。两侧按同一字面量重复声明并各有黄金向量测试。
/// 所有日期都落在工作日（周日停产），且不越过 <c>asOfDate</c>。
/// </summary>
public sealed record WorldHistoryTimeline(
    DateOnly OrderDate,
    DateOnly WorkOrderReleaseDate,
    DateOnly ProductionStartDate,
    DateOnly ProductionCompletionDate,
    DateOnly ShipDate,
    DateOnly CollectionDate)
{
    /// <summary>
    /// 按订单计划推导时间轴。所有节点顺序推进、逐个夹到 <paramref name="asOfDate"/> 以内，
    /// 因此即使最近下的单也不会出现「未来的报工」。
    /// </summary>
    public static WorldHistoryTimeline For(WorldHistoryOrderPlan plan, DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var random = new WorldHistoryRandom($"timeline:{plan.SalesOrderNo}");

        var orderDate = Clamp(WorldHistoryCalendar.SnapToWorkingDay(plan.OrderDate), asOfDate);
        var releaseDate = Clamp(WorldHistoryCalendar.AddWorkingDays(orderDate, random.NextInt(1, 4)), asOfDate);
        var startDate = Clamp(WorldHistoryCalendar.AddWorkingDays(releaseDate, random.NextInt(1, 3)), asOfDate);

        // 产出天数随批量走：小批 3–6 个工作日，大批可到 12 个工作日。
        var productionDays = plan.Quantity <= 120m
            ? random.NextInt(3, 7)
            : plan.Quantity <= 320m
                ? random.NextInt(5, 10)
                : random.NextInt(8, 13);
        var completionDate = Clamp(WorldHistoryCalendar.AddWorkingDays(startDate, productionDays), asOfDate);

        var shipDate = Clamp(WorldHistoryCalendar.AddWorkingDays(completionDate, random.NextInt(1, 4)), asOfDate);
        var collectionDate = Clamp(
            WorldHistoryCalendar.SnapToWorkingDay(shipDate.AddDays(random.NextInt(20, 56))),
            asOfDate);

        return new WorldHistoryTimeline(
            orderDate,
            releaseDate,
            startDate,
            completionDate,
            shipDate,
            collectionDate);
    }

    private static DateOnly Clamp(DateOnly candidate, DateOnly asOfDate)
    {
        if (candidate <= asOfDate)
        {
            return candidate;
        }

        // 越界时回退到 asOfDate 当天或其前最近的工作日，保证「不早于上线日、不晚于今天」。
        var cursor = asOfDate;
        while (!WorldHistoryCalendar.IsWorkingDay(cursor) && cursor > WorldHistoryCalendar.GoLiveDate)
        {
            cursor = cursor.AddDays(-1);
        }

        return cursor;
    }
}
