using Nerv.IIP.Business.Erp.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.DebitNoteAggregate;

public partial record DebitNoteId : IGuidStronglyTypedId;

public sealed class DebitNote : Entity<DebitNoteId>, IAggregateRoot
{
    private DebitNote()
    {
    }

    private DebitNote(
        string organizationId,
        string environmentId,
        string debitNoteNo,
        string purchaseReturnNo,
        string payableNo,
        string supplierCode,
        decimal amount,
        string currencyCode,
        decimal exchangeRate)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        DebitNoteNo = ErpText.Required(debitNoteNo, nameof(debitNoteNo));
        PurchaseReturnNo = ErpText.Required(purchaseReturnNo, nameof(purchaseReturnNo));
        PayableNo = ErpText.Required(payableNo, nameof(payableNo));
        SupplierCode = ErpText.Required(supplierCode, nameof(supplierCode));
        Amount = ErpText.Positive(amount, nameof(amount));
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        ExchangeRate = ErpText.Positive(exchangeRate, nameof(exchangeRate));
        LocalAmount = Amount * ExchangeRate;
        IssuedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DebitNoteNo { get; private set; } = string.Empty;
    public string PurchaseReturnNo { get; private set; } = string.Empty;
    public string PayableNo { get; private set; } = string.Empty;
    public string SupplierCode { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal ExchangeRate { get; private set; }
    public decimal LocalAmount { get; private set; }
    public DateTime IssuedAtUtc { get; private set; }

    public static DebitNote Issue(
        string organizationId,
        string environmentId,
        string debitNoteNo,
        string purchaseReturnNo,
        string payableNo,
        string supplierCode,
        decimal amount,
        string currencyCode,
        decimal exchangeRate = 1m)
        => new(organizationId, environmentId, debitNoteNo, purchaseReturnNo, payableNo, supplierCode, amount, currencyCode, exchangeRate);
}
