using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;
using Nerv.IIP.Business.DemandPlanning.Web.Application.Planning;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;

public sealed record GetMaterialDeliveriesQuery(string OrganizationId, string EnvironmentId, MrpRunId RunId, string? PlanId)
    : IQuery<MaterialDeliveriesResponse>;

public sealed class GetMaterialDeliveriesQueryValidator : AbstractValidator<GetMaterialDeliveriesQuery>
{
    public GetMaterialDeliveriesQueryValidator()
    {
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        RuleFor(x => x.RunId).NotNull();
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128).When(x => x.PlanId is not null);
    }
}

public enum MaterialDeliveryStatus { Yellow, Green, Red }
public sealed record MaterialDeliveriesResponse(string RunId, string? PlanId, DateTimeOffset EvaluatedAtUtc,
    string SupplyCoverageScope, IReadOnlyCollection<MaterialDeliveryResponse> Items);
public sealed record MaterialDeliveryDemandSource(string SourceReference, string? SourceLineReference,
    string SourceType, string ParentSkuCode, string? ComponentSkuCode, decimal GrossDemandQuantity,
    string? DemandSourceId, string? SourceDocumentId, int? SourceVersion, DateOnly? DueDate,
    string? ProductionVersionReference, string? ManufacturingBomReference, string? RoutingReference);
public sealed record MaterialDeliveryNetRequirementSource(decimal GrossDemandQuantity, decimal OnHandQuantity,
    decimal ReservedQuantity, decimal AvailableToNetQuantity, decimal ScheduledReceiptQuantity,
    decimal SafetyStockQuantity, decimal NetRequirementQuantity, decimal PlannedQuantity,
    decimal ScrapRate, decimal YieldRate, string Formula, string UomConversionSummary);
public sealed record MaterialDeliveryResponse(string SuggestionId, string RunId, string SuggestionType,
    string SuggestionStatus, string SkuCode, string UomCode, string SiteCode, decimal NetRequirementQuantity,
    DateOnly LatestProcurementDate, DateTimeOffset LatestProcurementUtc, DateOnly? ExpectedArrivalDate,
    DateTimeOffset? ExpectedArrivalUtc, DateTimeOffset? ExpectedStartUtc, DateTimeOffset? LatestStartUtc,
    decimal CoveredQuantity, decimal UncoveredQuantity, MaterialDeliveryStatus Status, IReadOnlyCollection<string> Reasons,
    MaterialDeliveryNetRequirementSource NetRequirementSource, IReadOnlyCollection<MaterialDeliveryDemandSource> DemandSources,
    IReadOnlyCollection<MaterialDeliverySupplySource> SupplySources,
    IReadOnlyCollection<MaterialDeliveryOrderSourceContract> SchedulingSources);

