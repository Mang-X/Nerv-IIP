using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.BackorderOrderAggregate;

public partial record BackorderOrderId : IGuidStronglyTypedId;

public enum BackorderOrderStatus
{
    Open = 0,
    Closed = 1,
}

public sealed class BackorderOrder : Entity<BackorderOrderId>, IAggregateRoot
{
    private BackorderOrder()
    {
    }

    private BackorderOrder(
        string organizationId,
        string environmentId,
        string backorderOrderNo,
        string outboundOrderNo,
        string outboundOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string pickLocationCode,
        decimal backorderQuantity)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        BackorderOrderNo = WmsText.Required(backorderOrderNo, nameof(backorderOrderNo));
        OutboundOrderNo = WmsText.Required(outboundOrderNo, nameof(outboundOrderNo));
        OutboundOrderLineNo = WmsText.Required(outboundOrderLineNo, nameof(outboundOrderLineNo));
        SkuCode = WmsText.Required(skuCode, nameof(skuCode));
        UomCode = WmsText.Required(uomCode, nameof(uomCode));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        PickLocationCode = WmsText.Required(pickLocationCode, nameof(pickLocationCode));
        BackorderQuantity = WmsText.Positive(backorderQuantity, nameof(backorderQuantity));
        Status = BackorderOrderStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string BackorderOrderNo { get; private set; } = string.Empty;
    public string OutboundOrderNo { get; private set; } = string.Empty;
    public string OutboundOrderLineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string PickLocationCode { get; private set; } = string.Empty;
    public decimal BackorderQuantity { get; private set; }
    public BackorderOrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ClosureReason { get; private set; }

    public static BackorderOrder Create(
        string organizationId,
        string environmentId,
        string backorderOrderNo,
        string outboundOrderNo,
        string outboundOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string pickLocationCode,
        decimal backorderQuantity) =>
        new(organizationId, environmentId, backorderOrderNo, outboundOrderNo, outboundOrderLineNo, skuCode, uomCode, siteCode, pickLocationCode, backorderQuantity);

    public WarehouseTask CreateReplenishmentRecommendation(string taskNo) =>
        WarehouseTask.CreateReplenishment(
            OrganizationId,
            EnvironmentId,
            taskNo,
            BackorderOrderNo,
            OutboundOrderLineNo,
            SkuCode,
            UomCode,
            SiteCode,
            PickLocationCode,
            BackorderQuantity);

    public void Close(string reason)
    {
        var normalizedReason = WmsText.Required(reason, nameof(reason));
        if (Status == BackorderOrderStatus.Closed)
        {
            if (!string.Equals(ClosureReason, normalizedReason, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Backorder order is already closed with a different reason.");
            }

            return;
        }

        Status = BackorderOrderStatus.Closed;
        ClosureReason = normalizedReason;
        ClosedAtUtc = DateTime.UtcNow;
    }
}
