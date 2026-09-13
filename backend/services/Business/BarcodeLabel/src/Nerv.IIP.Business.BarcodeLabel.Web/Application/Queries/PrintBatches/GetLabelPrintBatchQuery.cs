using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.PrintBatches;

public sealed record GetLabelPrintBatchQuery(
    LabelPrintBatchId PrintBatchId,
    string OrganizationId,
    string EnvironmentId) : IQuery<LabelPrintBatchDetail>;

public sealed record LabelPrintBatchDetail(
    LabelPrintBatchId PrintBatchId,
    LabelTemplateId LabelTemplateId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    string ReportIntentKey,
    int RequestedQuantity,
    string Status,
    string? PrinterId,
    string? PrintJobId,
    string? FailureReason,
    string? ProductionReportId,
    string? ProductionReportNo,
    IReadOnlyCollection<LabelPrintItemDetail> Items);

public sealed record LabelPrintItemDetail(
    int SequenceNo,
    string LabelValue,
    string? FileId,
    string Status,
    string? VoidReason,
    string? SerialNumber,
    string? LotNo,
    string? Gtin,
    string? EpcUri);

public sealed class GetLabelPrintBatchQueryValidator : AbstractValidator<GetLabelPrintBatchQuery>
{
    public GetLabelPrintBatchQueryValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetLabelPrintBatchQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetLabelPrintBatchQuery, LabelPrintBatchDetail>
{
    public async Task<LabelPrintBatchDetail> Handle(GetLabelPrintBatchQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.LabelPrintBatches
            .Where(x => x.Id == request.PrintBatchId
                && x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId)
            .Select(x => new LabelPrintBatchDetail(
                x.Id,
                x.LabelTemplateId,
                x.SourceDocumentType,
                x.SourceDocumentId,
                x.IdempotencyKey,
                x.IdempotencyKey,
                x.RequestedQuantity,
                x.Status,
                x.PrinterId,
                x.PrintJobId,
                x.FailureReason,
                x.ProductionReportId,
                x.ProductionReportNo,
                x.Items.OrderBy(item => item.SequenceNo).Select(item => new LabelPrintItemDetail(
                    item.SequenceNo,
                    item.LabelValue,
                    item.FileId,
                    item.Status,
                    item.VoidReason,
                    item.SerialNumber,
                    item.LotNo,
                    item.Gtin,
                    item.EpcUri)).ToArray()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException("未找到打印批次。");
    }
}
