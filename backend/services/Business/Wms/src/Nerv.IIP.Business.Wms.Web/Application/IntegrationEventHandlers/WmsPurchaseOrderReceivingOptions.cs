using Nerv.IIP.Contracts.Wms;

namespace Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;

public sealed class WmsPurchaseOrderReceivingOptions
{
    public const string SectionName = "PurchaseOrderReceiving";

    public WmsPurchaseOrderReceivingRoute[] Routes { get; set; } = [];

    public bool IsValid() =>
        Routes.All(route => HasCode(route.OrganizationId) && HasCode(route.EnvironmentId)
            && HasCode(route.SiteCode) && HasCode(route.StagingLocationCode)
            && route.QualityStatus is WmsReceivingQualityStatuses.Quality or WmsReceivingQualityStatuses.Unrestricted
            && route.OwnerType == "company")
        && Routes.Select(route => (route.OrganizationId, route.EnvironmentId, route.SiteCode)).Distinct().Count() == Routes.Length;

    private static bool HasCode(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && value == value.Trim();
}

public sealed class WmsPurchaseOrderReceivingRoute
{
    public string OrganizationId { get; set; } = "";
    public string EnvironmentId { get; set; } = "";
    public string SiteCode { get; set; } = "";
    public string StagingLocationCode { get; set; } = "";
    public string QualityStatus { get; set; } = "";
    public string OwnerType { get; set; } = "";
}
