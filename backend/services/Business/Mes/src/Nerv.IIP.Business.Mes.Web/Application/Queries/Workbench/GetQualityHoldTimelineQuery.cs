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
    // 强类型 id 以字符串对外投影(与其他读面一致),避免网关按 Guid/字符串反序列化下游强类型 id 失败。
    string TransitionId,
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
        // 先取原始行(含强类型 Id),再在内存端投影为字符串 TransitionId——强类型 id 的 ToString() 不宜进 EF SQL 翻译。
        var persistedItems = await dbContext.QualityHoldTransitions
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourceService == request.SourceService &&
                x.SourceDocumentId == request.SourceDocumentId)
            .Select(x => new
            {
                x.Id,
                x.SourceService,
                x.SourceDocumentId,
                x.HoldCycleId,
                x.CorrelationId,
                x.EventKind,
                x.Actor,
                x.OccurredAtUtc,
                x.Reason,
                x.SourceInspectionRecordId,
                x.SourceInspectionDocumentId,
                x.Origin,
                x.IdempotencyKey,
            })
            .ToListAsync(cancellationToken);
        var items = persistedItems
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id.ToString(), StringComparer.Ordinal)
            .Select(x => new QualityHoldTimelineItem(
                x.Id.ToString(), x.SourceService, x.SourceDocumentId, x.HoldCycleId, x.CorrelationId,
                x.EventKind, x.Actor, x.OccurredAtUtc, x.Reason, x.SourceInspectionRecordId,
                x.SourceInspectionDocumentId, x.Origin, x.IdempotencyKey))
            .ToList();
        return new QualityHoldTimelineResponse(items);
    }
}
