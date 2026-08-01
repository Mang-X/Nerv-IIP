using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 「缺的是哪个采购件」的跨服务黄金向量（#1408）。
///
/// <para>
/// 同一个组件码在两个地方各算一次：
/// </para>
/// <list type="bullet">
/// <item>MES 侧：<c>WorldHistoryMesSpec.Components(sku)[WorldHistoryMesSpec.PurchasedComponentIndex]</c>，
///       决定「已下达待开工」档的齐套缺口压在哪个物料上；</item>
/// <item>DemandPlanning 侧：<c>WorldHistoryPlanningSpec.ShortageComponentSkuCode(sku)</c>，
///       决定这批单的 MRP 采购建议**建议采购哪个物料**。</item>
/// </list>
///
/// <para>
/// 两侧只要差一个字，演示走到「MRP 建议采购 → 请购 → 采购订单 → 收货 → 齐套转绿」时就会
/// 出现「建议采购 A、缺的是 B」——而两侧原有的用例都只断言条数与类型，条数恰恰是这种漂移
/// 唯一不改变的量，于是漂移可以在全绿的 CI 下长期存活（审计 T2「复制圈外零保障」）。
/// </para>
///
/// 本文件在 MES 与 DemandPlanning 两个测试工程里各存一份**逐字相同**的副本（仅 namespace 不同）：
/// 一侧改了公式而另一侧没跟上，两边的 <see cref="Digest"/> 会立刻分叉。
/// </summary>
internal static class WorldHistoryShortageComponentGoldenVector
{
    /// <summary>24 个成品，与 L0 <c>WorldBibleSpec.FinishedGoods</c> 同序同码。</summary>
    public static readonly IReadOnlyList<string> FinishedGoodSkus = BuildFinishedGoodSkus();

    /// <summary>「成品 → 缺口采购件」映射的内容摘要。</summary>
    public const string Digest = "7BB45CA222B56931";

    public static string DigestOf(IEnumerable<(string FinishedGoodSku, string ComponentSku)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var builder = new StringBuilder(256);
        foreach (var (finishedGoodSku, componentSku) in pairs)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{finishedGoodSku}|{componentSku};");
        }

        return Fnv1a64(builder.ToString()).ToString("X16", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> BuildFinishedGoodSkus()
    {
        var skus = new List<string>(24);
        foreach (var platform in new[] { "P1", "P2", "S1", "S2", "M1", "E1" })
        {
            foreach (var type in new[] { "QJ", "HJ" })
            {
                foreach (var side in new[] { "L", "R" })
                {
                    skus.Add($"FG-{type}-{platform}-{side}");
                }
            }
        }

        return skus;
    }

    private static ulong Fnv1a64(string value)
    {
        var hash = 0xCBF29CE484222325UL;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 0x100000001B3UL;
        }

        return hash;
    }
}
