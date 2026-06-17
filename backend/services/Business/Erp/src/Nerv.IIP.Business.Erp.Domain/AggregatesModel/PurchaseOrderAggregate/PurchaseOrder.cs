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
    DateOnly PromisedDate);

public sealed class PurchaseOrder : Entity<PurchaseOrderId>, IAggregateRoot
{
    private readonly List<PurchaseOrderLine> lines = [];

    private PurchaseOrder()
    {
    }

    private PurchaseOrder(
        string organizationId,
        string environmentId,
        string purchaseOrderNo,
        string supplierCode,
        string siteCode,
        IEnumerable<PurchaseOrderLineDraft> lineDrafts)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        PurchaseOrderNo = ErpText.Required(purchaseOrderNo, nameof(purchaseOrderNo));
        SupplierCode = ErpText.Required(supplierCode, nameof(supplierCode));
        SiteCode = ErpText.Required(siteCode, nameof(siteCode));
        Status = PurchaseOrderStatus.PendingApproval;
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
    public PurchaseOrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string? ApprovalChainId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<PurchaseOrderLine> Lines => lines;

    public static PurchaseOrder Create(
        string organizationId,
        string environmentId,
        string purchaseOrderNo,
        string supplierCode,
        string siteCode,
        IEnumerable<PurchaseOrderLineDraft> lines)
    {
        return new PurchaseOrder(organizationId, environmentId, purchaseOrderNo, supplierCode, siteCode, lines);
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

    public void CancelAfterApprovalRejected(string approvalChainId)
    {
        var normalizedApprovalChainId = ErpText.Required(approvalChainId, nameof(approvalChainId));
        if (!string.Equals(ApprovalChainId, normalizedApprovalChainId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Purchase order approval chain does not match.");
        }

        if (Status == PurchaseOrderStatus.Cancelled)
        {
            return;
        }

        if (Status != PurchaseOrderStatus.PendingApproval)
        {
            throw new InvalidOperationException("Only pending purchase orders can be cancelled after approval rejection.");
        }

        Status = PurchaseOrderStatus.Cancelled;
    }

    public PurchaseOrderLine RegisterReceipt(string lineNo, decimal quantity)
    {
        EnsureOpen();
        var line = lines.SingleOrDefault(x => x.LineNo == lineNo)
            ?? throw new InvalidOperationException($"Purchase order line '{lineNo}' was not found.");
        line.RegisterReceipt(quantity);
        if (lines.All(x => x.OpenQuantity == 0))
        {
            Status = PurchaseOrderStatus.Closed;
        }

        return line;
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
        ReceivedQuantity = 0m;
    }

    public string LineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public decimal OrderedQuantity { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public DateOnly PromisedDate { get; private set; }
    public decimal OpenQuantity => OrderedQuantity - ReceivedQuantity;
    public decimal LineAmount => OrderedQuantity * UnitPrice;

    public static PurchaseOrderLine Create(PurchaseOrderLineDraft draft)
    {
        return new PurchaseOrderLine(draft);
    }

    public void RegisterReceipt(decimal quantity)
    {
        _ = ErpText.Positive(quantity, nameof(quantity));
        if (quantity > OpenQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Receipt quantity cannot exceed open ordered quantity.");
        }

        ReceivedQuantity += quantity;
    }
}
