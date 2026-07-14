using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.NonconformanceReports;

public sealed record NonconformanceReportResponse(
    NonconformanceReportId NcrId,
    string OrganizationId,
    string EnvironmentId,
    string NcrCode,
    string SourceType,
    string SourceDocumentId,
    string SkuCode,
    decimal DefectQuantity,
    string DefectReason,
    string? BatchNo,
    string? SerialNo,
    string Status,
    string? DispositionType,
    string? DispositionApprovalChainId,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId,
    IReadOnlyCollection<string> AttachmentFileIds,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    // 权威业务关系：从检验开出的 NCR 回链其来源检验记录（记录 ↔ NCR 双向互查的服务端事实）。
    string? SourceInspectionRecordId = null);

public sealed record ListNonconformanceReportsResponse(IReadOnlyCollection<NonconformanceReportResponse> Items, int Total);

public sealed record ListNonconformanceReportsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? SourceType,
    string? SkuCode,
    string? Keyword = null,
    int Skip = 0,
    int Take = 100) : IQuery<ListNonconformanceReportsResponse>;

public sealed class ListNonconformanceReportsQueryValidator : AbstractValidator<ListNonconformanceReportsQuery>
{
    public ListNonconformanceReportsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Keyword).MaximumLength(200);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListNonconformanceReportsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListNonconformanceReportsQuery, ListNonconformanceReportsResponse>
{
    public async Task<ListNonconformanceReportsResponse> Handle(ListNonconformanceReportsQuery request, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.Take, 1, 500);
        var query = dbContext.NonconformanceReports
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(x => x.Status == request.Status);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceType))
        {
            query = query.Where(x => x.SourceType == request.SourceType);
        }

        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            query = query.Where(x => x.SkuCode == request.SkuCode);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            var hasKeywordId = Guid.TryParse(keyword, out var keywordGuid);
            var keywordId = hasKeywordId ? new NonconformanceReportId(keywordGuid) : null;
            query = query.Where(x =>
                (keywordId != null && x.Id == keywordId)
                || x.NcrCode.ToLower().Contains(keyword)
                || x.SourceDocumentId.ToLower().Contains(keyword));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(request.Skip)
            .Take(take)
            .Select(x => ToResponse(x))
            .ToListAsync(cancellationToken);

        return new ListNonconformanceReportsResponse(items, total);
    }

    internal static NonconformanceReportResponse ToResponse(NonconformanceReport ncr)
    {
        return new NonconformanceReportResponse(
            ncr.Id,
            ncr.OrganizationId,
            ncr.EnvironmentId,
            ncr.NcrCode,
            ncr.SourceType,
            ncr.SourceDocumentId,
            ncr.SkuCode,
            ncr.DefectQuantity,
            ncr.DefectReason,
            ncr.BatchNo,
            ncr.SerialNo,
            ncr.Status,
            ncr.DispositionType,
            ncr.DispositionApprovalChainId,
            ncr.ReworkWorkOrderId,
            ncr.ScrapMovementId,
            ncr.ReturnDocumentId,
            ncr.AttachmentFileIds,
            ncr.CreatedAtUtc,
            ncr.UpdatedAtUtc,
            ncr.SourceInspectionRecordId == null ? null : ncr.SourceInspectionRecordId.ToString());
    }
}
