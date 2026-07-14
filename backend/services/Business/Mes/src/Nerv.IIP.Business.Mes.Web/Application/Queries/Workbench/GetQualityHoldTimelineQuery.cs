using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

public sealed record GetQualityHoldTimelineQuery(
    string OrganizationId,
    string EnvironmentId,
    string SourceService,
    string SourceDocumentId) : IQuery<QualityHoldTimelineResponse>;

public sealed record QualityHoldTimelineItem(
    QualityHoldTransitionId TransitionId,
    string SourceService,
    string SourceDocumentId,
    string HoldCycleId,
    string CorrelationId,
    string EventKind,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string? Reason,
    string? SourceInspectionRecordId,
    string? SourceInspectionDocumentId,
    string Origin,
    string? IdempotencyKey);

public sealed record QualityHoldTimelineResponse(IReadOnlyCollection<QualityHoldTimelineItem> Items);

public sealed class GetQualityHoldTimelineQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetQualityHoldTimelineQuery, QualityHoldTimelineResponse>
{
    public async Task<QualityHoldTimelineResponse> Handle(
        GetQualityHoldTimelineQuery request,
        CancellationToken cancellationToken)
    {
        var persistedItems = await dbContext.QualityHoldTransitions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourceService == request.SourceService &&
                x.SourceDocumentId == request.SourceDocumentId)
            .Select(x => new QualityHoldTimelineItem(
                x.Id, x.SourceService, x.SourceDocumentId, x.HoldCycleId, x.CorrelationId,
                x.EventKind, x.Actor, x.OccurredAtUtc, x.Reason, x.SourceInspectionRecordId,
                x.SourceInspectionDocumentId, x.Origin, x.IdempotencyKey))
            .ToListAsync(cancellationToken);
        var items = persistedItems
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.TransitionId.ToString(), StringComparer.Ordinal)
            .ToList();
        return new QualityHoldTimelineResponse(items);
    }
}
