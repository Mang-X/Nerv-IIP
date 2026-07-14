using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.NonconformanceReports;

/// <summary>
/// 按 id 取单条 NCR。<paramref name="OrganizationId"/> / <paramref name="EnvironmentId"/> 提供时按
/// 租户过滤（网关 facade 必传，越权 id 与不存在同样返回 not found，不泄露跨租户数据）；
/// 留空保持既有内部调用语义。
/// </summary>
public sealed record GetNonconformanceReportQuery(
    NonconformanceReportId NcrId,
    string? OrganizationId = null,
    string? EnvironmentId = null) : IQuery<NonconformanceReportResponse>;

public sealed class GetNonconformanceReportQueryValidator : AbstractValidator<GetNonconformanceReportQuery>
{
    public GetNonconformanceReportQueryValidator()
    {
        RuleFor(x => x.NcrId).NotEmpty();
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
    }
}

public sealed class GetNonconformanceReportQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetNonconformanceReportQuery, NonconformanceReportResponse>
{
    public async Task<NonconformanceReportResponse> Handle(GetNonconformanceReportQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.NonconformanceReports
            .AsNoTracking()
            .Where(x => x.Id == request.NcrId);
        if (!string.IsNullOrWhiteSpace(request.OrganizationId))
        {
            query = query.Where(x => x.OrganizationId == request.OrganizationId);
        }

        if (!string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            query = query.Where(x => x.EnvironmentId == request.EnvironmentId);
        }

        var ncr = await query.FirstOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"NCR '{request.NcrId}' was not found.");
        return ListNonconformanceReportsQueryHandler.ToResponse(ncr);
    }
}
