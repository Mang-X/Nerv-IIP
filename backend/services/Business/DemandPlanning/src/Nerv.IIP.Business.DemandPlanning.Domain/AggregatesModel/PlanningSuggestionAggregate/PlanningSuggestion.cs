using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.DomainEvents;

namespace Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;

public partial record PlanningSuggestionId : IGuidStronglyTypedId;
public partial record PeggingLinkId : IGuidStronglyTypedId;

public enum PlanningSuggestionStatus
{
    Open = 0,
    Accepted = 1,
    Rejected = 2,
    Closed = 3,
}

public sealed class PlanningSuggestion : Entity<PlanningSuggestionId>, IAggregateRoot
{
    private readonly List<PeggingLink> peggingLinks = [];

    private PlanningSuggestion()
    {
    }

    private PlanningSuggestion(
        string organizationId,
        string environmentId,
        MrpRunId mrpRunId,
        string suggestionType,
        string skuCode,
        string uomCode,
        string siteCode,
        decimal quantity,
        DateOnly requiredDate,
        DateOnly releaseDate,
        string reasonCode)
    {
        OrganizationId = DemandPlanningText.Required(organizationId, nameof(organizationId));
        EnvironmentId = DemandPlanningText.Required(environmentId, nameof(environmentId));
        MrpRunId = mrpRunId;
        SuggestionType = DemandPlanningText.Required(suggestionType, nameof(suggestionType)).ToLowerInvariant();
        SkuCode = DemandPlanningText.Required(skuCode, nameof(skuCode));
        UomCode = DemandPlanningText.Required(uomCode, nameof(uomCode));
        SiteCode = DemandPlanningText.Required(siteCode, nameof(siteCode));
        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
        RequiredDate = requiredDate;
        ReleaseDate = releaseDate;
        ReasonCode = DemandPlanningText.Required(reasonCode, nameof(reasonCode));
        Status = PlanningSuggestionStatus.Open;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        this.AddDomainEvent(SuggestionType == "planned-work-order"
            ? new PlannedWorkOrderSuggestedDomainEvent(this)
            : new PlannedPurchaseSuggestedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public MrpRunId MrpRunId { get; private set; } = default!;
    public string SuggestionType { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public DateOnly RequiredDate { get; private set; }
    public DateOnly ReleaseDate { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public PlanningSuggestionStatus Status { get; private set; }
    public string? AcceptedDownstreamService { get; private set; }
    public string? AcceptedDownstreamDocumentType { get; private set; }
    public string? AcceptedDownstreamDocumentId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }
    public IReadOnlyCollection<PeggingLink> PeggingLinks => peggingLinks.AsReadOnly();

    public static PlanningSuggestion Create(
        string organizationId,
        string environmentId,
        MrpRunId mrpRunId,
        string suggestionType,
        string skuCode,
        string uomCode,
        string siteCode,
        decimal quantity,
        DateOnly requiredDate,
        DateOnly releaseDate,
        string reasonCode)
    {
        return new PlanningSuggestion(organizationId, environmentId, mrpRunId, suggestionType, skuCode, uomCode, siteCode, quantity, requiredDate, releaseDate, reasonCode);
    }

    public void AddPeggingLink(
        string peggingType,
        string demandSourceReference,
        string parentSkuCode,
        string? componentSkuCode,
        decimal quantity,
        string? productionVersionReference,
        string? manufacturingBomReference,
        string? routingReference)
    {
        peggingLinks.Add(new PeggingLink(
            peggingType,
            demandSourceReference,
            parentSkuCode,
            componentSkuCode,
            quantity,
            productionVersionReference,
            manufacturingBomReference,
            routingReference));
    }

    public void Accept(string downstreamService, string downstreamDocumentType, string downstreamDocumentId)
    {
        if (Status == PlanningSuggestionStatus.Accepted)
        {
            if (AcceptedDownstreamService == downstreamService
                && AcceptedDownstreamDocumentType == downstreamDocumentType
                && AcceptedDownstreamDocumentId == downstreamDocumentId)
            {
                return;
            }

            throw new InvalidOperationException("Planning suggestion has already been accepted with a different downstream reference.");
        }

        if (Status != PlanningSuggestionStatus.Open)
        {
            throw new InvalidOperationException("Only open planning suggestions can be accepted.");
        }

        AcceptedDownstreamService = DemandPlanningText.Required(downstreamService);
        AcceptedDownstreamDocumentType = DemandPlanningText.Required(downstreamDocumentType);
        AcceptedDownstreamDocumentId = DemandPlanningText.Required(downstreamDocumentId);
        AcceptedAtUtc = DateTimeOffset.UtcNow;
        Status = PlanningSuggestionStatus.Accepted;
        this.AddDomainEvent(new PlanningSuggestionAcceptedDomainEvent(this));
    }

    public void Reject(string actor, string reason)
    {
        _ = DemandPlanningText.Required(actor);
        ReasonCode = DemandPlanningText.Required(reason);
        if (Status != PlanningSuggestionStatus.Open)
        {
            throw new InvalidOperationException("Only open planning suggestions can be rejected.");
        }

        Status = PlanningSuggestionStatus.Rejected;
    }
}

public sealed class PeggingLink : Entity<PeggingLinkId>
{
    private PeggingLink()
    {
    }

    internal PeggingLink(
        string peggingType,
        string demandSourceReference,
        string parentSkuCode,
        string? componentSkuCode,
        decimal quantity,
        string? productionVersionReference,
        string? manufacturingBomReference,
        string? routingReference)
    {
        PeggingType = DemandPlanningText.Required(peggingType, nameof(peggingType));
        DemandSourceReference = DemandPlanningText.Required(demandSourceReference, nameof(demandSourceReference));
        ParentSkuCode = DemandPlanningText.Required(parentSkuCode, nameof(parentSkuCode));
        ComponentSkuCode = DemandPlanningText.Optional(componentSkuCode);
        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
        ProductionVersionReference = DemandPlanningText.Optional(productionVersionReference);
        ManufacturingBomReference = DemandPlanningText.Optional(manufacturingBomReference);
        RoutingReference = DemandPlanningText.Optional(routingReference);
    }

    public PlanningSuggestionId PlanningSuggestionId { get; private set; } = default!;
    public string PeggingType { get; private set; } = string.Empty;
    public string DemandSourceReference { get; private set; } = string.Empty;
    public string ParentSkuCode { get; private set; } = string.Empty;
    public string? ComponentSkuCode { get; private set; }
    public decimal Quantity { get; private set; }
    public string? ProductionVersionReference { get; private set; }
    public string? ManufacturingBomReference { get; private set; }
    public string? RoutingReference { get; private set; }
}
