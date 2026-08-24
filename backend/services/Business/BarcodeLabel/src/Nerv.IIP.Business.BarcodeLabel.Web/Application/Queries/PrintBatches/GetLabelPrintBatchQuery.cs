using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.PrintBatches;

public sealed record GetLabelPrintBatchQuery(LabelPrintBatchId PrintBatchId) : IQuery<LabelPrintBatchDetail>;

public sealed record LabelPrintBatchDetail(
    LabelPrintBatchId PrintBatchId,
    LabelTemplateId LabelTemplateId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    int RequestedQuantity,
    string Status,
    [property: JsonPropertyName("printerId")] string? LatestTransportPrinterId,
    [property: JsonPropertyName("printJobId")] string? LatestTransportPrintJobId,
    [property: JsonPropertyName("failureReason")] string? LatestTransportFailureReason,
    IReadOnlyCollection<LabelPrintItemDetail> Items);

public sealed record LabelPrintItemDetail(int SequenceNo, string LabelValue, string? FileId, string Status, string? VoidReason);

public sealed class GetLabelPrintBatchQueryValidator : AbstractValidator<GetLabelPrintBatchQuery>
{
    public GetLabelPrintBatchQueryValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
    }
}

public sealed class GetLabelPrintBatchQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetLabelPrintBatchQuery, LabelPrintBatchDetail>
{
    public async Task<LabelPrintBatchDetail> Handle(GetLabelPrintBatchQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.LabelPrintBatches
            .Where(x => x.Id == request.PrintBatchId)
            .Select(x => new LabelPrintBatchDetail(
                x.Id,
                x.LabelTemplateId,
                x.SourceDocumentType,
                x.SourceDocumentId,
                x.IdempotencyKey,
                x.RequestedQuantity,
                x.Status,
                x.PrinterId,
                x.PrintJobId,
                x.FailureReason,
                x.Items.OrderBy(item => item.SequenceNo).Select(item => new LabelPrintItemDetail(item.SequenceNo, item.LabelValue, item.FileId, item.Status, item.VoidReason)).ToArray()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"未找到打印批次，批次 ID = {request.PrintBatchId}。");
    }
}
