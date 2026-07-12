using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;

public partial record CashReceiptId : IGuidStronglyTypedId;
public partial record CashReceiptAllocationId : IGuidStronglyTypedId;

public enum CashReceiptStatus
{
    Registered,
    Matched,
}

public sealed record CashReceiptAllocationDraft(string ReceivableNo, decimal Amount);

public sealed class CashReceipt : Entity<CashReceiptId>, IAggregateRoot
{
    private readonly List<CashReceiptAllocation> allocations = [];

    private CashReceipt()
    {
    }

    private CashReceipt(
        string organizationId,
        string environmentId,
        string cashReceiptNo,
        string customerCode,
        decimal amount,
        string currencyCode,
        DateOnly receiptDate,
        string cashAccountCode)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        CashReceiptNo = ErpText.Required(cashReceiptNo, nameof(cashReceiptNo));
        CustomerCode = ErpText.Required(customerCode, nameof(customerCode));
        Amount = ErpText.Positive(amount, nameof(amount));
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        ReceiptDate = receiptDate;
        CashAccountCode = ErpText.Required(cashAccountCode, nameof(cashAccountCode));
        RegisteredAtUtc = DateTime.UtcNow;
        Status = CashReceiptStatus.Registered;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CashReceiptNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public DateOnly ReceiptDate { get; private set; }
    public string CashAccountCode { get; private set; } = string.Empty;
    public CashReceiptStatus Status { get; private set; }
    public DateTime RegisteredAtUtc { get; private set; }
    public DateTime? MatchedAtUtc { get; private set; }
    public IReadOnlyCollection<CashReceiptAllocation> Allocations => allocations;

    public static CashReceipt Register(
        string organizationId,
        string environmentId,
        string cashReceiptNo,
        string customerCode,
        decimal amount,
        string currencyCode,
        DateOnly receiptDate,
        string cashAccountCode)
    {
        return new CashReceipt(organizationId, environmentId, cashReceiptNo, customerCode, amount, currencyCode, receiptDate, cashAccountCode);
    }

    public static CashReceipt Register(
        string organizationId,
        string environmentId,
        string cashReceiptNo,
        string customerCode,
        decimal amount,
        string currencyCode,
        DateOnly receiptDate,
        string cashAccountCode,
        IReadOnlyCollection<CashReceiptAllocationDraft> allocationDrafts)
    {
        var receipt = Register(organizationId, environmentId, cashReceiptNo, customerCode, amount, currencyCode, receiptDate, cashAccountCode);
        receipt.AddAllocations(allocationDrafts);
        return receipt;
    }

    public void Match()
    {
        if (Status != CashReceiptStatus.Registered)
        {
            throw new InvalidOperationException("Only registered cash receipts can be matched.");
        }

        if (allocations.Count == 0)
        {
            throw new InvalidOperationException("At least one receivable allocation is required before matching.");
        }

        MatchedAtUtc = DateTime.UtcNow;
        Status = CashReceiptStatus.Matched;
    }

    public void Match(IReadOnlyCollection<CashReceiptAllocationDraft> allocationDrafts)
    {
        AddAllocations(allocationDrafts);
        Match();
    }

    private void AddAllocations(IReadOnlyCollection<CashReceiptAllocationDraft> allocationDrafts)
    {
        if (allocationDrafts.Count == 0)
        {
            throw new ArgumentException("At least one receivable allocation is required.", nameof(allocationDrafts));
        }

        if (allocations.Count > 0)
        {
            throw new InvalidOperationException("Cash receipt allocations have already been recorded.");
        }

        var allocatedAmount = allocationDrafts.Sum(x => x.Amount);
        if (allocatedAmount > Amount)
        {
            throw new ArgumentOutOfRangeException(nameof(allocationDrafts), allocatedAmount, "Matched amount cannot exceed receipt amount.");
        }

        allocations.AddRange(allocationDrafts.Select(CashReceiptAllocation.Create));
    }
}

public sealed class CashReceiptAllocation : Entity<CashReceiptAllocationId>
{
    private CashReceiptAllocation()
    {
    }

    private CashReceiptAllocation(string receivableNo, decimal amount)
    {
        ReceivableNo = ErpText.Required(receivableNo, nameof(receivableNo));
        Amount = ErpText.Positive(amount, nameof(amount));
    }

    public string ReceivableNo { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }

    public static CashReceiptAllocation Create(CashReceiptAllocationDraft draft)
    {
        return new CashReceiptAllocation(draft.ReceivableNo, draft.Amount);
    }
}
