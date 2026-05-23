using Microsoft.EntityFrameworkCore;
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
    IReadOnlyCollection<LabelPrintItemDetail> Items);

public sealed record LabelPrintItemDetail(int SequenceNo, string LabelValue, string? FileId);

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
                x.Items.OrderBy(item => item.SequenceNo).Select(item => new LabelPrintItemDetail(item.SequenceNo, item.LabelValue, item.FileId)).ToArray()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"Print batch not found, PrintBatchId = {request.PrintBatchId}");
    }
}
