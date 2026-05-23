using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;

public partial record AccountReceivableId : IGuidStronglyTypedId;

public sealed class AccountReceivable : Entity<AccountReceivableId>, IAggregateRoot
{
    private AccountReceivable()
    {
    }

    private AccountReceivable(string organizationId, string environmentId, string receivableNo, string sourceDocumentNo, string customerCode, decimal amount, string currencyCode)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        ReceivableNo = ErpText.Required(receivableNo, nameof(receivableNo));
        SourceDocumentNo = ErpText.Required(sourceDocumentNo, nameof(sourceDocumentNo));
        CustomerCode = ErpText.Required(customerCode, nameof(customerCode));
        Amount = ErpText.Positive(amount, nameof(amount));
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        CreatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new AccountReceivableCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string ReceivableNo { get; private set; } = string.Empty;
    public string SourceDocumentNo { get; private set; } = string.Empty;
    public string CustomerCode { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public decimal CollectedAmount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public decimal OpenAmount => Amount - CollectedAmount;

    public static AccountReceivable Create(string organizationId, string environmentId, string receivableNo, string sourceDocumentNo, string customerCode, decimal amount, string currencyCode)
    {
        return new AccountReceivable(organizationId, environmentId, receivableNo, sourceDocumentNo, customerCode, amount, currencyCode);
    }

    public void RegisterCollection(decimal amount)
    {
        _ = ErpText.Positive(amount, nameof(amount));
        if (amount > OpenAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Collected amount cannot exceed open receivable amount.");
        }

        CollectedAmount += amount;
    }
}
