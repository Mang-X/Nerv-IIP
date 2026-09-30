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
    Superseded = 4,
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
        string reasonCode,
        Guid? netRequirementReference)
    {
        OrganizationId = DemandPlanningText.Required(organizationId, nameof(organizationId));
        EnvironmentId = DemandPlanningText.Required(environmentId, nameof(environmentId));
        MrpRunId = mrpRunId;
        NetRequirementReference = netRequirementReference;
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
        if (SuggestionType == "planned-work-order")
        {
            this.AddDomainEvent(new PlannedWorkOrderSuggestedDomainEvent(this));
        }
        else if (SuggestionType == "planned-purchase")
        {
            this.AddDomainEvent(new PlannedPurchaseSuggestedDomainEvent(this));
        }
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public MrpRunId MrpRunId { get; private set; } = default!;
    public Guid? NetRequirementReference { get; private set; }
    public string SuggestionType { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public DateOnly RequiredDate { get; private set; }
    public DateOnly ReleaseDate { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public decimal GrossDemandQuantity { get; private set; }
    public decimal OnHandQuantity { get; private set; }
    public decimal ReservedQuantity { get; private set; }
    public decimal AvailableToNetQuantity { get; private set; }
    public decimal ScheduledReceiptQuantity { get; private set; }
    public decimal SafetyStockQuantity { get; private set; }
    public decimal NetRequirementQuantity { get; private set; }
    public decimal PlannedQuantity { get; private set; }
    public decimal ScrapRate { get; private set; }
    public decimal YieldRate { get; private set; } = 1m;
    public string PrimarySourceType { get; private set; } = string.Empty;
    public string Formula { get; private set; } = string.Empty;
    public string UomConversionSummary { get; private set; } = string.Empty;
    public PlanningSuggestionStatus Status { get; private set; }
    public MrpRunId? SupersededByRunId { get; private set; }
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
        string reasonCode,
        PlanningSuggestionId? suggestionId = null,
        Guid? netRequirementReference = null)
    {
        var suggestion = new PlanningSuggestion(
            organizationId,
            environmentId,
            mrpRunId,
            suggestionType,
            skuCode,
            uomCode,
            siteCode,
            quantity,
            requiredDate,
            releaseDate,
            reasonCode,
            netRequirementReference);
        if (suggestionId is not null)
        {
            suggestion.Id = suggestionId;
        }

        return suggestion;
    }

    public void SetNetRequirementExplanation(
        decimal grossDemandQuantity,
        decimal onHandQuantity,
        decimal reservedQuantity,
        decimal availableToNetQuantity,
        decimal scheduledReceiptQuantity,
        decimal safetyStockQuantity,
        decimal netRequirementQuantity,
        decimal plannedQuantity,
        decimal scrapRate,
        decimal yieldRate,
        string primarySourceType,
        string formula,
        string? uomConversionSummary)
    {
        GrossDemandQuantity = Math.Max(0m, grossDemandQuantity);
        OnHandQuantity = Math.Max(0m, onHandQuantity);
        ReservedQuantity = Math.Max(0m, reservedQuantity);
        AvailableToNetQuantity = Math.Max(0m, availableToNetQuantity);
        ScheduledReceiptQuantity = Math.Max(0m, scheduledReceiptQuantity);
        SafetyStockQuantity = Math.Max(0m, safetyStockQuantity);
        NetRequirementQuantity = Math.Max(0m, netRequirementQuantity);
        PlannedQuantity = Math.Max(0m, plannedQuantity);
        ScrapRate = Math.Max(0m, scrapRate);
        YieldRate = yieldRate <= 0m ? 1m : yieldRate;
        PrimarySourceType = DemandPlanningText.Required(primarySourceType, nameof(primarySourceType));
        Formula = DemandPlanningText.Required(formula, nameof(formula));
        UomConversionSummary = DemandPlanningText.Optional(uomConversionSummary) ?? string.Empty;
    }

    public void AddPeggingLink(
        string peggingType,
        string demandSourceReference,
        string parentSkuCode,
        string? componentSkuCode,
        decimal quantity,
        string? productionVersionReference,
        string? manufacturingBomReference,
        string? routingReference,
        string? sourceType = null,
        decimal grossDemandQuantity = 0m,
        string? sourceLineReference = null)
    {
        peggingLinks.Add(new PeggingLink(
            peggingType,
            demandSourceReference,
            parentSkuCode,
            componentSkuCode,
            quantity,
            productionVersionReference,
            manufacturingBomReference,
            routingReference,
            sourceType,
            grossDemandQuantity,
            sourceLineReference));
    }

    /// <summary>
    /// 建议 pegging 中所有 demand 类型的需求源引用（按 pegging 顺序去重）。
    /// 合批建议会 peg 到多个需求源；下游桥接/事件必须完整携带，履约追溯才能对每张订单点亮。
    /// scheduled-receipt 类型的 pegging 引用（如 erp:purchase-order:PO-x）不是需求源，予以排除。
    /// </summary>
    public IReadOnlyList<string> GetDemandSourceReferences()
    {
        var references = new List<string>();
        foreach (var link in peggingLinks)
        {
            if (!string.Equals(link.PeggingType, "demand", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var reference = link.DemandSourceReference?.Trim();
            if (string.IsNullOrEmpty(reference) || references.Contains(reference, StringComparer.Ordinal))
            {
                continue;
            }

            references.Add(reference);
        }

        return references;
    }

    /// <summary>
    /// 单值「主需求源引用」：<see cref="GetDemandSourceReferences"/> 的第一条；
    /// 没有 demand 类型 pegging 时回退到任意非空 pegging 引用，避免历史数据丢链。
    /// </summary>
    /// <remarks>
    /// 下游只认单值引用的字段（MES 工单的 SourceDemandReference、集成事件 payload 的主引用）都用它。
    /// 之所以收在聚合里：这条「主引用 + 回退」三联式原本在集成事件转换器、下游桥接、
    /// 验收测试三处各抄一遍，任一处的回退条件写歪，同一张建议在事件里和在工单上就会指向不同需求源。
    /// </remarks>
    public string? GetPrimaryDemandSourceReference()
    {
        var references = GetDemandSourceReferences();
        return references.Count > 0
            ? references[0]
            : peggingLinks.Select(x => x.DemandSourceReference).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    public void Accept(string downstreamService, string downstreamDocumentType, string? downstreamDocumentId,
        IReadOnlyCollection<string>? assemblyParentSuggestionIds = null)
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
        AcceptedDownstreamDocumentId = DemandPlanningText.Optional(downstreamDocumentId);
        AcceptedAtUtc = DateTimeOffset.UtcNow;
        Status = PlanningSuggestionStatus.Accepted;
        this.AddDomainEvent(new PlanningSuggestionAcceptedDomainEvent(this, assemblyParentSuggestionIds));
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

    public void Supersede(MrpRunId successorRunId)
    {
        if (Status != PlanningSuggestionStatus.Open)
        {
            return;
        }

        Status = PlanningSuggestionStatus.Superseded;
        SupersededByRunId = successorRunId;
    }

    public bool IsAssemblyParentOf(PlanningSuggestion component) =>
        Id != component.Id && MrpRunId == component.MrpRunId &&
        OrganizationId == component.OrganizationId && EnvironmentId == component.EnvironmentId && SiteCode == component.SiteCode &&
        SuggestionType == "planned-work-order" && ReleaseDate == component.RequiredDate &&
        component.PeggingLinks.Any(link => link.PeggingType is "demand" or "safety-stock" or "negative-availability" &&
            link.ComponentSkuCode == component.SkuCode && link.ParentSkuCode == SkuCode &&
            PeggingLinks.Any(parentLink => parentLink.PeggingType == link.PeggingType &&
                parentLink.DemandSourceReference == link.DemandSourceReference && parentLink.SourceLineReference == link.SourceLineReference));

    public void InvalidateDemandLines(string demandSourceReference, IReadOnlyCollection<string?> sourceLineReferences)
    {
        if (Status != PlanningSuggestionStatus.Open)
        {
            return;
        }

        var demandLinks = peggingLinks.Where(x => string.Equals(x.PeggingType, "demand", StringComparison.OrdinalIgnoreCase)).ToArray();
        var invalidQuantity = demandLinks
            .Where(x => string.Equals(x.DemandSourceReference, demandSourceReference, StringComparison.Ordinal)
                && sourceLineReferences.Contains(x.SourceLineReference))
            .Sum(x => x.Quantity);
        if (invalidQuantity == 0m)
        {
            return;
        }

        var remainingQuantity = demandLinks.Sum(x => x.Quantity) - invalidQuantity;
        if (remainingQuantity == 0m)
        {
            Status = PlanningSuggestionStatus.Closed;
            return;
        }

        Quantity = decimal.Round(Quantity * remainingQuantity / (remainingQuantity + invalidQuantity), 6, MidpointRounding.AwayFromZero);
        if (Quantity == 0m)
        {
            Status = PlanningSuggestionStatus.Closed;
            return;
        }

        peggingLinks.RemoveAll(x => string.Equals(x.PeggingType, "demand", StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.DemandSourceReference, demandSourceReference, StringComparison.Ordinal)
            && sourceLineReferences.Contains(x.SourceLineReference));
        PlannedQuantity = Quantity;
    }

    public void NotifySalesOrderDemandChanged(string demandSourceReference, string salesOrderId, int orderVersion, bool cancelled)
    {
        this.AddDomainEvent(new SalesOrderDemandChangedForWorkOrderDomainEvent(
            this, demandSourceReference, salesOrderId, orderVersion, cancelled));
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
        string? routingReference,
        string? sourceType = null,
        decimal grossDemandQuantity = 0m,
        string? sourceLineReference = null)
    {
        PeggingType = DemandPlanningText.Required(peggingType, nameof(peggingType));
        DemandSourceReference = DemandPlanningText.Required(demandSourceReference, nameof(demandSourceReference));
        ParentSkuCode = DemandPlanningText.Required(parentSkuCode, nameof(parentSkuCode));
        ComponentSkuCode = DemandPlanningText.Optional(componentSkuCode);
        Quantity = DemandPlanningText.Positive(quantity, nameof(quantity));
        ProductionVersionReference = DemandPlanningText.Optional(productionVersionReference);
        ManufacturingBomReference = DemandPlanningText.Optional(manufacturingBomReference);
        RoutingReference = DemandPlanningText.Optional(routingReference);
        SourceType = DemandPlanningText.Optional(sourceType) ?? "unknown";
        GrossDemandQuantity = Math.Max(0m, grossDemandQuantity);
        SourceLineReference = DemandPlanningText.Optional(sourceLineReference);
    }

    public PlanningSuggestionId PlanningSuggestionId { get; private set; } = default!;
    public string PeggingType { get; private set; } = string.Empty;
    public string DemandSourceReference { get; private set; } = string.Empty;
    public string? SourceLineReference { get; private set; }
    public string ParentSkuCode { get; private set; } = string.Empty;
    public string? ComponentSkuCode { get; private set; }
    public decimal Quantity { get; private set; }
    public string? ProductionVersionReference { get; private set; }
    public string? ManufacturingBomReference { get; private set; }
    public string? RoutingReference { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public decimal GrossDemandQuantity { get; private set; }
}
