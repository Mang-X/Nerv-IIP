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
    InventoryPostingFailed = 2,
    Cancelled = 3,
    InventoryPostingPending = 4,
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
        Version = 1;
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
    public string? CancellationReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public long Version { get; private set; }
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
        decimal quantity,
        string? inventoryReservationId = null,
        string? reservedLocationCode = null,
        string? reservedLotNo = null,
        string? reservedSerialNo = null)
    {
        EnsureOpen();
        var line = FindLine(lineNo);
        EnsurePickingQuantity(line, quantity);

        var taskFromLocationCode = reservedLocationCode ?? fromLocationCode;
        line.MarkPickLocation(taskFromLocationCode);
        line.MarkInventoryReserved(inventoryReservationId, taskFromLocationCode, reservedLotNo, reservedSerialNo);
        AdvanceVersion();
        return WarehouseTask.CreatePicking(
            OrganizationId,
            EnvironmentId,
            taskNo,
            OutboundOrderNo,
            line.LineNo,
            line.SkuCode,
            line.UomCode,
            SiteCode,
            taskFromLocationCode,
            toLocationCode,
            quantity);
    }

    public void EnsureCanCreatePickingTask(string lineNo, decimal quantity)
    {
        EnsureOpen();
        EnsurePickingQuantity(FindLine(lineNo), quantity);
    }

    private static void EnsurePickingQuantity(OutboundOrderLine line, decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new KnownException("Pick quantity must be positive.");
        }

        if (quantity > line.RequestedQuantity)
        {
            throw new KnownException("Pick quantity cannot exceed outbound line quantity.");
        }
    }

    public IReadOnlyCollection<InventoryMovementRequest> CompletePackReview(string packReviewNo, bool passed, string idempotencyKey)
        => CompletePackReview(packReviewNo, passed, idempotencyKey, null);

    public IReadOnlyCollection<InventoryMovementRequest> CompletePackReview(
        string packReviewNo,
        bool passed,
        string idempotencyKey,
        IReadOnlyDictionary<string, decimal>? executedQuantitiesByLine)
    {
        EnsureOpen();
        _ = WmsText.Required(idempotencyKey, nameof(idempotencyKey));
        if (!passed)
        {
            throw new InvalidOperationException("Outbound order cannot complete when pack review failed.");
        }

        EnsureHasLines();
        PackReviewNo = WmsText.Required(packReviewNo, nameof(packReviewNo));
        PackReviewPassed = true;
        Status = OutboundOrderStatus.InventoryPostingPending;
        CompletedAtUtc = null;
        var singleLine = lines.Count == 1;
        foreach (var line in lines)
        {
            line.RecordFulfillment(GetExecutedQuantity(line, executedQuantitiesByLine));
        }

        var postingLines = lines.Where(x => x.IssuedQuantity > 0).ToArray();
        if (postingLines.Length == 0)
        {
            throw new InvalidOperationException("Outbound order cannot complete without executed pick quantity.");
        }

        AdvanceVersion();
        singleLine = postingLines.Length == 1;
        var requests = postingLines.Select(line => InventoryMovementRequest.Create(
                OrganizationId,
                EnvironmentId,
                "outbound",
                OutboundOrderNo,
                line.LineNo,
                singleLine ? idempotencyKey : WmsText.LineIdempotencyKey(idempotencyKey, line.LineNo),
                line.SkuCode,
                line.UomCode,
                SiteCode,
                line.PickLocationCode,
                line.LotNo,
                line.SerialNo,
                line.QualityStatus,
                line.OwnerType,
                line.OwnerId,
                line.IssuedQuantity,
                line.InventoryReservationId))
            .ToArray();
        return requests;
    }

    private static decimal GetExecutedQuantity(OutboundOrderLine line, IReadOnlyDictionary<string, decimal>? executedQuantitiesByLine)
    {
        if (executedQuantitiesByLine is null || !executedQuantitiesByLine.TryGetValue(line.LineNo, out var executedQuantity))
        {
            return line.RequestedQuantity;
        }

        if (executedQuantity < 0)
        {
            throw new InvalidOperationException($"Executed quantity for outbound line '{line.LineNo}' must be within requested quantity.");
        }

        return Math.Min(executedQuantity, line.RequestedQuantity);
    }

    public void MarkInventoryPostingFailed()
    {
        if (Status == OutboundOrderStatus.InventoryPostingFailed)
        {
            return;
        }

        if (Status != OutboundOrderStatus.InventoryPostingPending)
        {
            throw new InvalidOperationException("Only outbound orders with pending Inventory posting can be marked as Inventory posting failed.");
        }

        Status = OutboundOrderStatus.InventoryPostingFailed;
        CompletedAtUtc = null;
        AdvanceVersion();
    }

    public void RecordInventoryPostingProgress()
    {
        if (Status != OutboundOrderStatus.InventoryPostingPending)
        {
            throw new InvalidOperationException("Only outbound orders with pending Inventory posting can record posting progress.");
        }

        AdvanceVersion();
    }

    public void MarkInventoryPostingCompleted()
    {
        if (Status == OutboundOrderStatus.Completed)
        {
            return;
        }

        if (Status != OutboundOrderStatus.InventoryPostingPending)
        {
            throw new InvalidOperationException("Only outbound orders with pending Inventory posting can be completed.");
        }

        Status = OutboundOrderStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        AdvanceVersion();
        this.AddDomainEvent(new OutboundOrderCompletedDomainEvent(this));
    }

    public void EnsureCanCancel()
    {
        EnsureOpen();
    }

    public void Cancel(string reason)
    {
        EnsureCanCancel();
        CancellationReason = WmsText.Required(reason, nameof(reason));
        foreach (var line in lines)
        {
            line.ClearInventoryReservation();
        }

        Status = OutboundOrderStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        AdvanceVersion();
        this.AddDomainEvent(new OutboundOrderCancelledDomainEvent(this));
    }

    public void MarkInventoryReservationReleased(string inventoryReservationId)
    {
        var reservationId = WmsText.Required(inventoryReservationId, nameof(inventoryReservationId));
        var matchingLines = lines.Where(x => x.InventoryReservationId == reservationId).ToArray();
        foreach (var line in matchingLines)
        {
            line.ClearInventoryReservation();
        }

        if (matchingLines.Length > 0)
        {
            AdvanceVersion();
        }
    }

    public void EnsureCanRetryInventoryPosting(IReadOnlyCollection<string> lineNos)
    {
        if (Status != OutboundOrderStatus.InventoryPostingFailed)
        {
            throw new InvalidOperationException("Only outbound orders with failed Inventory posting can be retried.");
        }

        if (lineNos.Count == 0)
        {
            throw new InvalidOperationException("At least one failed outbound line is required for Inventory posting retry.");
        }

        foreach (var lineNo in lineNos)
        {
            var line = FindLine(lineNo);
            if (line.InventoryReservationId is not null)
            {
                throw new InvalidOperationException($"Outbound line '{lineNo}' still has an Inventory reservation and cannot be retried safely.");
            }
        }
    }

    public IReadOnlyCollection<InventoryMovementRequest> RetryInventoryPosting(
        string idempotencyKey,
        IReadOnlyDictionary<string, string?> inventoryReservationIds)
    {
        _ = WmsText.Required(idempotencyKey, nameof(idempotencyKey));
        EnsureCanRetryInventoryPosting(inventoryReservationIds.Keys.ToArray());
        Status = OutboundOrderStatus.InventoryPostingPending;
        CompletedAtUtc = null;
        var retryLines = lines
            .Where(line => inventoryReservationIds.ContainsKey(line.LineNo))
            .OrderBy(line => line.LineNo, StringComparer.Ordinal)
            .ToArray();
        var singleLine = retryLines.Length == 1;
        var requests = retryLines.Select(line =>
            {
                var inventoryReservationId = inventoryReservationIds[line.LineNo];
                line.MarkInventoryReserved(inventoryReservationId);
                return InventoryMovementRequest.Create(
                    OrganizationId,
                    EnvironmentId,
                    "outbound",
                    OutboundOrderNo,
                    line.LineNo,
                    singleLine ? idempotencyKey : WmsText.LineIdempotencyKey(idempotencyKey, line.LineNo),
                    line.SkuCode,
                    line.UomCode,
                    SiteCode,
                    line.PickLocationCode,
                    line.LotNo,
                    line.SerialNo,
                    line.QualityStatus,
                    line.OwnerType,
                    line.OwnerId,
                    line.RequestedQuantity,
                    line.InventoryReservationId);
            })
            .ToArray();
        AdvanceVersion();
        return requests;
    }

    private void AdvanceVersion()
    {
        Version = checked(Version + 1);
    }

    private OutboundOrderLine FindLine(string lineNo)
    {
        return lines.SingleOrDefault(x => x.LineNo == lineNo)
            ?? throw new InvalidOperationException($"Outbound line '{lineNo}' was not found.");
    }

    private void EnsureOpen()
    {
        if (Status != OutboundOrderStatus.Open)
        {
            throw new InvalidOperationException("Completed or failed outbound orders are immutable.");
        }
    }

    private void EnsureHasLines()
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Outbound order must contain at least one line before completion.");
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
    public string? InventoryReservationId { get; private set; }
    public decimal IssuedQuantity { get; private set; }
    public decimal BackorderQuantity { get; private set; }
    public bool FulfillmentRecorded { get; private set; }

    public static OutboundOrderLine Create(OutboundOrderLineDraft draft)
    {
        return new OutboundOrderLine(draft);
    }

    public void MarkInventoryReserved(string? inventoryReservationId, string? locationCode = null, string? lotNo = null, string? serialNo = null)
    {
        if (string.IsNullOrWhiteSpace(inventoryReservationId))
        {
            return;
        }

        var normalizedReservationId = WmsText.Required(inventoryReservationId, nameof(inventoryReservationId));
        if (InventoryReservationId is not null && InventoryReservationId != normalizedReservationId)
        {
            throw new InvalidOperationException("Outbound line already has a different Inventory reservation id.");
        }

        InventoryReservationId = normalizedReservationId;
        if (!string.IsNullOrWhiteSpace(locationCode))
        {
            PickLocationCode = WmsText.Required(locationCode, nameof(locationCode));
        }

        if (!string.IsNullOrWhiteSpace(lotNo))
        {
            LotNo = WmsText.Optional(lotNo);
        }

        if (!string.IsNullOrWhiteSpace(serialNo))
        {
            SerialNo = WmsText.Optional(serialNo);
        }
    }

    public void MarkPickLocation(string pickLocationCode)
    {
        PickLocationCode = WmsText.Required(pickLocationCode, nameof(pickLocationCode));
    }

    public void RecordFulfillment(decimal issuedQuantity)
    {
        if (issuedQuantity < 0 || issuedQuantity > RequestedQuantity)
        {
            throw new InvalidOperationException("Issued quantity must be within requested quantity.");
        }

        IssuedQuantity = issuedQuantity;
        BackorderQuantity = RequestedQuantity - issuedQuantity;
        FulfillmentRecorded = true;
    }

    public void ClearInventoryReservation()
    {
        InventoryReservationId = null;
    }
}
