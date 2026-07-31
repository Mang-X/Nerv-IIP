namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

/// <summary>
/// 缺料原因串在 **Scheduling 服务内**的唯一措辞与唯一剥码入口。
///
/// 形态 <c>CODE: 中文事实</c> 是**跨服务约定**（MES 侧的对应实现是
/// <c>MaterialReadinessGuards.FormatShortageReason</c> / <c>DescribeForUser</c>，
/// 前端的解析实现是 <c>useBusinessMes.ts</c> 的 <c>describeMesReadinessReason</c>）。
/// 三处**各自独立实现**——服务边界不共享库，前端更不可能引用后端代码，这是有意的重复；
/// 别再写第四份：本服务内新增缺料串一律走这里，并由
/// <c>SchedulingMaterialReasonTextTests</c> 的格式断言钉住与 MES 侧一致的形态。
///
/// 背景：MAN-698 台账 #35——此前三处各写一套，其中两处直出英文生码
/// 「物料编码 + shortage + 数量」，界面上既读不懂又被徽标截断。
/// </summary>
internal static class SchedulingMaterialReasonText
{
    internal const string ShortageCode = "MATERIAL_SHORTAGE";

    /// <summary>缺料原因串：<c>MATERIAL_SHORTAGE: 物料 X，批次 Y 缺口 N</c>（批次可空）。</summary>
    public static string FormatShortage(string materialId, string? materialLotId, decimal shortageQuantity)
    {
        var lot = string.IsNullOrWhiteSpace(materialLotId) ? string.Empty : $"，批次 {materialLotId}";
        return $"{ShortageCode}: 物料 {materialId}{lot} 缺口 {shortageQuantity:0.######}";
    }

    /// <summary>
    /// 剥掉 <c>CODE: </c> 前缀只留中文事实——原因串上屏前必须过这一道，
    /// 界面上不该出现 MATERIAL_SHORTAGE 这类英文码。中文说明里自带的冒号不当分隔符。
    /// </summary>
    public static string StripCode(string reason)
    {
        var separator = reason.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return reason.Trim();
        }

        var code = reason[..separator];
        return code.All(x => char.IsAsciiLetterUpper(x) || char.IsAsciiDigit(x) || x == '_')
            ? reason[(separator + 1)..].Trim()
            : reason.Trim();
    }

    /// <summary>
    /// 少数上游只给一个**裸码**（既没有 <c>CODE: </c> 前缀也没有中文，如
    /// <c>material-shortage</c> / <c>material.shortage</c>）——这类码剥不掉，
    /// 已知的翻成中文，其余的**丢弃**（见 <see cref="DescribeForUser"/>）。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> BareCodeTranslations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["material-shortage"] = "物料缺料",
            ["material.shortage"] = "物料缺料",
            ["material-requirement-snapshot-missing"] = "齐套需求快照缺失",
        };

    /// <summary>
    /// 把一组原因串整理成**给用户看的**若干句中文（逐条剥码、翻译裸码、去重）。
    ///
    /// 剥完仍然一个中文字都没有的（纯英文码 / 内部标识）**直接丢弃**：它对用户零信息量，
    /// 留在句子里只是噪声——「物料未齐套（material-shortage）」这种就是丢弃前的样子。
    /// 全丢光时调用方会退回不带明细的那句话，仍然是完整、诚实的一句中文。
    /// </summary>
    public static IReadOnlyCollection<string> DescribeForUser(IEnumerable<string> reasons)
    {
        return reasons
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(x => x.Length > 0)
            .Select(x => BareCodeTranslations.TryGetValue(x, out var translated) ? translated : StripCode(x))
            .Where(ContainsChinese)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsChinese(string text)
    {
        return text.Any(x => x >= '一' && x <= '鿿');
    }
}
