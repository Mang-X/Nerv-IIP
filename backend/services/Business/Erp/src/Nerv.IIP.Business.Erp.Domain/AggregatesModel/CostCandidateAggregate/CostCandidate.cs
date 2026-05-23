using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;

public partial record CostCandidateId : IGuidStronglyTypedId;

public sealed class CostCandidate : Entity<CostCandidateId>, IAggregateRoot
{
    private CostCandidate()
    {
    }

    private CostCandidate(
        string organizationId,
        string environmentId,
        string candidateNo,
        string sourceType,
        string sourceDocumentNo,
        decimal amount,
        string currencyCode)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        CandidateNo = ErpText.Required(candidateNo, nameof(candidateNo));
        SourceType = ErpText.Required(sourceType, nameof(sourceType));
        SourceDocumentNo = ErpText.Required(sourceDocumentNo, nameof(sourceDocumentNo));
        Amount = ErpText.Positive(amount, nameof(amount));
        CurrencyCode = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        CreatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new CostCandidateCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string CandidateNo { get; private set; } = string.Empty;
    public string SourceType { get; private set; } = string.Empty;
    public string SourceDocumentNo { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    public static CostCandidate Create(string organizationId, string environmentId, string candidateNo, string sourceType, string sourceDocumentNo, decimal amount, string currencyCode)
    {
        return new CostCandidate(organizationId, environmentId, candidateNo, sourceType, sourceDocumentNo, amount, currencyCode);
    }
}
