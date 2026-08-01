using Nerv.IIP.Business.Mes.Web.Application.Seed;
using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 补产工单「来源订单」配对的跨服务黄金向量（#1374）。
///
/// 补产工单在两个地方各挑一次来源单，用的是同一条按下标切片的公式
/// <c>candidates[(seq-1) * candidates.Length / reworkCount]</c>：
/// <list type="bullet">
/// <item>MES 侧：<c>WorldHistorySeedService.SeedReworkWorkOrdersAsync</c>，结果落进
///       <c>work_orders.source_plan_reference</c>；</item>
/// <item>Inventory / Wms / Quality / BarcodeLabel 侧：四份 <c>WorldHistoryPhase2Spec.BuildWorkOrderFacts</c>。</item>
/// </list>
///
/// <para>
/// **两侧的候选池判据只要差一个订单，下标就整体错位**，同一个 <c>WO-2026-R####</c>
/// 会在两边指向不同的来源订单，SKU / 数量 / 时间线全不一致。而两侧原有的用例都只断言**条数**
/// ——条数恰恰是唯一不受影响的量，于是这类漂移可以在全绿的 CI 下长期存活
/// （审计 T2「复制圈外零保障」，已立案 #1388）。
/// </para>
///
/// 本文件在 MES 与 WMS 两个测试工程里各存一份**逐字相同**的副本：一侧改了判据而另一侧没跟上，
/// 两边的 <see cref="Digest"/> 会立刻分叉。
/// </summary>
internal static class WorldHistoryReworkSourceGoldenVector
{
    /// <summary>黄金向量的取样点：与 MES 用例既有的 asOfDate / scale 一致。</summary>
    public static readonly DateOnly AsOfDate = new(2026, 7, 26);

    public const double Scale = 0.02d;

    /// <summary>该取样点下的补产工单条数。</summary>
    public const int ReworkCount = 8;

    /// <summary>「补产工单号 → 来源工单号」配对的内容摘要。</summary>
    public const string Digest = "5824B9A85B755D1F";

    /// <summary>把配对压成摘要。入参必须按补产工单号升序。</summary>
    public static string DigestOf(IEnumerable<(string ReworkWorkOrderNo, string SourceWorkOrderNo)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var builder = new StringBuilder(256);
        foreach (var (reworkWorkOrderNo, sourceWorkOrderNo) in pairs)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{reworkWorkOrderNo}|{sourceWorkOrderNo};");
        }

        return WorldHistoryRandom.Fnv1a64(builder.ToString()).ToString("X16", CultureInfo.InvariantCulture);
    }
}
