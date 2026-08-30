using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Scans;

public sealed record ListScansQuery(
    string OrganizationId,
    string EnvironmentId,
    string? DeviceCode,
    string? ScannedValue,
    string? SourceWorkflow,
    string? SourceDocumentId,
    int Skip = 0,
    int Take = OffsetPage.DefaultTake) : IQuery<ScanRecordListResult>;

public sealed record ScanRecordListResult(IReadOnlyCollection<ScanRecordSummary> Items, int Total);

public sealed record ScanRecordSummary(
    ScanRecordId ScanRecordId,
    string DeviceCode,
    string ScannedValue,
    string SourceWorkflow,
    string SourceDocumentId,
    string Result,
    string? RejectionReason,
    string DownstreamProcessingStatus,
    DateTimeOffset ScannedAtUtc);

public sealed class ListScansQueryValidator : AbstractValidator<ListScansQuery>
{
    public ListScansQueryValidator()
    {
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
        RuleFor(x => x.DeviceCode).MaximumLength(100);
        RuleFor(x => x.ScannedValue).MaximumLength(200);
        RuleFor(x => x.SourceWorkflow).MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).MaximumLength(150);
    }
}

public sealed class ListScansQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListScansQuery, ScanRecordListResult>
{
    public async Task<ScanRecordListResult> Handle(ListScansQuery request, CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var page = OffsetPage.From(request.Skip, request.Take);
        var query = dbContext.ScanRecords.Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId);
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
            .Skip(page.Skip)
            .Take(page.Take)
            .Select(x => new ScanRecordSummary(x.Id, x.DeviceCode, x.ScannedValue, x.SourceWorkflow, x.SourceDocumentId, x.Result, x.RejectionReason, x.DownstreamProcessingStatus, x.ScannedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new ScanRecordListResult(items, total);
    }
}
