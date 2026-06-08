using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.PrintBatches;

public sealed record ListLabelPrintBatchesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SourceDocumentType,
    string? SourceDocumentId,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<LabelPrintBatchListResult>;

public sealed record LabelPrintBatchListResult(IReadOnlyCollection<LabelPrintBatchSummary> Items, int Total);

public sealed record LabelPrintBatchSummary(
    LabelPrintBatchId PrintBatchId,
    LabelTemplateId LabelTemplateId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    int RequestedQuantity,
    string Status,
    DateTimeOffset CreatedAtUtc);

public sealed class ListLabelPrintBatchesQueryValidator : AbstractValidator<ListLabelPrintBatchesQuery>
{
    public ListLabelPrintBatchesQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentType).MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).MaximumLength(150);
        RuleFor(x => x.Status).MaximumLength(30);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListLabelPrintBatchesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListLabelPrintBatchesQuery, LabelPrintBatchListResult>
{
    public async Task<LabelPrintBatchListResult> Handle(ListLabelPrintBatchesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.LabelPrintBatches
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);
        if (!string.IsNullOrWhiteSpace(request.SourceDocumentType))
        {
            var sourceDocumentType = request.SourceDocumentType.Trim().ToLowerInvariant();
            query = query.Where(x => x.SourceDocumentType == sourceDocumentType);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceDocumentId))
        {
            query = query.Where(x => x.SourceDocumentId == request.SourceDocumentId);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new LabelPrintBatchSummary(
                x.Id,
                x.LabelTemplateId,
                x.SourceDocumentType,
                x.SourceDocumentId,
                x.IdempotencyKey,
                x.RequestedQuantity,
                x.Status,
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new LabelPrintBatchListResult(items, total);
    }
}
