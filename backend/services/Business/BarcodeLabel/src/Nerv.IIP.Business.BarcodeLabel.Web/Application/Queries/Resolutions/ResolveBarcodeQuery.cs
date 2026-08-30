using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Resolutions;

public sealed record ResolveBarcodeQuery(
    string OrganizationId,
    string EnvironmentId,
    string ScannedValue,
    int Skip = 0,
    int Take = 20) : IQuery<ResolveBarcodeResult>;

public sealed record ResolveBarcodeResult(
    string Status,
    string? ReasonCode,
    IReadOnlyCollection<ResolvedBarcodeCandidate> Candidates,
    int Total);

public sealed record ResolvedBarcodeCandidate(
    string SourceDocumentType,
    string SourceDocumentId,
    string Authority,
    DateTimeOffset ObservedAtUtc);

public sealed class ResolveBarcodeQueryValidator : AbstractValidator<ResolveBarcodeQuery>
{
    public ResolveBarcodeQueryValidator()
    {
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
        RuleFor(x => x.ScannedValue).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Take).LessThanOrEqualTo(100);
    }
}

public sealed class ResolveBarcodeQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ResolveBarcodeQuery, ResolveBarcodeResult>
{
    public async Task<ResolveBarcodeResult> Handle(
        ResolveBarcodeQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var page = OffsetPage.From(request.Skip, request.Take);
        var matches =
                from item in dbContext.LabelPrintItems
                join batch in dbContext.LabelPrintBatches on item.LabelPrintBatchId equals batch.Id
                where batch.OrganizationId == tenant.OrganizationId
                    && batch.EnvironmentId == tenant.EnvironmentId
                    && item.LabelValue == request.ScannedValue
                select new
                {
                    batch.SourceDocumentType,
                    batch.SourceDocumentId,
                    batch.CreatedAtUtc,
                    item.Status,
                };

        if (await matches.AnyAsync(match => match.Status == "voided", cancellationToken))
        {
            return new ResolveBarcodeResult("forbidden", "label-voided", [], 0);
        }

        var distinctMatches = matches
            .GroupBy(match => new { match.SourceDocumentType, match.SourceDocumentId })
            .Select(group => new
            {
                group.Key.SourceDocumentType,
                group.Key.SourceDocumentId,
                ObservedAtUtc = group.Max(match => match.CreatedAtUtc),
            });
        var total = await distinctMatches.CountAsync(cancellationToken);
        var candidateRows = total == 1
            ? await distinctMatches.ToArrayAsync(cancellationToken)
            : await distinctMatches
                .OrderBy(candidate => candidate.SourceDocumentType)
                .ThenBy(candidate => candidate.SourceDocumentId)
                .Skip(page.Skip)
                .Take(page.Take)
                .ToArrayAsync(cancellationToken);
        var candidates = candidateRows
            .Select(candidate => new ResolvedBarcodeCandidate(
                candidate.SourceDocumentType,
                candidate.SourceDocumentId,
                "barcode-label",
                candidate.ObservedAtUtc))
            .ToArray();

        if (total == 1)
        {
            return new ResolveBarcodeResult("resolved", null, candidates, total);
        }

        if (total > 1)
        {
            return new ResolveBarcodeResult("ambiguous", "multiple-source-documents", candidates, total);
        }

        var activeRules = await dbContext.BarcodeRules
            .Where(rule => rule.OrganizationId == tenant.OrganizationId
                && rule.EnvironmentId == tenant.EnvironmentId
                && rule.Status == BarcodeRule.ActiveStatus)
            .Select(rule => new { rule.BarcodeType, rule.Prefix, rule.Length })
            .ToArrayAsync(cancellationToken);
        var managedFormat = activeRules.Any(rule => MatchesRuleFormat(
            request.ScannedValue,
            rule.BarcodeType,
            rule.Prefix,
            rule.Length));

        return managedFormat
            ? new ResolveBarcodeResult("unknown", "managed-label-not-found", [], 0)
            : new ResolveBarcodeResult("unsupported", "barcode-format-unsupported", [], 0);
    }

    private static bool MatchesRuleFormat(
        string scannedValue,
        string barcodeType,
        string prefix,
        int maximumLength)
    {
        if (scannedValue.Length > maximumLength)
        {
            return false;
        }

        if (!barcodeType.StartsWith("gs1-", StringComparison.Ordinal))
        {
            return scannedValue.StartsWith(prefix, StringComparison.Ordinal);
        }

        try
        {
            var parsed = Gs1ApplicationIdentifierParser.Parse(scannedValue);
            return parsed.Gtin.StartsWith(prefix, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
