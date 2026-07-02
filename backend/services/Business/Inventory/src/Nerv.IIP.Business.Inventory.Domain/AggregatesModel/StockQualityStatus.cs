namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel;

public static class StockQualityStatus
{
    public const string Unrestricted = "unrestricted";
    public const string Quality = "quality";
    public const string Restricted = "restricted";
    public const string Blocked = "blocked";

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [Unrestricted] = Unrestricted,
        ["qualified"] = Unrestricted,
        ["available"] = Unrestricted,
        [Quality] = Quality,
        ["inspection"] = Quality,
        ["quality-inspection"] = Quality,
        [Restricted] = Restricted,
        ["conditional-release"] = Restricted,
        [Blocked] = Blocked,
        ["rejected"] = Blocked,
    };

    public static string Normalize(string value, string parameterName = "qualityStatus")
    {
        var normalized = InventoryText.Required(value);
        return Aliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : throw new ArgumentOutOfRangeException(parameterName, $"Stock quality status '{value}' is not supported. Supported values are unrestricted, quality, restricted, and blocked.");
    }
}
