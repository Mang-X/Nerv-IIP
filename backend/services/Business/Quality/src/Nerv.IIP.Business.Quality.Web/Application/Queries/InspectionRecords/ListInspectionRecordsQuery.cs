using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;

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
    string Result,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds,
    IReadOnlyCollection<InspectionResultLineResponse> ResultLines,
    DateTime CreatedAtUtc);

public sealed record InspectionResultLineResponse(
    string CharacteristicCode,
    string ObservedValue,
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
    int Take = 100) : IQuery<ListInspectionRecordsResponse>;

public sealed class ListInspectionRecordsQueryValidator : AbstractValidator<ListInspectionRecordsQuery>
{
    public ListInspectionRecordsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListInspectionRecordsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListInspectionRecordsQuery, ListInspectionRecordsResponse>
{
    public async Task<ListInspectionRecordsResponse> Handle(ListInspectionRecordsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.InspectionRecords
            .AsNoTracking()
            .Include(x => x.ResultLines)
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

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
            .Skip(request.Skip)
            .Take(Math.Clamp(request.Take, 1, 500))
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
                x.Result,
                x.DispositionReason,
                x.DispositionAttachmentFileIds,
                x.ResultLines.Select(line => new InspectionResultLineResponse(
                    line.CharacteristicCode,
                    line.ObservedValue,
                    line.UnitCode,
                    line.Result,
                    line.DefectReason,
                    line.DefectQuantity,
                    line.AttachmentFileIds)).ToArray(),
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new ListInspectionRecordsResponse(items, total);
    }
}
