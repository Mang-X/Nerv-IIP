using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Queries;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionRecords;

public sealed record InspectionRecordResponse(
    InspectionRecordId InspectionRecordId,
    string OrganizationId,
    string EnvironmentId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string SkuCode,
    decimal InspectedQuantity,
    string? BatchNo,
    string? SerialNo,
    string? UomCode,
    string? SiteCode,
    string? LocationCode,
    string? SourceQualityStatus,
    string? OwnerType,
    string? OwnerId,
    string Result,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds,
    IReadOnlyCollection<InspectionResultLineResponse> ResultLines,
    DateTime CreatedAtUtc,
    // 详情读补充：回链的 NCR id（记录 → NCR 双向互查）；列表投影不需要，保持 null。
    string? NonconformanceReportId = null,
    int AttemptNumber = 1,
    InspectionRecordId? ReinspectionOfInspectionRecordId = null);

public sealed record InspectionResultLineResponse(
    string CharacteristicCode,
    string ObservedValue,
    decimal? MeasuredValue,
    string? UnitCode,
    string Result,
    string? DefectReason,
    decimal? DefectQuantity,
    IReadOnlyCollection<string> AttachmentFileIds);

public sealed record ListInspectionRecordsResponse(IReadOnlyCollection<InspectionRecordResponse> Items, int Total);

public sealed record ListInspectionRecordsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SourceService,
    string? SourceDocumentId,
    string? SourceType,
    string? SkuCode,
    string? Result,
    int Skip = 0,
    int Take = OffsetPage.DefaultTake) : IQuery<ListInspectionRecordsResponse>;

public sealed class ListInspectionRecordsQueryValidator : AbstractValidator<ListInspectionRecordsQuery>
{
    public ListInspectionRecordsQueryValidator()
    {
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        this.AddOffsetPageRules(x => x.Skip, x => x.Take);
    }
}

public sealed class ListInspectionRecordsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListInspectionRecordsQuery, ListInspectionRecordsResponse>
{
    public async Task<ListInspectionRecordsResponse> Handle(ListInspectionRecordsQuery request, CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var page = OffsetPage.From(request.Skip, request.Take);
        var query = dbContext.InspectionRecords
            .AsNoTracking()
            .Include(x => x.ResultLines)
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.SourceService))
        {
            query = query.Where(x => x.SourceService == request.SourceService);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceDocumentId))
        {
            query = query.Where(x => x.SourceDocumentId == request.SourceDocumentId);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceType))
        {
            query = query.Where(x => x.SourceType == request.SourceType);
        }

        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            query = query.Where(x => x.SkuCode == request.SkuCode);
        }

        if (!string.IsNullOrWhiteSpace(request.Result))
        {
            query = query.Where(x => x.Result == request.Result);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.Take)
            .Select(x => new InspectionRecordResponse(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.SourceType,
                x.SourceService,
                x.SourceDocumentId,
                x.SkuCode,
                x.InspectedQuantity,
                x.BatchNo,
                x.SerialNo,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.SourceQualityStatus,
                x.OwnerType,
                x.OwnerId,
                x.Result,
                x.DispositionReason,
                x.DispositionAttachmentFileIds,
                x.ResultLines.Select(line => new InspectionResultLineResponse(
                    line.CharacteristicCode,
                    line.ObservedValue,
                    line.MeasuredValue,
                    line.UnitCode,
                    line.Result,
                    line.DefectReason,
                    line.DefectQuantity,
                    line.AttachmentFileIds)).ToArray(),
                x.CreatedAtUtc,
                null,
                x.AttemptNumber,
                x.ReinspectionOfInspectionRecordId))
            .ToListAsync(cancellationToken);

        return new ListInspectionRecordsResponse(items, total);
    }
}
