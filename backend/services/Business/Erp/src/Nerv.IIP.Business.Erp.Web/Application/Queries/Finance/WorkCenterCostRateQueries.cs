using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

public sealed record ListWorkCenterCostRatesQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    DateTimeOffset? AtUtc = null) : IQuery<ListWorkCenterCostRatesResponse>;

public sealed class ListWorkCenterCostRatesQueryValidator : AbstractValidator<ListWorkCenterCostRatesQuery>
{
    public ListWorkCenterCostRatesQueryValidator()
    {
        RuleFor(x => x.OrganizationId).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
        RuleFor(x => x.AtUtc).Must(value => value is null || value.Value.Offset == TimeSpan.Zero);
    }
}

public sealed record ListWorkCenterCostRatesResponse(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    DateTimeOffset AtUtc,
    int? CurrentEffectiveRevision,
    IReadOnlyCollection<WorkCenterCostRateListItem> Items);

public sealed record WorkCenterCostRateListItem(
    string WorkCenterCostRateId,
    string WorkCenterId,
    decimal HourlyRate,
    string CurrencyCode,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    int Revision,
    string ChangedBy,
    string Reason,
    DateTimeOffset ChangedAtUtc,
    string EffectiveStatus,
    bool IsEffectiveAtUtc,
    bool IsCurrentEffectiveRevision);

public sealed class ListWorkCenterCostRatesQueryHandler(ApplicationDbContext dbContext, TimeProvider? timeProvider = null)
    : IQueryHandler<ListWorkCenterCostRatesQuery, ListWorkCenterCostRatesResponse>
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ListWorkCenterCostRatesResponse> Handle(ListWorkCenterCostRatesQuery request, CancellationToken cancellationToken)
    {
        var atUtc = request.AtUtc ?? clock.GetUtcNow();
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var workCenterId = request.WorkCenterId.Trim();
        var rates = await dbContext.WorkCenterCostRates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId)
            .OrderByDescending(x => x.Revision)
            .ToListAsync(cancellationToken);
        var currentRevision = rates
            .Where(x => x.EffectiveFromUtc <= atUtc && (x.EffectiveToUtc == null || atUtc < x.EffectiveToUtc))
            .Select(x => (int?)x.Revision)
            .FirstOrDefault();

        return new ListWorkCenterCostRatesResponse(
            organizationId,
            environmentId,
            workCenterId,
            atUtc,
            currentRevision,
            rates.Select(x => new WorkCenterCostRateListItem(
                x.Id.ToString(),
                x.WorkCenterId,
                x.HourlyRate,
                x.CurrencyCode,
                x.EffectiveFromUtc,
                x.EffectiveToUtc,
                x.Revision,
                x.ChangedBy,
                x.Reason,
                x.ChangedAtUtc,
                GetEffectiveStatus(x.EffectiveFromUtc, x.EffectiveToUtc, atUtc),
                x.EffectiveFromUtc <= atUtc && (x.EffectiveToUtc == null || atUtc < x.EffectiveToUtc),
                x.Revision == currentRevision)).ToArray());
    }

    private static string GetEffectiveStatus(DateTimeOffset effectiveFromUtc, DateTimeOffset? effectiveToUtc, DateTimeOffset atUtc)
        => atUtc < effectiveFromUtc ? "future" : effectiveToUtc is not null && atUtc >= effectiveToUtc ? "expired" : "effective";
}
