using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionRecords;

/// <summary>
/// 按 id 取单条检验记录（PDA NCR 详情「来源检验记录」→ 打开记录详情的互链读）。
/// org/env 提供时按租户过滤（网关 facade 必传，越权 id 与不存在同为 not found）；留空保持内部语义。
/// </summary>
public sealed record GetInspectionRecordQuery(
    InspectionRecordId InspectionRecordId,
    string? OrganizationId = null,
    string? EnvironmentId = null) : IQuery<InspectionRecordResponse>;

public sealed class GetInspectionRecordQueryValidator : AbstractValidator<GetInspectionRecordQuery>
{
    public GetInspectionRecordQueryValidator()
    {
        RuleFor(x => x.InspectionRecordId).NotEmpty();
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
    }
}

public sealed class GetInspectionRecordQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetInspectionRecordQuery, InspectionRecordResponse>
{
    public async Task<InspectionRecordResponse> Handle(GetInspectionRecordQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.InspectionRecords
            .AsNoTracking()
            .Include(x => x.ResultLines)
            .Where(x => x.Id == request.InspectionRecordId);
        if (!string.IsNullOrWhiteSpace(request.OrganizationId))
        {
            query = query.Where(x => x.OrganizationId == request.OrganizationId);
        }

        if (!string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            query = query.Where(x => x.EnvironmentId == request.EnvironmentId);
        }

        var record = await query.FirstOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Inspection record '{request.InspectionRecordId}' was not found.");
        return new InspectionRecordResponse(
            record.Id,
            record.OrganizationId,
            record.EnvironmentId,
            record.SourceType,
            record.SourceService,
            record.SourceDocumentId,
            record.SkuCode,
            record.InspectedQuantity,
            record.BatchNo,
            record.SerialNo,
            record.UomCode,
            record.SiteCode,
            record.LocationCode,
            record.SourceQualityStatus,
            record.OwnerType,
            record.OwnerId,
            record.Result,
            record.DispositionReason,
            record.DispositionAttachmentFileIds,
            record.ResultLines.Select(line => new InspectionResultLineResponse(
                line.CharacteristicCode,
                line.ObservedValue,
                line.MeasuredValue,
                line.UnitCode,
                line.Result,
                line.DefectReason,
                line.DefectQuantity,
                line.AttachmentFileIds)).ToArray(),
            record.CreatedAtUtc,
            record.NonconformanceReportId);
    }
}
