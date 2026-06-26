using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;

public partial record InboundOrderId : IGuidStronglyTypedId;

public partial record InboundOrderLineId : IGuidStronglyTypedId;

public enum InboundOrderStatus
{
    Open = 0,
    Completed = 1,
    InventoryPostingFailed = 2,
}

public sealed record InboundOrderLineDraft(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal ReceivedQuantity,
    string StagingLocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId);

public sealed class InboundOrder : Entity<InboundOrderId>, IAggregateRoot
{
    private readonly List<InboundOrderLine> lines = [];

    private InboundOrder()
    {
    }

    private InboundOrder(
        string organizationId,
        string environmentId,
        string inboundOrderNo,
        string sourceDocumentType,
        string sourceDocumentId,
        string siteCode,
        IEnumerable<InboundOrderLineDraft> lineDrafts)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        InboundOrderNo = WmsText.Required(inboundOrderNo, nameof(inboundOrderNo));
        SourceDocumentType = WmsText.Required(sourceDocumentType, nameof(sourceDocumentType));
        SourceDocumentId = WmsText.Required(sourceDocumentId, nameof(sourceDocumentId));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        Status = InboundOrderStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
        foreach (var draft in lineDrafts)
        {
            lines.Add(InboundOrderLine.Create(draft));
        }

        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one inbound line is required.", nameof(lineDrafts));
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string InboundOrderNo { get; private set; } = string.Empty;
    public string SourceDocumentType { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public InboundOrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public IReadOnlyCollection<InboundOrderLine> Lines => lines;

    public static InboundOrder Create(
        string organizationId,
        string environmentId,
        string inboundOrderNo,
        string sourceDocumentType,
        string sourceDocumentId,
        string siteCode,
        IEnumerable<InboundOrderLineDraft> lines)
    {
        return new InboundOrder(organizationId, environmentId, inboundOrderNo, sourceDocumentType, sourceDocumentId, siteCode, lines);
    }

    public WarehouseTask CreatePutawayTask(
        string taskNo,
        string lineNo,
        string fromLocationCode,
        string toLocationCode,
        decimal quantity)
    {
        EnsureOpen();
        var line = FindLine(lineNo);
        if (quantity > line.ReceivedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Putaway quantity cannot exceed inbound line quantity.");
        }

        return WarehouseTask.CreatePutaway(
            OrganizationId,
            EnvironmentId,
            taskNo,
            InboundOrderNo,
            line.LineNo,
            line.SkuCode,
            line.UomCode,
            SiteCode,
            fromLocationCode,
            toLocationCode,
            quantity);
    }

    public IReadOnlyCollection<InventoryMovementRequest> Complete(string idempotencyKey)
    {
        EnsureOpen();
        _ = WmsText.Required(idempotencyKey, nameof(idempotencyKey));
        Status = InboundOrderStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        var singleLine = lines.Count == 1;
        var requests = lines.Select(line => InventoryMovementRequest.Create(
                OrganizationId,
                EnvironmentId,
                "inbound",
                InboundOrderNo,
                line.LineNo,
                singleLine ? idempotencyKey : BuildLineIdempotencyKey(idempotencyKey, line.LineNo),
                line.SkuCode,
                line.UomCode,
                SiteCode,
                line.StagingLocationCode,
                line.LotNo,
                line.SerialNo,
                line.QualityStatus,
                line.OwnerType,
                line.OwnerId,
                line.ReceivedQuantity))
            .ToArray();
        this.AddDomainEvent(new InboundOrderCompletedDomainEvent(this));
        return requests;
    }

    public void MarkInventoryPostingFailed()
    {
        if (Status == InboundOrderStatus.InventoryPostingFailed)
        {
            return;
        }

        if (Status != InboundOrderStatus.Completed)
        {
            throw new InvalidOperationException("Only completed inbound orders can be marked as Inventory posting failed.");
        }

        Status = InboundOrderStatus.InventoryPostingFailed;
    }

    private InboundOrderLine FindLine(string lineNo)
    {
        return lines.SingleOrDefault(x => x.LineNo == lineNo)
            ?? throw new InvalidOperationException($"Inbound line '{lineNo}' was not found.");
    }

    private void EnsureOpen()
    {
        if (Status != InboundOrderStatus.Open)
        {
            throw new InvalidOperationException("Completed or failed inbound orders are immutable.");
        }
    }

    private static string BuildLineIdempotencyKey(string idempotencyKey, string lineNo)
    {
        var candidate = $"{idempotencyKey}:{lineNo}";
        if (candidate.Length <= 128)
        {
            return candidate;
        }

        var raw = $"{idempotencyKey}:{lineNo}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return $"wms-line:{hash}";
    }
}

public sealed class InboundOrderLine : Entity<InboundOrderLineId>
{
    private InboundOrderLine()
    {
    }

    private InboundOrderLine(InboundOrderLineDraft draft)
    {
        LineNo = WmsText.Required(draft.LineNo, nameof(draft.LineNo));
        SkuCode = WmsText.Required(draft.SkuCode, nameof(draft.SkuCode));
        UomCode = WmsText.Required(draft.UomCode, nameof(draft.UomCode));
        ReceivedQuantity = WmsText.Positive(draft.ReceivedQuantity, nameof(draft.ReceivedQuantity));
        StagingLocationCode = WmsText.Required(draft.StagingLocationCode, nameof(draft.StagingLocationCode));
        LotNo = WmsText.Optional(draft.LotNo);
        SerialNo = WmsText.Optional(draft.SerialNo);
        QualityStatus = WmsText.Required(draft.QualityStatus, nameof(draft.QualityStatus)).ToLowerInvariant();
        OwnerType = WmsText.Required(draft.OwnerType, nameof(draft.OwnerType)).ToLowerInvariant();
        OwnerId = WmsText.Optional(draft.OwnerId);
    }

    public string LineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal ReceivedQuantity { get; private set; }
    public string StagingLocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string QualityStatus { get; private set; } = string.Empty;
    public string OwnerType { get; private set; } = string.Empty;
    public string? OwnerId { get; private set; }

    public static InboundOrderLine Create(InboundOrderLineDraft draft)
    {
        return new InboundOrderLine(draft);
    }
}
