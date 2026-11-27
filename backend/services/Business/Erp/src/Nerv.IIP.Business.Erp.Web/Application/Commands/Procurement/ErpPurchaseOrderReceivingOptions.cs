using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;

public sealed class ErpPurchaseOrderReceivingOptions
{
    public const string SectionName = "PurchaseOrderReceiving";

    public ErpPurchaseOrderReceivingScope[] WmsScopes { get; init; } = [];

    internal PurchaseReceiptInventoryPostingRoute Resolve(string organizationId, string environmentId, string siteCode) =>
        WmsScopes.Any(scope => scope.OrganizationId == organizationId.Trim()
            && scope.EnvironmentId == environmentId.Trim() && scope.SiteCode == siteCode.Trim())
            ? PurchaseReceiptInventoryPostingRoute.Wms
            : PurchaseReceiptInventoryPostingRoute.Direct;
}

public sealed record ErpPurchaseOrderReceivingScope(string OrganizationId, string EnvironmentId, string SiteCode);