public sealed class GetMaterialDeliveriesQueryHandler(ApplicationDbContext dbContext,
    IMaterialDeliverySourcesClient sourcesClient, TimeProvider timeProvider)
    : IQueryHandler<GetMaterialDeliveriesQuery, MaterialDeliveriesResponse>
{
    public async Task<MaterialDeliveriesResponse> Handle(GetMaterialDeliveriesQuery request, CancellationToken cancellationToken)
    {
        var suggestions = await dbContext.PlanningSuggestions.AsNoTracking().Include(x => x.PeggingLinks)
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.MrpRunId == request.RunId)
            .OrderBy(x => x.RequiredDate).ThenBy(x => x.SkuCode).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var demands = await dbContext.DemandSources.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .ToListAsync(cancellationToken);
        var demandSources = suggestions.ToDictionary(x => x.Id, x => (IReadOnlyCollection<MaterialDeliveryDemandSource>)x.PeggingLinks
            .Where(link => link.PeggingType == "demand")
            .OrderBy(link => link.DemandSourceReference).ThenBy(link => link.SourceLineReference)
            .Select(link =>
            {
                // 历史未知销售行身份保持未知，不能通过同单号或 SKU 猜一行。
                var matches = demands.Where(d => d.SourceReference == link.DemandSourceReference &&
                    d.SourceLineReference == link.SourceLineReference && d.SiteCode == x.SiteCode &&
                    (link.SourceType is not ("sales" or "sales-order") || d.DemandType == "sales-order")).ToArray();
                var demand = matches.Length == 1 ? matches[0] : null;
                return new MaterialDeliveryDemandSource(link.DemandSourceReference, link.SourceLineReference,
                    link.SourceType, link.ParentSkuCode, link.ComponentSkuCode, link.GrossDemandQuantity,
                    demand?.Id.ToString(), demand?.SourceDocumentId, demand?.SourceVersion, demand?.DueDate,
                    link.ProductionVersionReference, link.ManufacturingBomReference, link.RoutingReference);
            }).ToArray());
        var selections = suggestions.Where(x => x.SuggestionType == "planned-work-order")
            .Select(x => new MaterialDeliverySourceSelection(x.Id.ToString(),
                x.AcceptedDownstreamService == DemandPlanningDownstreamReferences.BusinessMes &&
                x.AcceptedDownstreamDocumentType == DemandPlanningDownstreamReferences.WorkOrder ? x.AcceptedDownstreamDocumentId : null,
                demandSources[x.Id].Where(d => d.DueDate.HasValue)
                    .Select(d => new MaterialDeliveryDueSourceContract(SourceKey(d), MaterialDeliveryProjection.ToUtc(d.DueDate!.Value)))
                    .Distinct().ToArray()))
            .Where(x => x.DueSources.Count > 0).ToArray();
        var scheduling = request.PlanId is null || selections.Length == 0 ? [] :
            (await sourcesClient.GetSchedulingAsync(request.OrganizationId, request.EnvironmentId, request.PlanId, selections, cancellationToken)).Items;
        var supply = suggestions.Any(x => x.NetRequirementQuantity > 0) ?
            await sourcesClient.GetSupplyAsync(request.OrganizationId, request.EnvironmentId, cancellationToken) : [];
        var now = timeProvider.GetUtcNow();
        var rows = suggestions.Where(x => x.NetRequirementQuantity > 0 && x.SuggestionType is "planned-work-order" or "planned-purchase")
            .Select(x =>
            {
                var productionSuggestions = x.SuggestionType == "planned-work-order" ? [x] : suggestions.Where(parent => parent.IsAssemblyParentOf(x)).ToArray();
                var productionIds = productionSuggestions.Select(parent => parent.Id.ToString()).ToHashSet(StringComparer.Ordinal);
                return MaterialDeliveryProjection.Create(x, demandSources[x.Id], supply,
                    scheduling.Where(s => productionIds.Contains(s.SuggestionId)).ToArray(),
                    productionSuggestions.Length, request.PlanId, now, productionSuggestions.All(parent =>
                        demandSources[parent.Id].Count > 0 && demandSources[parent.Id].All(d => d.DueDate.HasValue)));
            }).ToArray();
        return new(request.RunId.ToString(), request.PlanId, now, "independent-per-net-requirement", rows);
    }

    private static string SourceKey(MaterialDeliveryDemandSource source) =>
        source.DemandSourceId!;
}

