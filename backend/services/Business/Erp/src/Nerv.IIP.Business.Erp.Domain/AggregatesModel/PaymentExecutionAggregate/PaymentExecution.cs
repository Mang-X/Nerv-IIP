using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.PaymentExecutionAggregate;

public partial record PaymentExecutionId : IGuidStronglyTypedId;
public partial record PaymentExecutionAllocationId : IGuidStronglyTypedId;

public static class PaymentExecutionStatus
{
    public const string Approved = "approved";
    public const string Executed = "executed";
}

public sealed record PaymentExecutionAllocationDraft(string PayableNo, decimal Amount);

public sealed class PaymentExecution : Entity<PaymentExecutionId>, IAggregateRoot
{
    private readonly List<PaymentExecutionAllocation> allocations = [];

    private PaymentExecution()
    {
    }

    private PaymentExecution(
        string organizationId,
        string environmentId,
        string paymentExecutionNo,
        string supplierCode,
        decimal amount,
        string currencyCode,
        DateOnly paymentDate,
        string cashAccountCode,
        string approvedBy)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        PaymentExecutionNo = ErpText.Required(paymentExecutionNo, nameof(paymentExecutionNo));
        SupplierCode = ErpText.Required(supplierCode, nameof(supplierCode));
        Amount = ErpText.Positive(amount, nameof(amount));
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        PaymentDate = paymentDate;
        CashAccountCode = ErpText.Required(cashAccountCode, nameof(cashAccountCode));
        ApprovedBy = ErpText.Required(approvedBy, nameof(approvedBy));
        ApprovedAtUtc = DateTime.UtcNow;
        Status = PaymentExecutionStatus.Approved;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PaymentExecutionNo { get; private set; } = string.Empty;
    public string SupplierCode { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public DateOnly PaymentDate { get; private set; }
    public string CashAccountCode { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string ApprovedBy { get; private set; } = string.Empty;
    public DateTime ApprovedAtUtc { get; private set; }
    public string? ExecutedBy { get; private set; }
    public DateTime? ExecutedAtUtc { get; private set; }
    public IReadOnlyCollection<PaymentExecutionAllocation> Allocations => allocations;

    public static PaymentExecution Approve(
        string organizationId,
        string environmentId,
        string paymentExecutionNo,
        string supplierCode,
        decimal amount,
        string currencyCode,
        DateOnly paymentDate,
        string cashAccountCode,
        string approvedBy)
    {
        return new PaymentExecution(organizationId, environmentId, paymentExecutionNo, supplierCode, amount, currencyCode, paymentDate, cashAccountCode, approvedBy);
    }

    public void Execute(IReadOnlyCollection<PaymentExecutionAllocationDraft> allocationDrafts, string executedBy)
    {
        if (Status != PaymentExecutionStatus.Approved)
        {
            throw new InvalidOperationException("Only approved payment executions can be executed.");
        }

        if (allocationDrafts.Count == 0)
        {
            throw new ArgumentException("At least one payable allocation is required.", nameof(allocationDrafts));
        }

        var allocatedAmount = allocationDrafts.Sum(x => x.Amount);
        if (allocatedAmount > Amount)
        {
            throw new ArgumentOutOfRangeException(nameof(allocationDrafts), allocatedAmount, "Allocated amount cannot exceed payment amount.");
        }

        allocations.AddRange(allocationDrafts.Select(PaymentExecutionAllocation.Create));
        ExecutedBy = ErpText.Required(executedBy, nameof(executedBy));
        ExecutedAtUtc = DateTime.UtcNow;
        Status = PaymentExecutionStatus.Executed;
    }
}

public sealed class PaymentExecutionAllocation : Entity<PaymentExecutionAllocationId>
{
    private PaymentExecutionAllocation()
    {
    }

    private PaymentExecutionAllocation(string payableNo, decimal amount)
    {
        PayableNo = ErpText.Required(payableNo, nameof(payableNo));
        Amount = ErpText.Positive(amount, nameof(amount));
    }

    public string PayableNo { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }

    public static PaymentExecutionAllocation Create(PaymentExecutionAllocationDraft draft)
    {
        return new PaymentExecutionAllocation(draft.PayableNo, draft.Amount);
    }
}
