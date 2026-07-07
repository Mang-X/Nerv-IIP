using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;

public partial record AccountingPeriodId : IGuidStronglyTypedId;

public enum AccountingPeriodStatus
{
    Open,
    Closed,
}

public sealed class AccountingPeriod : Entity<AccountingPeriodId>, IAggregateRoot
{
    private AccountingPeriod()
    {
    }

    private AccountingPeriod(
        string organizationId,
        string environmentId,
        string periodCode,
        DateOnly startDate,
        DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate), endDate, "Period end date cannot be earlier than start date.");
        }

        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        PeriodCode = ErpText.Required(periodCode, nameof(periodCode));
        StartDate = startDate;
        EndDate = endDate;
        Status = AccountingPeriodStatus.Open;
        OpenedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string PeriodCode { get; private set; } = string.Empty;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public AccountingPeriodStatus Status { get; private set; }
    public DateTime OpenedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ClosedBy { get; private set; }
    public string? CloseReason { get; private set; }
    public DateTime? ReopenedAtUtc { get; private set; }
    public string? ReopenedBy { get; private set; }
    public string? ReopenReason { get; private set; }
    public bool CanPost => Status == AccountingPeriodStatus.Open;

    public static AccountingPeriod Open(
        string organizationId,
        string environmentId,
        string periodCode,
        DateOnly startDate,
        DateOnly endDate)
    {
        return new AccountingPeriod(organizationId, environmentId, periodCode, startDate, endDate);
    }

    public bool Contains(DateOnly date)
    {
        return date >= StartDate && date <= EndDate;
    }

    public void Close(string closedBy, string reason)
    {
        if (Status == AccountingPeriodStatus.Closed)
        {
            throw new InvalidOperationException("Accounting period is already closed.");
        }

        ClosedBy = ErpText.Required(closedBy, nameof(closedBy));
        CloseReason = ErpText.Required(reason, nameof(reason));
        ClosedAtUtc = DateTime.UtcNow;
        Status = AccountingPeriodStatus.Closed;
    }

    public void Reopen(string reopenedBy, string reason)
    {
        if (Status == AccountingPeriodStatus.Open)
        {
            throw new InvalidOperationException("Accounting period is already open.");
        }

        ReopenedBy = ErpText.Required(reopenedBy, nameof(reopenedBy));
        ReopenReason = ErpText.Required(reason, nameof(reason));
        ReopenedAtUtc = DateTime.UtcNow;
        Status = AccountingPeriodStatus.Open;
    }
}
