using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Production;

public sealed record ListTelemetryProductionReportCandidatesQuery(string OrganizationId, string EnvironmentId, string? Status,
    string? WorkCenterId, string? DeviceAssetId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, int Skip, int Take)
    : IQuery<TelemetryProductionReportCandidateListResponse>;
public sealed record GetTelemetryProductionReportCandidateQuery(string OrganizationId, string EnvironmentId, TelemetryProductionReportCandidateId CandidateId)
    : IQuery<TelemetryProductionReportCandidateFact>;
public sealed record TelemetryProductionReportCandidateListResponse(IReadOnlyCollection<TelemetryProductionReportCandidateFact> Items, int Total);
public sealed record TelemetryProductionReportCandidateTransitionFact(string FromStatus, string ToStatus, string Actor, string? Reason, DateTimeOffset OccurredAtUtc);
public sealed record TelemetryProductionReportCandidateFact(string CandidateId, string OrganizationId, string EnvironmentId, string Status,
    string ReportingMode, string DeviceAssetId, string TagKey, decimal GoodQuantity, DateTimeOffset BucketStartUtc, DateTimeOffset BucketEndUtc,
    string? WorkCenterId, string? WorkOrderId, string? OperationTaskId, string? SuspensionReason, string SourceIdempotencyKey,
    string? ResolutionReason, string? ResolvedBy, DateTimeOffset? ResolvedAtUtc, string? ProductionReportId,
    IReadOnlyCollection<TelemetryProductionReportCandidateTransitionFact> Transitions);

internal static class TelemetryCandidateProjection
{
    public static TelemetryProductionReportCandidateFact Map(TelemetryProductionReportCandidate x) => new(
        x.Id.ToString(), x.OrganizationId, x.EnvironmentId, x.Status, x.ReportingMode, x.DeviceAssetId, x.TagKey, x.GoodQuantity,
        x.BucketStartUtc, x.BucketEndUtc, x.WorkCenterId, x.WorkOrderId, x.OperationTaskId, x.SuspensionReason, x.SourceIdempotencyKey,
        x.ResolutionReason, x.ResolvedBy, x.ResolvedAtUtc, x.ProductionReportId,
        x.Transitions.OrderBy(t => t.OccurredAtUtc).Select(t => new TelemetryProductionReportCandidateTransitionFact(t.FromStatus, t.ToStatus, t.Actor, t.Reason, t.OccurredAtUtc)).ToArray());
}

public sealed class ListTelemetryProductionReportCandidatesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListTelemetryProductionReportCandidatesQuery, TelemetryProductionReportCandidateListResponse>
{
    public async Task<TelemetryProductionReportCandidateListResponse> Handle(ListTelemetryProductionReportCandidatesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.TelemetryProductionReportCandidates.AsNoTracking().Include(x => x.Transitions)
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);
        if (!string.IsNullOrWhiteSpace(request.Status)) query = query.Where(x => x.Status == request.Status.Trim().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(request.WorkCenterId)) query = query.Where(x => x.WorkCenterId == request.WorkCenterId.Trim());
        if (!string.IsNullOrWhiteSpace(request.DeviceAssetId)) query = query.Where(x => x.DeviceAssetId == request.DeviceAssetId.Trim());
        if (request.FromUtc is not null) query = query.Where(x => x.BucketEndUtc >= request.FromUtc);
        if (request.ToUtc is not null) query = query.Where(x => x.BucketStartUtc <= request.ToUtc);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).Skip(Math.Max(0, request.Skip)).Take(Math.Clamp(request.Take, 1, 200)).ToArrayAsync(cancellationToken);
        return new(rows.Select(TelemetryCandidateProjection.Map).ToArray(), total);
    }
}

public sealed class GetTelemetryProductionReportCandidateQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetTelemetryProductionReportCandidateQuery, TelemetryProductionReportCandidateFact>
{
    public async Task<TelemetryProductionReportCandidateFact> Handle(GetTelemetryProductionReportCandidateQuery request, CancellationToken cancellationToken)
    {
        var row = await dbContext.TelemetryProductionReportCandidates.AsNoTracking().Include(x => x.Transitions).SingleOrDefaultAsync(
            x => x.Id == request.CandidateId && x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId, cancellationToken)
            ?? throw new KnownException("Telemetry production report candidate was not found.");
        return TelemetryCandidateProjection.Map(row);
    }
}