public static class MaterialDeliveryProjection
{
    // DateOnly 的既有来源精度显式映射到 UTC 午夜，原始日期同时返回。
    public static DateTimeOffset ToUtc(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    public static MaterialDeliveryStatus EvaluateStatus(DateTimeOffset now, DateTimeOffset? latest,
        DateTimeOffset? arrival, DateTimeOffset? start, bool complete)
    {
        if (latest.HasValue && (now > latest || arrival > latest || start > latest)) return MaterialDeliveryStatus.Red;
        if (complete && latest.HasValue && arrival.HasValue && start.HasValue && now < latest && arrival < latest && start < latest)
            return MaterialDeliveryStatus.Green;
        return MaterialDeliveryStatus.Yellow;
    }

    public static MaterialDeliveryResponse Create(PlanningSuggestion suggestion,
        IReadOnlyCollection<MaterialDeliveryDemandSource> demands, IReadOnlyCollection<MaterialDeliverySupplySource> supply,
        IReadOnlyCollection<MaterialDeliveryOrderSourceContract> scheduling, int productionSourceCount, string? planId, DateTimeOffset now, bool productionDueSourcesComplete)
    {
        var lines = supply.Where(x => x.SiteCode == suggestion.SiteCode && x.SkuCode == suggestion.SkuCode && x.UomCode == suggestion.UomCode)
            .OrderBy(x => x.PromisedDate).ThenBy(x => x.PurchaseOrderNo, StringComparer.Ordinal).ThenBy(x => x.LineNo, StringComparer.Ordinal).ToArray();
        var accumulated = 0m;
        DateOnly? arrivalDate = null;
        foreach (var line in lines)
        {
            accumulated += line.OpenQuantity;
            if (accumulated >= suggestion.NetRequirementQuantity) { arrivalDate = line.PromisedDate; break; }
        }
        var total = lines.Sum(x => x.OpenQuantity);
        var covered = Math.Min(total, suggestion.NetRequirementQuantity);
        var latest = scheduling.Where(x => x.LatestStartUtc.HasValue).Select(x => x.LatestStartUtc).DefaultIfEmpty().Min();
        // 关联多个装配工单时，物料需要赶上最早的实际开始；保留每个工单的独立来源。
        var start = scheduling.Where(x => x.ScheduledStartUtc.HasValue).Select(x => x.ScheduledStartUtc).DefaultIfEmpty().Min();
        var arrival = arrivalDate.HasValue ? ToUtc(arrivalDate.Value) : (DateTimeOffset?)null;
        var reasons = new List<string>();
        if (covered < suggestion.NetRequirementQuantity) reasons.Add("supply-insufficient");
        if (demands.Count == 0 || demands.Any(x => !x.DueDate.HasValue)) reasons.Add("demand-due-source-missing");
        if (planId is null) reasons.Add("plan-not-selected");
        if (productionSourceCount == 0) reasons.Add("production-suggestion-not-linked");
        if (!productionDueSourcesComplete) reasons.Add("production-demand-due-source-missing");
        if (scheduling.Count != productionSourceCount) reasons.Add("scheduling-source-missing");
        reasons.AddRange(scheduling.Where(x => x.Status != "scheduled").Select(x => x.Status));
        if (!latest.HasValue) reasons.Add("latest-start-missing");
        if (!start.HasValue) reasons.Add("expected-start-missing");
        if (latest.HasValue)
        {
            if (now > latest) reasons.Add("evaluation-after-latest-start");
            if (arrival > latest) reasons.Add("arrival-after-latest-start");
            if (start > latest) reasons.Add("start-after-latest-start");
            if (now == latest || arrival == latest || start == latest) reasons.Add("at-latest-start-boundary");
        }
        var net = new MaterialDeliveryNetRequirementSource(suggestion.GrossDemandQuantity, suggestion.OnHandQuantity,
            suggestion.ReservedQuantity, suggestion.AvailableToNetQuantity, suggestion.ScheduledReceiptQuantity,
            suggestion.SafetyStockQuantity, suggestion.NetRequirementQuantity, suggestion.PlannedQuantity,
            suggestion.ScrapRate, suggestion.YieldRate, suggestion.Formula, suggestion.UomConversionSummary);
        return new(suggestion.Id.ToString(), suggestion.MrpRunId.ToString(), suggestion.SuggestionType, suggestion.Status.ToString(),
            suggestion.SkuCode, suggestion.UomCode, suggestion.SiteCode, suggestion.NetRequirementQuantity,
            suggestion.ReleaseDate, ToUtc(suggestion.ReleaseDate), arrivalDate, arrival, start, latest, covered,
            suggestion.NetRequirementQuantity - covered, EvaluateStatus(now, latest, arrival, start, reasons.Count == 0),
            reasons.Distinct(StringComparer.Ordinal).ToArray(), net, demands, lines, scheduling);
    }
}
