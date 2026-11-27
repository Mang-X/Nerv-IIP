namespace Nerv.IIP.Business.Wms.Infrastructure.IntegrationEvents;

public sealed record PurchaseOrderCancellation(
    string OrganizationId,
    string EnvironmentId,
    string PurchaseOrderNo,
    string PurchaseOrderId,
    string SiteCode,
    int PurchaseOrderVersion,
    DateTimeOffset CancelledAtUtc,
    string Reason);
