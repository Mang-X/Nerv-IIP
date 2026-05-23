using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;

public partial record StockLocationId : IGuidStronglyTypedId;

public sealed class StockLocation : Entity<StockLocationId>, IAggregateRoot
{
    private StockLocation()
    {
    }

    private StockLocation(
        string organizationId,
        string environmentId,
        string locationCode,
        string locationType,
        string siteCode,
        string? parentLocationCode,
        string status)
    {
        OrganizationId = InventoryText.Required(organizationId);
        EnvironmentId = InventoryText.Required(environmentId);
        LocationCode = InventoryText.Required(locationCode);
        LocationType = InventoryText.Required(locationType).ToLowerInvariant();
        SiteCode = InventoryText.Required(siteCode);
        ParentLocationCode = InventoryText.Optional(parentLocationCode);
        Status = InventoryText.Required(status).ToLowerInvariant();
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string LocationCode { get; private set; } = string.Empty;
    public string LocationType { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string? ParentLocationCode { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static StockLocation CreateOrUpdate(
        StockLocation? existing,
        string organizationId,
        string environmentId,
        string locationCode,
        string locationType,
        string siteCode,
        string? parentLocationCode,
        string status)
    {
        if (existing is null)
        {
            return new StockLocation(
                organizationId,
                environmentId,
                locationCode,
                locationType,
                siteCode,
                parentLocationCode,
                status);
        }

        existing.Update(locationType, siteCode, parentLocationCode, status);
        return existing;
    }

    private void Update(string locationType, string siteCode, string? parentLocationCode, string status)
    {
        LocationType = InventoryText.Required(locationType).ToLowerInvariant();
        SiteCode = InventoryText.Required(siteCode);
        ParentLocationCode = InventoryText.Optional(parentLocationCode);
        Status = InventoryText.Required(status).ToLowerInvariant();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
