using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;

public partial record PurchaseRequisitionId : IGuidStronglyTypedId;

public enum PurchaseRequisitionStatus
{
    Open = 0,
    Converted = 1,
    Cancelled = 2,
}

public sealed class PurchaseRequisition : Entity<PurchaseRequisitionId>, IAggregateRoot
{
    private PurchaseRequisition()
    {
    }

    private PurchaseRequisition(
        string organizationId,
        string environmentId,
        string requisitionNo,
        string suggestionId,
        string skuCode,
        string uomCode,
        string siteCode,
        decimal quantity,
        DateOnly requiredDate)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        RequisitionNo = ErpText.Required(requisitionNo, nameof(requisitionNo));
        SuggestionId = ErpText.Required(suggestionId, nameof(suggestionId));
        SkuCode = ErpText.Required(skuCode, nameof(skuCode));
        UomCode = ErpText.Required(uomCode, nameof(uomCode));
        SiteCode = ErpText.Required(siteCode, nameof(siteCode));
        Quantity = ErpText.Positive(quantity, nameof(quantity));
        RequiredDate = requiredDate;
        Status = PurchaseRequisitionStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
        this.AddDomainEvent(new PurchaseRequisitionCreatedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RequisitionNo { get; private set; } = string.Empty;
    public string SuggestionId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public DateOnly RequiredDate { get; private set; }
    public PurchaseRequisitionStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static PurchaseRequisition CreateFromSuggestion(
        string organizationId,
        string environmentId,
        string requisitionNo,
        string suggestionId,
        string skuCode,
        string uomCode,
        string siteCode,
        decimal quantity,
        DateOnly requiredDate)
    {
        return new PurchaseRequisition(organizationId, environmentId, requisitionNo, suggestionId, skuCode, uomCode, siteCode, quantity, requiredDate);
    }
}
