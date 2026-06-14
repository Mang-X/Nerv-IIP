using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.QualityReasons;

public sealed record QualityReasonItem(
    string ReasonCode,
    string ReasonName,
    string GroupName,
    string Severity,
    string? DefaultDisposition,
    bool Enabled,
    string SnapshotVersion);

public sealed record QualityReasonListResponse(IReadOnlyCollection<QualityReasonItem> Items, int Total);

public sealed record ListQualityReasonsQuery(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled = null,
    string? Search = null,
    string? GroupName = null,
    int Skip = 0,
    int Take = 100) : IQuery<QualityReasonListResponse>;

public sealed record GetQualityReasonQuery(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode) : IQuery<QualityReasonItem>;

public sealed class ListQualityReasonsQueryValidator : AbstractValidator<ListQualityReasonsQuery>
{
    public ListQualityReasonsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.GroupName).MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class GetQualityReasonQueryValidator : AbstractValidator<GetQualityReasonQuery>
{
    public GetQualityReasonQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class ListQualityReasonsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListQualityReasonsQuery, QualityReasonListResponse>
{
    public async Task<QualityReasonListResponse> Handle(ListQualityReasonsQuery request, CancellationToken cancellationToken)
    {
        var keyword = NormalizeKeyword(request.Search);
        var query = dbContext.QualityReasons
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => !request.Enabled.HasValue || x.Enabled == request.Enabled.Value)
            .Where(x => string.IsNullOrWhiteSpace(request.GroupName) || x.GroupName == request.GroupName)
            .Where(x => keyword == null || x.ReasonCode.ToLower().Contains(keyword) || x.ReasonName.ToLower().Contains(keyword));
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.GroupName)
            .ThenBy(x => x.ReasonCode)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .ToListAsync(cancellationToken);

        return new QualityReasonListResponse(items.Select(QualityReasonMapper.ToItem).ToArray(), total);
    }

    private static string? NormalizeKeyword(string? keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim().ToLowerInvariant();
    }
}

public sealed class GetQualityReasonQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetQualityReasonQuery, QualityReasonItem>
{
    public async Task<QualityReasonItem> Handle(GetQualityReasonQuery request, CancellationToken cancellationToken)
    {
        var reason = await dbContext.QualityReasons.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.ReasonCode == request.ReasonCode,
            cancellationToken)
            ?? throw new KnownException($"Quality reason '{request.ReasonCode}' was not found.");
        return QualityReasonMapper.ToItem(reason);
    }
}

internal static class QualityReasonMapper
{
    public static QualityReasonItem ToItem(QualityReason reason)
    {
        return new QualityReasonItem(
            reason.ReasonCode,
            reason.ReasonName,
            reason.GroupName,
            reason.Severity,
            reason.DefaultDisposition,
            reason.Enabled,
            reason.UpdatedAtUtc.ToString("O"));
    }
}
