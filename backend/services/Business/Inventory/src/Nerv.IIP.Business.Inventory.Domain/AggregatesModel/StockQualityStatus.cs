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
            : throw new ArgumentOutOfRangeException(parameterName, $"Stock quality status '{value}' is not supported. Supported values are unrestricted, quality, restricted, and blocked.");
    }
}
