namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel;

public static class StockOwnerType
{
    public const string Company = "company";
    public const string Customer = "customer";
    public const string Supplier = "supplier";
    public const string Production = "production";
    public const string Maintenance = "maintenance";

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [Company] = Company,
        ["internal"] = Company,
        ["own"] = Company,
        ["owned"] = Company,
        [Customer] = Customer,
        [Supplier] = Supplier,
        ["vendor"] = Supplier,
        [Production] = Production,
        ["manufacturing"] = Production,
        [Maintenance] = Maintenance,
    };

    public static string Normalize(string value, string parameterName = "ownerType")
    {
        var normalized = InventoryText.Required(value);
        return Aliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : throw new ArgumentOutOfRangeException(parameterName, $"Stock owner type '{value}' is not supported.");
    }
}
