using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.MrpRunAggregate;
using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;
using Nerv.IIP.Business.DemandPlanning.Infrastructure;

namespace Nerv.IIP.Business.DemandPlanning.Web.Application.Queries;

public sealed record ListDemandSourcesQuery(string OrganizationId, string EnvironmentId) : IQuery<IReadOnlyCollection<DemandSourceResponse>>;

public sealed record DemandSourceResponse(
    string DemandSourceId,
    string DemandType,
    string SourceReference,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly DueDate);

public sealed class ListDemandSourcesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListDemandSourcesQuery, IReadOnlyCollection<DemandSourceResponse>>
{
    public async Task<IReadOnlyCollection<DemandSourceResponse>> Handle(ListDemandSourcesQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.DemandSources.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.SourceReference)
            .Select(x => new DemandSourceResponse(
                x.Id.ToString(),
                x.DemandType,
                x.SourceReference,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.Quantity,
                x.DueDate))
            .ToListAsync(cancellationToken);
    }
}

public sealed record ListMrpRunsQuery(string OrganizationId, string EnvironmentId) : IQuery<IReadOnlyCollection<MrpRunResponse>>;

public sealed record MrpRunResponse(
    MrpRunId RunId,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    MrpRunStatus Status,
    int DemandCount,
    int AvailabilityCount,
    int SuggestionCount);

public sealed class ListMrpRunsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMrpRunsQuery, IReadOnlyCollection<MrpRunResponse>>
{
    public async Task<IReadOnlyCollection<MrpRunResponse>> Handle(ListMrpRunsQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.MrpRuns.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new MrpRunResponse(x.Id, x.HorizonStart, x.HorizonEnd, x.Status, x.DemandCount, x.AvailabilityCount, x.SuggestionCount))
            .ToListAsync(cancellationToken);
    }
}

public sealed record ListPlanningSuggestionsQuery(string OrganizationId, string EnvironmentId, string? Status) : IQuery<IReadOnlyCollection<PlanningSuggestionResponse>>;

public sealed record PlanningSuggestionResponse(
    PlanningSuggestionId SuggestionId,
    MrpRunId MrpRunId,
    string SuggestionType,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    PlanningSuggestionStatus Status,
    string? AcceptedDownstreamDocumentId);

public sealed class ListPlanningSuggestionsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListPlanningSuggestionsQuery, IReadOnlyCollection<PlanningSuggestionResponse>>
{
    public async Task<IReadOnlyCollection<PlanningSuggestionResponse>> Handle(ListPlanningSuggestionsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.PlanningSuggestions.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);
        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<PlanningSuggestionStatus>(request.Status, ignoreCase: true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderBy(x => x.RequiredDate).ThenBy(x => x.SkuCode)
            .Select(x => new PlanningSuggestionResponse(
                x.Id,
                x.MrpRunId,
                x.SuggestionType,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.Quantity,
                x.RequiredDate,
                x.Status,
                x.AcceptedDownstreamDocumentId))
            .ToListAsync(cancellationToken);
    }
}

public sealed record ListMrpPeggingQuery(MrpRunId RunId) : IQuery<IReadOnlyCollection<PeggingLinkResponse>>;

public sealed record PeggingLinkResponse(
    PlanningSuggestionId SuggestionId,
    string PeggingType,
    string DemandSourceReference,
    string ParentSkuCode,
    string? ComponentSkuCode,
    decimal Quantity,
    string? ProductionVersionReference,
    string? ManufacturingBomReference,
    string? RoutingReference);

public sealed class ListMrpPeggingQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMrpPeggingQuery, IReadOnlyCollection<PeggingLinkResponse>>
{
    public async Task<IReadOnlyCollection<PeggingLinkResponse>> Handle(ListMrpPeggingQuery request, CancellationToken cancellationToken)
    {
        var suggestions = await dbContext.PlanningSuggestions.AsNoTracking()
            .Include(x => x.PeggingLinks)
            .Where(x => x.MrpRunId == request.RunId)
            .ToListAsync(cancellationToken);

        return suggestions
            .SelectMany(x => x.PeggingLinks.Select(link => new PeggingLinkResponse(
                x.Id,
                link.PeggingType,
                link.DemandSourceReference,
                link.ParentSkuCode,
                link.ComponentSkuCode,
                link.Quantity,
                link.ProductionVersionReference,
                link.ManufacturingBomReference,
                link.RoutingReference)))
            .ToArray();
    }
}
