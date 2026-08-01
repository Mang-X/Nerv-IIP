using Nerv.IIP.Business.Wms.Web.Application.Seed;
using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.Wms.Web.Tests;

/// <summary>
/// <c>WorldHistoryCountSpec</c> 的跨服务黄金向量。
///
/// 该规格在仓储域与库存域各存一份逐字相同的副本（两侧不通信、不跨库查询）。
/// 本文件在两个测试工程里也各存一份**逐字相同**的副本：任一侧改了规格而另一侧没跟上，
/// 两边的 <see cref="Digest"/> 会立刻分叉，跨域盘点对账的断裂在门禁上就被拦住，
/// 而不是等到演示当场才发现「同一个 CNT 号在两页上是两笔不同的盘点」。
///
/// 摘要口径：全量（<c>scale=1.0</c>）、<c>asOfDate=2026-07-27</c> 下每条计划的
/// 单号 / 物料 / 库位 / 批次 / 账面量 / 差异量 / 结局，用 FNV-1a 64 折成一个十六进制串。
/// </summary>
internal static class WorldHistoryCountGoldenVector
{
    /// <summary>全量盘点条数（29 周 × 每周 6 个组合，扣掉春节停线两周）。</summary>
    public const int PlanCount = 162;

    /// <summary>全量计划的内容摘要。</summary>
    public const string Digest = "11352FA2FAE09E8C";

    public static string DigestOf(IReadOnlyList<WorldHistoryCountPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        var builder = new StringBuilder(plans.Count * 80);
        foreach (var plan in plans)
        {
            builder.Append(CultureInfo.InvariantCulture,
                $"{plan.CountNo}|{plan.SkuCode}|{plan.UomCode}|{plan.SiteCode}|{plan.LocationCode}|{plan.LotNo}");
            builder.Append(CultureInfo.InvariantCulture,
                $"|{plan.ExpectedQuantity}|{plan.VarianceQuantity}|{plan.Outcome}|{plan.CountDate:yyyy-MM-dd};");
        }

        return WorldHistoryRandom.Fnv1a64(builder.ToString()).ToString("X16", CultureInfo.InvariantCulture);
    }
}
