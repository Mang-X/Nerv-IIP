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
    /// 给**外部输入落到本词表上的那些调用点**用（HTTP 写面、集成事件消费者）：它们要把非法取值
    /// 表达成 <c>KnownException</c>／400，而不是 <see cref="ArgumentOutOfRangeException"/>／500
    /// ／CAP poison message（#3186）。别名表本身**不放宽**（#2976 裁定仍然成立）——这里只换
    /// 失败的表达方式。
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

    public static string UnsupportedMessage(string value)
    {
        return $"Stock quality status '{value}' is not supported. Supported values are unrestricted, quality, restricted, and blocked.";
    }
}
