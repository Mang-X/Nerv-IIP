using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel;

public static class StockQualityStatus
{
    public const string Unrestricted = InventoryQualityStatuses.Unrestricted;
    public const string Quality = InventoryQualityStatuses.Quality;
    public const string Restricted = InventoryQualityStatuses.Restricted;
    public const string Blocked = InventoryQualityStatuses.Blocked;

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [InventoryQualityStatuses.Unrestricted] = InventoryQualityStatuses.Unrestricted,
        ["qualified"] = InventoryQualityStatuses.Unrestricted,
        ["available"] = InventoryQualityStatuses.Unrestricted,
        [InventoryQualityStatuses.Quality] = InventoryQualityStatuses.Quality,
        ["inspection"] = InventoryQualityStatuses.Quality,
        ["quality-inspection"] = InventoryQualityStatuses.Quality,
        [InventoryQualityStatuses.Restricted] = InventoryQualityStatuses.Restricted,
        ["conditional-release"] = InventoryQualityStatuses.Restricted,
        [InventoryQualityStatuses.Blocked] = InventoryQualityStatuses.Blocked,
        ["rejected"] = InventoryQualityStatuses.Blocked,
    };

    public static string Normalize(string value, string parameterName = "qualityStatus")
    {
        var normalized = InventoryText.Required(value);
        return Aliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : throw new ArgumentOutOfRangeException(parameterName, UnsupportedMessage(value));
    }

    /// <summary>
    /// 规范化取值；不认识的取值返回 <c>false</c> 而不抛异常。
    ///
    /// 给**外部输入落到本词表上的那些调用点**用（HTTP 写面、集成事件消费者）：非法取值是调用方的
    /// 输入错误，应当表达成 <c>KnownException</c>，而不是 <see cref="ArgumentOutOfRangeException"/>
    /// ——后者在 HTTP 写面上表现为 <c>500 /「未知错误」</c>（#3186）。
    ///
    /// 别名表本身**不放宽**（#2976 裁定仍然成立）——这里只换失败的表达方式。
    /// **换类型不等于换投递结局**：在 CAP 消费路径上两种异常同样逃逸出消费者，见
    /// <c>QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer</c> 里的说明与 #877。
    /// </summary>
    public static bool TryNormalize(string value, out string canonical)
    {
        if (!string.IsNullOrWhiteSpace(value) && Aliases.TryGetValue(value.Trim(), out var resolved))
        {
            canonical = resolved;
            return true;
        }

        canonical = string.Empty;
        return false;
    }

    /// <summary>
    /// 失败消息。空白与「有值但不认识」是两种输入错误，分开措辞——
    /// 对空值说 "'' is not supported" 是不贴切的（#3186 复审）。
    /// 只在这里分支，不新增抛出点：<c>InventoryKnownExceptionMessageArchitectureTests</c>
    /// 的闭合台账按抛出点计数。
    /// </summary>
    public static string UnsupportedMessage(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "Stock quality status is required."
            : $"Stock quality status '{value}' is not supported. Supported values are unrestricted, quality, restricted, and blocked.";
    }
}
