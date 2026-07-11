using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;

public partial record PurchaseOrderId : IGuidStronglyTypedId;
public partial record PurchaseOrderLineId : IGuidStronglyTypedId;

public enum PurchaseOrderStatus
{
    PendingApproval = 0,
    Released = 1,
    Closed = 2,
    Cancelled = 3,
}

public sealed record PurchaseOrderLineDraft(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly PromisedDate,
    decimal OverReceiptTolerancePercent = 0m,
    decimal UnderReceiptTolerancePercent = 0m,
    IReadOnlyCollection<PurchaseOrderLineSourceDraft>? Sources = null);

public sealed record PurchaseOrderLineSourceDraft(
    string PurchaseRequisitionNo,
    string PurchaseRequisitionLineNo,
    decimal Quantity);

public sealed record PurchaseOrderLineChangeDraft(
    string LineNo,
    decimal OrderedQuantity,
    decimal UnitPrice,
    DateOnly PromisedDate);

public enum PurchaseOrderChangeStatus
{
    PendingApproval = 0,
    Approved = 1,
    Rejected = 2,
    Applied = 3,
}

public sealed class PurchaseOrder : Entity<PurchaseOrderId>, IAggregateRoot
{
    private readonly List<PurchaseOrderLine> lines = [];
    private readonly List<PurchaseOrderChange> changeHistory = [];

    private PurchaseOrder()
    {
    }

    private PurchaseOrder(
        string organizationId,
        string environmentId,
        string purchaseOrderNo,
        string supplierCode,
        string siteCode,
        string currencyCode,
        IEnumerable<PurchaseOrderLineDraft> lineDrafts)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        PurchaseOrderNo = ErpText.Required(purchaseOrderNo, nameof(purchaseOrderNo));
        SupplierCode = ErpText.Required(supplierCode, nameof(supplierCode));
        SiteCode = ErpText.Required(siteCode, nameof(siteCode));
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        Status = PurchaseOrderStatus.PendingApproval;
        Version = 1;
        CreatedAtUtc = DateTime.UtcNow;
        lines.AddRange(lineDrafts.Select(PurchaseOrderLine.Create));
        if (lines.Count == 0)
        {
            throw new ArgumentException("At least one purchase order line is required.", nameof(lineDrafts));
        }

        TotalAmount = lines.Sum(x => x.LineAmount);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PurchaseOrderNo { get; private set; } = string.Empty;
    public string SupplierCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public PurchaseOrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string? ApprovalChainId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<PurchaseOrderLine> Lines => lines;
    public int Version { get; private set; }
    public IReadOnlyCollection<PurchaseOrderChange> ChangeHistory => changeHistory;

    public static PurchaseOrder Create(
        string organizationId,
        string environmentId,
        string purchaseOrderNo,
        string supplierCode,
        string siteCode,
        IEnumerable<PurchaseOrderLineDraft> lines)
    {
        return new PurchaseOrder(organizationId, environmentId, purchaseOrderNo, supplierCode, siteCode, "CNY", lines);
    }

    public static PurchaseOrder Create(
        string organizationId,
        string environmentId,
        string purchaseOrderNo,
        string supplierCode,
        string siteCode,
        string currencyCode,
        IEnumerable<PurchaseOrderLineDraft> lines)
    {
        return new PurchaseOrder(organizationId, environmentId, purchaseOrderNo, supplierCode, siteCode, currencyCode, lines);
    }

