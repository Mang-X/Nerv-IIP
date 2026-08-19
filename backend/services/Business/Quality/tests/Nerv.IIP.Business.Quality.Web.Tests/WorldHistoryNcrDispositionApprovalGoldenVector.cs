using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 「NCR 处置审批回链」的跨服务黄金向量（#1684）。
///
/// <para>
/// 同一条回链在两个地方各算一次：
/// </para>
/// <list type="bullet">
/// <item>Approval 侧：<c>WorldHistoryApprovalSpec.NcrReferenceCount</c> 给出覆盖条数 K、
///       <c>WorldHistoryApprovalSpec.NonconformanceReportNo</c> 给出单号，
///       种子按 <c>WorldHistoryNcrDispositionApprovals.SeededDispositionChainId</c> 给审批链定 id；</item>
/// <item>Quality 侧：用自己那份 <c>WorldHistoryProcurementSpec</c> 副本复算 K、
///       <c>WorldHistoryPhase2Spec.NonconformanceReportNo</c> 给出单号，
///       按同一确定性公式把链 id 回填到 <c>NonconformanceReport.DispositionApprovalChainId</c>。</item>
/// </list>
///
/// <para>
/// 单号公式、K 下界输入（采购单量副本）、盐串三者任何一侧漂移，回链都会静默指向不存在的链——
/// 而条数类断言恰恰对「id 指错」不敏感。本文件在 Approval 与 Quality 两个测试工程里各存一份
/// **逐字相同**的副本（仅 namespace 不同）：一侧改了公式而另一侧没跟上，两边的
/// <see cref="Digest"/> 会立刻分叉。
/// </para>
/// </summary>
internal static class WorldHistoryNcrDispositionApprovalGoldenVector
{
    /// <summary>标定点：全量周一 / 周一小规模 / 春节段小规模（K=0 的空覆盖点）/ 月末中规模。</summary>
    public static readonly IReadOnlyList<(DateOnly AsOfDate, double Scale)> CalibrationPoints =
    [
        (new DateOnly(2026, 7, 26), 1.0d),
        (new DateOnly(2026, 7, 27), 0.05d),
        (new DateOnly(2026, 2, 16), 0.05d),
        (new DateOnly(2026, 7, 31), 0.2d),
    ];

    /// <summary>「标定点 → 覆盖条数 K → 逐条 (NCR 单号, 链 id)」的内容摘要。</summary>
    public const string Digest = "7B8399503BF69473";

    public static string DigestOf(
        Func<DateOnly, double, int> coveredCount,
        Func<string, Guid> chainId,
        Func<int, string> ncrCode)
    {
        ArgumentNullException.ThrowIfNull(coveredCount);
        ArgumentNullException.ThrowIfNull(chainId);
        ArgumentNullException.ThrowIfNull(ncrCode);
        var builder = new StringBuilder(8192);
        foreach (var (asOfDate, scale) in CalibrationPoints)
        {
            var count = coveredCount(asOfDate, scale);
            builder.Append(CultureInfo.InvariantCulture, $"{asOfDate:yyyy-MM-dd}|{scale:0.##}|{count};");
            for (var sequence = 1; sequence <= count; sequence++)
            {
                var code = ncrCode(sequence);
                builder.Append(CultureInfo.InvariantCulture, $"{code}|{chainId(code):D};");
            }
        }

        return Fnv1a64(builder.ToString()).ToString("X16", CultureInfo.InvariantCulture);
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
