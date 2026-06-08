using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Scans;

public sealed record ListScansQuery(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceCode,
    string? ScannedValue,
    string? SourceWorkflow,
    string? SourceDocumentId,
    int Skip = 0,
    int Take = 100) : IQuery<ScanRecordListResult>;

public sealed record ScanRecordListResult(IReadOnlyCollection<ScanRecordSummary> Items, int Total);

public sealed record ScanRecordSummary(
    ScanRecordId ScanRecordId,
    string DeviceCode,
    string ScannedValue,
    string SourceWorkflow,
    string SourceDocumentId,
    string Result,
    string? RejectionReason,
    DateTimeOffset ScannedAtUtc);

public sealed class ListScansQueryValidator : AbstractValidator<ListScansQuery>
{
    public ListScansQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceCode).MaximumLength(100);
        RuleFor(x => x.ScannedValue).MaximumLength(200);
        RuleFor(x => x.SourceWorkflow).MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).MaximumLength(150);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 500);
    }
}

public sealed class ListScansQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListScansQuery, ScanRecordListResult>
{
    public async Task<ScanRecordListResult> Handle(ListScansQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ScanRecords.Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);
        if (!string.IsNullOrWhiteSpace(request.DeviceCode))
        {
            query = query.Where(x => x.DeviceCode == request.DeviceCode);
        }

        if (!string.IsNullOrWhiteSpace(request.ScannedValue))
        {
            query = query.Where(x => x.ScannedValue == request.ScannedValue);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceWorkflow))
        {
            query = query.Where(x => x.SourceWorkflow == request.SourceWorkflow);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceDocumentId))
        {
            query = query.Where(x => x.SourceDocumentId == request.SourceDocumentId);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.ScannedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new ScanRecordSummary(x.Id, x.DeviceCode, x.ScannedValue, x.SourceWorkflow, x.SourceDocumentId, x.Result, x.RejectionReason, x.ScannedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ScanRecordListResult(items, total);
    }
}