    public void MarkApprovalRequested(string approvalChainId)
    {
        if (Status != PurchaseOrderStatus.PendingApproval)
        {
            throw new InvalidOperationException("Only purchase orders pending approval can be linked to an approval chain.");
        }

        var normalizedApprovalChainId = ErpText.Required(approvalChainId, nameof(approvalChainId));
        if (ApprovalChainId is not null && !string.Equals(ApprovalChainId, normalizedApprovalChainId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Purchase order approval chain has already been assigned.");
        }

        ApprovalChainId = normalizedApprovalChainId;
    }

    public void ReleaseAfterApproval(string approvalChainId)
    {
        var normalizedApprovalChainId = ErpText.Required(approvalChainId, nameof(approvalChainId));
        if (!string.Equals(ApprovalChainId, normalizedApprovalChainId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Purchase order approval chain does not match.");
        }

        if (Status == PurchaseOrderStatus.Released)
        {
            return;
        }

        if (Status != PurchaseOrderStatus.PendingApproval)
        {
            throw new InvalidOperationException("Only pending purchase orders can be released after approval.");
        }

        Status = PurchaseOrderStatus.Released;
        this.AddDomainEvent(new PurchaseOrderReleasedDomainEvent(this));
    }

    public void ReturnToEditableAfterApprovalRejected(string approvalChainId)
    {
        var normalizedApprovalChainId = ErpText.Required(approvalChainId, nameof(approvalChainId));
        if (Status == PurchaseOrderStatus.PendingApproval && ApprovalChainId is null)
        {
            return;
        }

        if (!string.Equals(ApprovalChainId, normalizedApprovalChainId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Purchase order approval chain does not match.");
        }

        if (Status != PurchaseOrderStatus.PendingApproval)
        {
            throw new InvalidOperationException("Only pending purchase orders can return to editable state after approval rejection.");
        }

        ApprovalChainId = null;
    }

    public PurchaseOrderLine RegisterReceipt(string lineNo, decimal quantity, bool finalDelivery = false)
    {
        EnsureOpen();
        var line = lines.SingleOrDefault(x => x.LineNo == lineNo)
            ?? throw new InvalidOperationException($"Purchase order line '{lineNo}' was not found.");
        line.RegisterReceipt(quantity, finalDelivery);
        if (lines.All(x => x.OpenQuantity == 0 || x.FinalDelivery))
        {
            Status = PurchaseOrderStatus.Closed;
        }

        return line;
    }

    public PurchaseOrderChange RequestChange(IReadOnlyCollection<PurchaseOrderLineChangeDraft> lineChanges, string? reason = null)
    {
        EnsureOpen();
        if (lineChanges.Count == 0)
        {
            throw new ArgumentException("At least one purchase order line change is required.", nameof(lineChanges));
        }

        var normalized = lineChanges.ToArray();
        var lineNumbers = normalized
            .Select(change => ErpText.Required(change.LineNo, nameof(change.LineNo)))
            .ToArray();
        if (lineNumbers.Distinct(StringComparer.Ordinal).Count() != lineNumbers.Length)
        {
            throw new InvalidOperationException("Purchase order change cannot contain duplicate lines.");
        }

        foreach (var change in normalized)
        {
            var line = lines.SingleOrDefault(x => x.LineNo == change.LineNo)
                ?? throw new InvalidOperationException($"Purchase order line '{change.LineNo}' was not found.");
            line.EnsureCanChange(change.OrderedQuantity);
        }

        var pending = PurchaseOrderChange.Request("amend", normalized, reason);
        changeHistory.Add(pending);
        return pending;
    }

    public void ApplyApprovedChange(string approvalChainId)
    {
        EnsureOpen();
        var change = changeHistory.SingleOrDefault(x => string.Equals(x.ApprovalChainId, ErpText.Required(approvalChainId, nameof(approvalChainId)), StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Purchase order change approval chain was not found.");
        change.EnsurePending();
        foreach (var lineChange in change.Lines)
        {
            var line = lines.Single(x => x.LineNo == lineChange.LineNo);
            line.ApplyChange(lineChange.OrderedQuantity, lineChange.UnitPrice, lineChange.PromisedDate);
        }

        change.Approve();
        TotalAmount = lines.Sum(x => x.LineAmount);
        Version++;
    }

    public void RejectChange(string approvalChainId)
    {
        var change = changeHistory.SingleOrDefault(x => string.Equals(x.ApprovalChainId, ErpText.Required(approvalChainId, nameof(approvalChainId)), StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Purchase order change approval chain was not found.");
        change.Reject();
    }

    public void CloseRemainingLine(string lineNo, string reason)
    {
        EnsureOpen();
        var line = lines.SingleOrDefault(x => x.LineNo == ErpText.Required(lineNo, nameof(lineNo)))
            ?? throw new InvalidOperationException($"Purchase order line '{lineNo}' was not found.");
        line.CloseRemaining();
        changeHistory.Add(PurchaseOrderChange.Applied("final-delivery", [line.ToChangeDraft()], reason));
        Version++;
        if (lines.All(x => x.OpenQuantity == 0 || x.FinalDelivery))
        {
            Status = PurchaseOrderStatus.Closed;
        }
    }

    public void Cancel(string reason)
    {
        EnsureOpen();
        if (lines.Any(x => x.ReceivedQuantity > 0m))
        {
            throw new InvalidOperationException("Purchase orders with received quantity cannot be cancelled.");
        }

        changeHistory.Add(PurchaseOrderChange.Applied("cancel", [], reason));
        Version++;
        Status = PurchaseOrderStatus.Cancelled;
    }

    private void EnsureOpen()
    {
        if (Status != PurchaseOrderStatus.Released)
        {
            throw new InvalidOperationException("Closed purchase orders are immutable.");
        }
    }
}

public sealed class PurchaseOrderLine : Entity<PurchaseOrderLineId>
{
    private readonly List<PurchaseOrderLineSourceLink> sourceLinks = [];

    private PurchaseOrderLine()
    {
    }

    private PurchaseOrderLine(PurchaseOrderLineDraft draft)
    {
        LineNo = ErpText.Required(draft.LineNo, nameof(draft.LineNo));
        SkuCode = ErpText.Required(draft.SkuCode, nameof(draft.SkuCode));
        UomCode = ErpText.Required(draft.UomCode, nameof(draft.UomCode));
        OrderedQuantity = ErpText.Positive(draft.Quantity, nameof(draft.Quantity));
        UnitPrice = ErpText.Positive(draft.UnitPrice, nameof(draft.UnitPrice));
        PromisedDate = draft.PromisedDate;
        OverReceiptTolerancePercent = ErpText.NonNegative(draft.OverReceiptTolerancePercent, nameof(draft.OverReceiptTolerancePercent));
        UnderReceiptTolerancePercent = ErpText.NonNegative(draft.UnderReceiptTolerancePercent, nameof(draft.UnderReceiptTolerancePercent));
        ReceivedQuantity = 0m;
        sourceLinks.AddRange((draft.Sources ?? []).Select(PurchaseOrderLineSourceLink.Create));
        if (sourceLinks.Count > 0 && sourceLinks.Sum(x => x.Quantity) != OrderedQuantity)
        {
            throw new ArgumentException("Purchase order line source quantities must equal ordered quantity.", nameof(draft));
        }
    }

    public string LineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal OrderedQuantity { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public DateOnly PromisedDate { get; private set; }
    public decimal OverReceiptTolerancePercent { get; private set; }
    public decimal UnderReceiptTolerancePercent { get; private set; }
    public bool FinalDelivery { get; private set; }
    public decimal OpenQuantity => FinalDelivery ? 0m : Math.Max(OrderedQuantity - ReceivedQuantity, 0m);
    public decimal LineAmount => OrderedQuantity * UnitPrice;
    public IReadOnlyCollection<PurchaseOrderLineSourceLink> SourceLinks => sourceLinks;

    public static PurchaseOrderLine Create(PurchaseOrderLineDraft draft)
    {
        return new PurchaseOrderLine(draft);
    }

    public void RegisterReceipt(decimal quantity, bool finalDelivery = false)
    {
        _ = ErpText.Positive(quantity, nameof(quantity));
        var newReceivedQuantity = ReceivedQuantity + quantity;
        var maxReceivableQuantity = OrderedQuantity * (1m + OverReceiptTolerancePercent / 100m);
        if (newReceivedQuantity > maxReceivableQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Receipt quantity cannot exceed over-receipt tolerance.");
        }

        var minFinalDeliveryQuantity = OrderedQuantity * (1m - UnderReceiptTolerancePercent / 100m);
        if (finalDelivery && newReceivedQuantity < minFinalDeliveryQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Final delivery shortage cannot exceed under-receipt tolerance.");
        }

        ReceivedQuantity = newReceivedQuantity;
        if (finalDelivery)
        {
            FinalDelivery = true;
        }
    }

    internal void EnsureCanChange(decimal orderedQuantity)
    {
        _ = ErpText.Positive(orderedQuantity, nameof(orderedQuantity));
        if (FinalDelivery || orderedQuantity < ReceivedQuantity)
        {
            throw new InvalidOperationException("Purchase order changes cannot reduce quantity below received quantity or reopen a final-delivery line.");
        }
    }

    internal void ApplyChange(decimal orderedQuantity, decimal unitPrice, DateOnly promisedDate)
    {
        EnsureCanChange(orderedQuantity);
        OrderedQuantity = orderedQuantity;
        UnitPrice = ErpText.Positive(unitPrice, nameof(unitPrice));
        PromisedDate = promisedDate;
    }

    internal void CloseRemaining()
    {
        if (FinalDelivery || ReceivedQuantity <= 0m || ReceivedQuantity >= OrderedQuantity)
        {
            throw new InvalidOperationException("Only partially received purchase order lines can be closed as final delivery.");
        }

        var minimumReceipt = OrderedQuantity * (1m - UnderReceiptTolerancePercent / 100m);
        if (ReceivedQuantity < minimumReceipt)
        {
            throw new InvalidOperationException("Final delivery shortage exceeds the configured under-receipt tolerance.");
        }

        FinalDelivery = true;
    }
}

public sealed class PurchaseOrderChange
{
    private readonly List<PurchaseOrderChangeLine> lines = [];

    private PurchaseOrderChange()
    {
    }

    private PurchaseOrderChange(string changeType, IEnumerable<PurchaseOrderLineChangeDraft> drafts, string? reason, PurchaseOrderChangeStatus status)
    {
        ChangeType = ErpText.Required(changeType, nameof(changeType));
        Reason = ErpText.Optional(reason);
        Status = status;
        RequestedAtUtc = DateTime.UtcNow;
        lines.AddRange(drafts.Select(PurchaseOrderChangeLine.Create));
    }

    public long Id { get; private set; }
    public string ChangeType { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public PurchaseOrderChangeStatus Status { get; private set; }
    public string? ApprovalChainId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public IReadOnlyCollection<PurchaseOrderChangeLine> Lines => lines;

    public static PurchaseOrderChange Request(string changeType, IEnumerable<PurchaseOrderLineChangeDraft> drafts, string? reason)
        => new(changeType, drafts, reason, PurchaseOrderChangeStatus.PendingApproval);

    public static PurchaseOrderChange Applied(string changeType, IEnumerable<PurchaseOrderLineChangeDraft> drafts, string? reason)
        => new(changeType, drafts, reason, PurchaseOrderChangeStatus.Applied) { ResolvedAtUtc = DateTime.UtcNow };

    public void AssignApprovalChain(string approvalChainId)
    {
        EnsurePending();
        var normalized = ErpText.Required(approvalChainId, nameof(approvalChainId));
        if (ApprovalChainId is not null && !string.Equals(ApprovalChainId, normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Purchase order change approval chain has already been assigned.");
        }

        ApprovalChainId = normalized;
    }

    internal void EnsurePending()
    {
        if (Status != PurchaseOrderChangeStatus.PendingApproval)
        {
            throw new InvalidOperationException("Only pending purchase order changes can be resolved.");
        }
    }

    internal void Approve()
    {
        EnsurePending();
        Status = PurchaseOrderChangeStatus.Approved;
        ResolvedAtUtc = DateTime.UtcNow;
    }

    internal void Reject()
    {
        EnsurePending();
        Status = PurchaseOrderChangeStatus.Rejected;
        ResolvedAtUtc = DateTime.UtcNow;
    }
}

public sealed class PurchaseOrderChangeLine
{
    private PurchaseOrderChangeLine()
    {
    }

    private PurchaseOrderChangeLine(PurchaseOrderLineChangeDraft draft)
    {
        LineNo = ErpText.Required(draft.LineNo, nameof(draft.LineNo));
        OrderedQuantity = ErpText.Positive(draft.OrderedQuantity, nameof(draft.OrderedQuantity));
        UnitPrice = ErpText.Positive(draft.UnitPrice, nameof(draft.UnitPrice));
        PromisedDate = draft.PromisedDate;
    }

    public long Id { get; private set; }
    public string LineNo { get; private set; } = string.Empty;
    public decimal OrderedQuantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public DateOnly PromisedDate { get; private set; }

    public static PurchaseOrderChangeLine Create(PurchaseOrderLineChangeDraft draft) => new(draft);
}

internal static class PurchaseOrderLineChangeDraftExtensions
{
    public static PurchaseOrderLineChangeDraft ToChangeDraft(this PurchaseOrderLine line)
        => new(line.LineNo, line.OrderedQuantity, line.UnitPrice, line.PromisedDate);
}

public sealed class PurchaseOrderLineSourceLink
{
    private PurchaseOrderLineSourceLink()
    {
    }

    private PurchaseOrderLineSourceLink(PurchaseOrderLineSourceDraft draft)
    {
        PurchaseRequisitionNo = ErpText.Required(draft.PurchaseRequisitionNo, nameof(draft.PurchaseRequisitionNo));
        PurchaseRequisitionLineNo = ErpText.Required(draft.PurchaseRequisitionLineNo, nameof(draft.PurchaseRequisitionLineNo));
        Quantity = ErpText.Positive(draft.Quantity, nameof(draft.Quantity));
    }

    public long Id { get; private set; }
    public string PurchaseRequisitionNo { get; private set; } = string.Empty;
    public string PurchaseRequisitionLineNo { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }

    public static PurchaseOrderLineSourceLink Create(PurchaseOrderLineSourceDraft draft)
    {
        return new PurchaseOrderLineSourceLink(draft);
    }
}
