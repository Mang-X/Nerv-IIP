using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.SupplierReturnAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Contracts.Wms;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;

public partial record InboundOrderId : IGuidStronglyTypedId;

public partial record InboundOrderLineId : IGuidStronglyTypedId;

public enum InboundOrderStatus
{
    Open = 0,
    Completed = 1,
    InventoryPostingFailed = 2,
    PendingQualityCheck = 3,
    Cancelled = 4,
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
    string? OwnerId,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null);

public sealed record InboundOrderLineCapture(
    string LineNo,
    string? LotNo,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate);

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
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
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

    public void Cancel(string reason)
    {
        EnsureOpen();
        CancellationReason = WmsText.Required(reason, nameof(reason));
        CancelledAtUtc = DateTime.UtcNow;
        Status = InboundOrderStatus.Cancelled;
    }

    public WarehouseTask CreatePutawayTask(
        string taskNo,
        string lineNo,
        string fromLocationCode,
        string toLocationCode,
        decimal quantity)
    {
        var line = FindLine(lineNo);
        EnsureCanCreatePutawayTask(line);
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

    public IReadOnlyCollection<InventoryMovementRequest> Complete(
        string idempotencyKey,
        IReadOnlyCollection<InboundOrderLineCapture>? captures = null)
    {
        EnsureOpen();
        _ = WmsText.Required(idempotencyKey, nameof(idempotencyKey));
        EnsureHasLines();
        var validatedCaptures = ValidateCaptures(captures);
        foreach (var (line, capture) in validatedCaptures)
        {
            line.Capture(capture);
        }

        Status = lines.Any(x => x.RequiresQualityInspection)
            ? InboundOrderStatus.PendingQualityCheck
            : InboundOrderStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        var singleLine = lines.Count == 1;
        var requests = lines.Select(line => InventoryMovementRequest.Create(
                OrganizationId,
                EnvironmentId,
                "inbound",
                InboundOrderNo,
                line.LineNo,
                singleLine ? idempotencyKey : WmsText.LineIdempotencyKey(idempotencyKey, line.LineNo),
                line.SkuCode,
                line.UomCode,
                SiteCode,
                line.StagingLocationCode,
                line.LotNo,
                line.SerialNo,
                line.ReceiptQualityStatus,
                line.OwnerType,
                line.OwnerId,
                line.ReceivedQuantity,
                ProductionDate: line.ProductionDate,
                ExpiryDate: line.ExpiryDate))
            .ToArray();
        this.AddDomainEvent(new InboundOrderCompletedDomainEvent(this));
        return requests;
    }

    public SupplierReturnRequest? ApplyInspectionResult(
        string eventType,
        string inspectionRecordId,
        string skuCode,
        string? lotNo,
        string? serialNo,
        decimal inspectedQuantity,
        string? dispositionReason)
    {
        if (Status != InboundOrderStatus.PendingQualityCheck && Status != InboundOrderStatus.Completed)
        {
            throw new InvalidOperationException("Quality inspection result can only be applied after inbound completion.");
        }

        var line = FindInspectionLine(skuCode, lotNo, serialNo);
        var result = InboundQualityInspectionResult.FromEventType(eventType);
        line.ApplyInspectionResult(result.GateStatus, inspectionRecordId, inspectedQuantity, dispositionReason);
        if (lines.All(x => !x.RequiresQualityInspection || x.HasQualityResult))
        {
            Status = InboundOrderStatus.Completed;
        }

        if (result.GateStatus != InboundQualityGateStatuses.Rejected)
        {
            return null;
        }

        return SupplierReturnRequest.Create(
            OrganizationId,
            EnvironmentId,
            InboundOrderNo,
            line.LineNo,
            inspectionRecordId,
            line.SkuCode,
            line.UomCode,
            SiteCode,
            line.StagingLocationCode,
            line.LotNo,
            line.SerialNo,
            line.OwnerType,
            line.OwnerId,
            inspectedQuantity,
            dispositionReason);
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

    public IReadOnlyCollection<InventoryMovementRequest> RetryInventoryPosting(string idempotencyKey)
    {
        if (Status != InboundOrderStatus.InventoryPostingFailed)
        {
            throw new InvalidOperationException("Only inbound orders with failed Inventory posting can be retried.");
        }

        _ = WmsText.Required(idempotencyKey, nameof(idempotencyKey));
        EnsureHasLines();
        Status = InboundOrderStatus.Completed;
        var singleLine = lines.Count == 1;
        var requests = lines.Select(line => InventoryMovementRequest.Create(
                OrganizationId,
                EnvironmentId,
                "inbound",
                InboundOrderNo,
                line.LineNo,
                singleLine ? idempotencyKey : WmsText.LineIdempotencyKey(idempotencyKey, line.LineNo),
                line.SkuCode,
                line.UomCode,
                SiteCode,
                line.StagingLocationCode,
                line.LotNo,
                line.SerialNo,
                line.ReceiptQualityStatus,
                line.OwnerType,
                line.OwnerId,
                line.ReceivedQuantity,
                ProductionDate: line.ProductionDate,
                ExpiryDate: line.ExpiryDate))
            .ToArray();
        return requests;
    }

    private InboundOrderLine FindLine(string lineNo)
    {
        return lines.SingleOrDefault(x => x.LineNo == lineNo)
            ?? throw new InvalidOperationException($"Inbound line '{lineNo}' was not found.");
    }

    private InboundOrderLine FindInspectionLine(string skuCode, string? lotNo, string? serialNo)
    {
        var normalizedSkuCode = WmsText.Required(skuCode, nameof(skuCode));
        var normalizedLotNo = WmsText.Optional(lotNo);
        var normalizedSerialNo = WmsText.Optional(serialNo);
        var matches = lines
            .Where(x => x.SkuCode == normalizedSkuCode
                && x.LotNo == normalizedLotNo
                && x.SerialNo == normalizedSerialNo)
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException("Quality inspection result cannot resolve exactly one inbound line.");
    }

    private void EnsureCanCreatePutawayTask(InboundOrderLine line)
    {
        if (Status == InboundOrderStatus.Open)
        {
            if (line.RequiresQualityInspection)
            {
                throw new InvalidOperationException("Inbound line is pending quality inspection and cannot be put away.");
            }

            return;
        }

        if (line.QualityGateStatus == InboundQualityGateStatuses.Rejected)
        {
            throw new InvalidOperationException("Rejected inbound line cannot be put away.");
        }

        if (Status == InboundOrderStatus.PendingQualityCheck)
        {
            if (!line.RequiresQualityInspection || line.IsReleasedForPutaway)
            {
                return;
            }

            throw new InvalidOperationException("Inbound line is pending quality inspection and cannot be put away.");
        }

        if (Status == InboundOrderStatus.Completed && line.IsReleasedForPutaway)
        {
            return;
        }

        throw Status == InboundOrderStatus.InventoryPostingFailed
            ? new InvalidOperationException("Inbound orders with failed Inventory posting cannot be put away.")
            : new InvalidOperationException("Completed inbound orders are immutable.");
    }

    private void EnsureOpen()
    {
        if (Status != InboundOrderStatus.Open)
        {
            throw new InvalidOperationException("Completed or failed inbound orders are immutable.");
        }
    }

    private void EnsureHasLines()
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("Inbound order must contain at least one line before completion.");
        }
    }

    private IReadOnlyCollection<(InboundOrderLine Line, InboundOrderLineCapture Capture)> ValidateCaptures(
        IReadOnlyCollection<InboundOrderLineCapture>? captures)
    {
        if (captures is null || captures.Count == 0)
        {
            return [];
        }

        var linesByNumber = lines.ToDictionary(x => x.LineNo, StringComparer.Ordinal);
        var normalizedLineNumbers = new HashSet<string>(StringComparer.Ordinal);
        var validatedCaptures = new List<(InboundOrderLine, InboundOrderLineCapture)>(captures.Count);
        foreach (var capture in captures)
        {
            var normalizedLineNo = WmsText.Required(capture.LineNo, nameof(capture.LineNo));
            if (!normalizedLineNumbers.Add(normalizedLineNo))
            {
                throw new InvalidOperationException($"Inbound line '{normalizedLineNo}' was captured more than once.");
            }

            if (capture.ProductionDate.HasValue
                && capture.ExpiryDate.HasValue
                && capture.ProductionDate.Value > capture.ExpiryDate.Value)
            {
                throw new InvalidOperationException($"Inbound line '{normalizedLineNo}' production date cannot be after its expiry date.");
            }

            if (!linesByNumber.TryGetValue(normalizedLineNo, out var line))
            {
                throw new InvalidOperationException($"Inbound line '{normalizedLineNo}' was not found.");
            }

            validatedCaptures.Add((line, capture with { LineNo = normalizedLineNo }));
        }

        return validatedCaptures;
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
        QualityGateStatus = WmsReceivingQualityStatuses.RequiresInspection(QualityStatus)
            ? InboundQualityGateStatuses.Pending
            : InboundQualityGateStatuses.NotRequired;
        OwnerType = WmsText.Required(draft.OwnerType, nameof(draft.OwnerType)).ToLowerInvariant();
        OwnerId = WmsText.Optional(draft.OwnerId);
        ProductionDate = draft.ProductionDate;
        ExpiryDate = draft.ExpiryDate;
    }

    public string LineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal ReceivedQuantity { get; private set; }
    public string StagingLocationCode { get; private set; } = string.Empty;
    public string? LotNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string QualityStatus { get; private set; } = string.Empty;
    public string QualityGateStatus { get; private set; } = InboundQualityGateStatuses.Pending;
    public string? InspectionRecordId { get; private set; }
    public string? QualityDispositionReason { get; private set; }
    public string OwnerType { get; private set; } = string.Empty;
    public string? OwnerId { get; private set; }
    public DateOnly? ProductionDate { get; private set; }
    public DateOnly? ExpiryDate { get; private set; }
    public bool RequiresQualityInspection => QualityGateStatus != InboundQualityGateStatuses.NotRequired;
    public bool HasQualityResult => QualityGateStatus is InboundQualityGateStatuses.Passed or InboundQualityGateStatuses.ConditionalReleased or InboundQualityGateStatuses.Rejected;
    public bool IsReleasedForPutaway => QualityGateStatus is InboundQualityGateStatuses.Passed or InboundQualityGateStatuses.ConditionalReleased;
    public string ReceiptQualityStatus => RequiresQualityInspection ? "quality" : "unrestricted";

    public static InboundOrderLine Create(InboundOrderLineDraft draft)
    {
        return new InboundOrderLine(draft);
    }

    internal void Capture(InboundOrderLineCapture capture)
    {
        LotNo = WmsText.Optional(capture.LotNo);
        ProductionDate = capture.ProductionDate;
        ExpiryDate = capture.ExpiryDate;
    }

    internal void ApplyInspectionResult(
        string gateStatus,
        string inspectionRecordId,
        decimal inspectedQuantity,
        string? dispositionReason)
    {
        if (!RequiresQualityInspection)
        {
            throw new InvalidOperationException("Inspection-exempt inbound line cannot receive a quality inspection result.");
        }

        if (inspectedQuantity <= 0m || inspectedQuantity > ReceivedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(inspectedQuantity), inspectedQuantity, "Inspected quantity must be within received quantity.");
        }

        var normalizedInspectionRecordId = WmsText.Required(inspectionRecordId, nameof(inspectionRecordId));
        if (InspectionRecordId == normalizedInspectionRecordId && QualityGateStatus == gateStatus)
        {
            return;
        }

        if (HasQualityResult)
        {
            throw new InvalidOperationException("Inbound line already has a quality inspection result.");
        }

        QualityGateStatus = gateStatus;
        InspectionRecordId = normalizedInspectionRecordId;
        QualityDispositionReason = WmsText.Optional(dispositionReason);
    }
}

public static class InboundQualityGateStatuses
{
    public const string Pending = "pending";
    public const string Passed = "passed";
    public const string ConditionalReleased = "conditional-release";
    public const string Rejected = "rejected";
    public const string NotRequired = "not-required";

    public static bool RequiresInspection(string qualityStatus) => WmsReceivingQualityStatuses.RequiresInspection(qualityStatus);
}

internal readonly record struct InboundQualityInspectionResult(string GateStatus)
{
    public static InboundQualityInspectionResult FromEventType(string eventType)
    {
        return eventType switch
        {
            "quality.InspectionPassed" => new InboundQualityInspectionResult(InboundQualityGateStatuses.Passed),
            "quality.InspectionConditionalReleased" => new InboundQualityInspectionResult(InboundQualityGateStatuses.ConditionalReleased),
            "quality.InspectionRejected" => new InboundQualityInspectionResult(InboundQualityGateStatuses.Rejected),
            _ => throw new InvalidOperationException($"Unsupported quality inspection event type: {eventType}."),
        };
    }
}
