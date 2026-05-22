using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.NonconformanceReports;

public sealed record GetNonconformanceReportQuery(NonconformanceReportId NcrId) : IQuery<NonconformanceReportResponse>;

public sealed class GetNonconformanceReportQueryValidator : AbstractValidator<GetNonconformanceReportQuery>
{
    public GetNonconformanceReportQueryValidator()
    {
        RuleFor(x => x.NcrId).NotEmpty();
    }
}

public sealed class GetNonconformanceReportQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetNonconformanceReportQuery, NonconformanceReportResponse>
{
    public async Task<NonconformanceReportResponse> Handle(GetNonconformanceReportQuery request, CancellationToken cancellationToken)
    {
        var ncr = await dbContext.NonconformanceReports
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.NcrId, cancellationToken)
            ?? throw new KnownException($"NCR '{request.NcrId}' was not found.");
        return ListNonconformanceReportsQueryHandler.ToResponse(ncr);
    }
}
