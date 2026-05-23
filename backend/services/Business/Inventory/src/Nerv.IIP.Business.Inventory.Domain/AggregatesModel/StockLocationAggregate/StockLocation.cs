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
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        LocationCode = Required(locationCode);
        LocationType = Required(locationType).ToLowerInvariant();
        SiteCode = Required(siteCode);
        ParentLocationCode = Optional(parentLocationCode);
        Status = Required(status).ToLowerInvariant();
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
        LocationType = Required(locationType).ToLowerInvariant();
        SiteCode = Required(siteCode);
        ParentLocationCode = Optional(parentLocationCode);
        Status = Required(status).ToLowerInvariant();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
