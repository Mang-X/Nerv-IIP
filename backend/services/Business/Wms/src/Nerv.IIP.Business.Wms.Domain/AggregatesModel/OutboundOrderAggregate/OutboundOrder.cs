using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;

public partial record OutboundOrderId : IGuidStronglyTypedId;

public partial record OutboundOrderLineId : IGuidStronglyTypedId;

public enum OutboundOrderStatus
{
    Open = 0,
    Completed = 1,
}

public sealed record OutboundOrderLineDraft(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal RequestedQuantity,
    string PickLocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId);

public sealed class OutboundOrder : Entity<OutboundOrderId>, IAggregateRoot
{
    private readonly List<OutboundOrderLine> lines = [];

    private OutboundOrder()
    {
    }

    private OutboundOrder(
        string organizationId,
        string environmentId,
        string outboundOrderNo,
        string sourceDocumentType,
        string sourceDocumentId,
        string siteCode,
        IEnumerable<OutboundOrderLineDraft> lineDrafts)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        OutboundOrderNo = WmsText.Required(outboundOrderNo, nameof(outboundOrderNo));
        SourceDocumentType = WmsText.Required(sourceDocumentType, nameof(sourceDocumentType));
        SourceDocumentId = WmsText.Required(sourceDocumentId, nameof(sourceDocumentId));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        Status = OutboundOrderStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
        foreach (var draft in lineDrafts)
        {
            lines.Add(OutboundOrderLine.Create(draft));
        }

        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one outbound line is required.", nameof(lineDrafts));
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string OutboundOrderNo { get; private set; } = string.Empty;
    public string SourceDocumentType { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public OutboundOrderStatus Status { get; private set; }
    public string? PackReviewNo { get; private set; }
    public bool? PackReviewPassed { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public IReadOnlyCollection<OutboundOrderLine> Lines => lines;

    public static OutboundOrder Create(
        string organizationId,
        string environmentId,
        string outboundOrderNo,
        string sourceDocumentType,
        string sourceDocumentId,
        string siteCode,
        IEnumerable<OutboundOrderLineDraft> lines)
    {
        return new OutboundOrder(organizationId, environmentId, outboundOrderNo, sourceDocumentType, sourceDocumentId, siteCode, lines);
    }

    public WarehouseTask CreatePickingTask(
        string taskNo,
        string lineNo,
        string fromLocationCode,
        string toLocationCode,
        decimal quantity)
    {
        EnsureOpen();
        var line = FindLine(lineNo);
        if (quantity > line.RequestedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Pick quantity cannot exceed outbound line quantity.");
        }

        return WarehouseTask.CreatePicking(
            OrganizationId,
            EnvironmentId,
            taskNo,
            OutboundOrderNo,
            line.LineNo,
            line.SkuCode,
            line.UomCode,
            SiteCode,
            fromLocationCode,
            toLocationCode,
            quantity);
    }

    public InventoryMovementRequest CompletePackReview(string packReviewNo, bool passed, string idempotencyKey)
    {
        EnsureOpen();
        _ = WmsText.Required(idempotencyKey, nameof(idempotencyKey));
        if (!passed)
        {
            throw new InvalidOperationException("Outbound order cannot complete when pack review failed.");
        }

        var line = lines[0];
        PackReviewNo = WmsText.Required(packReviewNo, nameof(packReviewNo));
        PackReviewPassed = true;
        Status = OutboundOrderStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        var request = InventoryMovementRequest.Create(
            OrganizationId,
            EnvironmentId,
            "outbound",
            OutboundOrderNo,
            line.LineNo,
            idempotencyKey,
            line.SkuCode,
            line.UomCode,
            SiteCode,
            line.PickLocationCode,
            line.LotNo,
            line.SerialNo,
            line.QualityStatus,
            line.OwnerType,
            line.OwnerId,
            line.RequestedQuantity);
        this.AddDomainEvent(new OutboundOrderCompletedDomainEvent(this));
        return request;
    }

    private OutboundOrderLine FindLine(string lineNo)
    {
        return lines.SingleOrDefault(x => x.LineNo == lineNo)
            ?? throw new InvalidOperationException($"Outbound line '{lineNo}' was not found.");
    }

    private void EnsureOpen()
    {
        if (Status == OutboundOrderStatus.Completed)
        {
            throw new InvalidOperationException("Completed outbound orders are immutable.");
        }
    }
}

public sealed class OutboundOrderLine : Entity<OutboundOrderLineId>
{
    private OutboundOrderLine()
    {
    }

    private OutboundOrderLine(OutboundOrderLineDraft draft)
    {
        LineNo = WmsText.Required(draft.LineNo, nameof(draft.LineNo));
        SkuCode = WmsText.Required(draft.SkuCode, nameof(draft.SkuCode));
        UomCode = WmsText.Required(draft.UomCode, nameof(draft.UomCode));
        RequestedQuantity = WmsText.Positive(draft.RequestedQuantity, nameof(draft.RequestedQuantity));
        PickLocationCode = WmsText.Required(draft.PickLocationCode, nameof(draft.PickLocationCode));
        LotNo = WmsText.Optional(draft.LotNo);
        SerialNo = WmsText.Optional(draft.SerialNo);
        QualityStatus = WmsText.Required(draft.QualityStatus, nameof(draft.QualityStatus)).ToLowerInvariant();
        OwnerType = WmsText.Required(draft.OwnerType, nameof(draft.OwnerType)).ToLowerInvariant();
        OwnerId = WmsText.Optional(draft.OwnerId);
    }

    public string LineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal RequestedQuantity { get; private set; }
    public string PickLocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string QualityStatus { get; private set; } = string.Empty;
    public string OwnerType { get; private set; } = string.Empty;
    public string? OwnerId { get; private set; }

    public static OutboundOrderLine Create(OutboundOrderLineDraft draft)
    {
        return new OutboundOrderLine(draft);
    }
}
