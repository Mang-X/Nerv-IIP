using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

public sealed record ListWorkCenterMachineOverheadRatesQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    string AccountingPeriodCode) : IQuery<ListWorkCenterMachineOverheadRatesResponse>;

public sealed class ListWorkCenterMachineOverheadRatesQueryValidator
    : AbstractValidator<ListWorkCenterMachineOverheadRatesQuery>
{
    public ListWorkCenterMachineOverheadRatesQueryValidator()
    {
        RuleFor(x => x.OrganizationId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.AccountingPeriodCode).Must(BeNonBlank).MaximumLength(50);
    }

    private static bool BeNonBlank(string value) => !string.IsNullOrWhiteSpace(value);
}

public sealed record ListWorkCenterMachineOverheadRatesResponse(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    string AccountingPeriodCode,
    int? CurrentRevision,
    IReadOnlyList<WorkCenterMachineOverheadRateListItem> Items);

public sealed record WorkCenterMachineOverheadRateListItem(
    string WorkCenterMachineOverheadRateId,
    string AccountingPeriodCode,
    string Applicability,
    decimal FixedOverheadBudget,
    decimal VariableOverheadBudget,
    decimal NormalCapacityMachineHours,
    decimal FixedHourlyRate,
    decimal VariableHourlyRate,
    decimal TotalHourlyRate,
    string CurrencyCode,
    int Revision,
    string ChangedBy,
    string Reason,
    DateTimeOffset ChangedAtUtc);

public sealed class ListWorkCenterMachineOverheadRatesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListWorkCenterMachineOverheadRatesQuery, ListWorkCenterMachineOverheadRatesResponse>
{
    public async Task<ListWorkCenterMachineOverheadRatesResponse> Handle(
        ListWorkCenterMachineOverheadRatesQuery request,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var workCenterId = request.WorkCenterId.Trim();
        var accountingPeriodCode = request.AccountingPeriodCode.Trim();
        var items = await dbContext.WorkCenterMachineOverheadRates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId
                && x.AccountingPeriodCode == accountingPeriodCode)
            .OrderByDescending(x => x.Revision)
            .Select(x => new WorkCenterMachineOverheadRateListItem(
                x.Id.ToString(),
                x.AccountingPeriodCode,
                x.Applicability.ToString(),
                x.FixedOverheadBudget,
                x.VariableOverheadBudget,
                x.NormalCapacityMachineHours,
                x.FixedHourlyRate,
                x.VariableHourlyRate,
                x.TotalHourlyRate,
                x.CurrencyCode,
                x.Revision,
                x.ChangedBy,
                x.Reason,
                x.ChangedAtUtc))
            .ToListAsync(cancellationToken);

        return new ListWorkCenterMachineOverheadRatesResponse(
            organizationId,
            environmentId,
            workCenterId,
            accountingPeriodCode,
            items.Count == 0 ? null : items[0].Revision,
            items);
    }
}

public sealed record ResolveWorkCenterMachineOverheadRateForSettlementQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    DateTimeOffset CompletedAtUtc) : IQuery<ResolvedWorkCenterMachineOverheadRate>;

public sealed class ResolveWorkCenterMachineOverheadRateForSettlementQueryValidator
    : AbstractValidator<ResolveWorkCenterMachineOverheadRateForSettlementQuery>
{
    public ResolveWorkCenterMachineOverheadRateForSettlementQueryValidator()
    {
        RuleFor(x => x.OrganizationId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.CompletedAtUtc).NotEmpty().Must(value => value.Offset == TimeSpan.Zero);
    }

    private static bool BeNonBlank(string value) => !string.IsNullOrWhiteSpace(value);
}

public sealed record ResolvedWorkCenterMachineOverheadRate(
    string WorkCenterMachineOverheadRateId,
    string AccountingPeriodCode,
    string Applicability,
    decimal FixedHourlyRate,
    decimal VariableHourlyRate,
    decimal TotalHourlyRate,
    string CurrencyCode,
    int Revision);

public sealed class ResolveWorkCenterMachineOverheadRateForSettlementQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ResolveWorkCenterMachineOverheadRateForSettlementQuery, ResolvedWorkCenterMachineOverheadRate>
{
    public async Task<ResolvedWorkCenterMachineOverheadRate> Handle(
        ResolveWorkCenterMachineOverheadRateForSettlementQuery request,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var workCenterId = request.WorkCenterId.Trim();
        var completionDate = DateOnly.FromDateTime(request.CompletedAtUtc.UtcDateTime);
        var periodCodes = await dbContext.AccountingPeriods
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.StartDate <= completionDate
                && x.EndDate >= completionDate)
            .Select(x => x.PeriodCode)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (periodCodes.Count != 1)
        {
            throw new KnownException(
                $"结算完成时点『{request.CompletedAtUtc:O}』未唯一匹配会计期间『{organizationId}·{environmentId}』。");
        }

        var periodCode = periodCodes[0];
        var resolved = await dbContext.WorkCenterMachineOverheadRates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId
                && x.AccountingPeriodCode == periodCode)
            .OrderByDescending(x => x.Revision)
            .Select(x => new ResolvedWorkCenterMachineOverheadRate(
                x.Id.ToString(),
                x.AccountingPeriodCode,
                x.Applicability.ToString(),
                x.FixedHourlyRate,
                x.VariableHourlyRate,
                x.TotalHourlyRate,
                x.CurrencyCode,
                x.Revision))
            .FirstOrDefaultAsync(cancellationToken);

        return resolved ?? throw new KnownException(
            $"工作中心『{organizationId}·{environmentId}·{workCenterId}』在会计期间『{periodCode}』缺少适用或明确不适用的机器制造费用率。");
    }
}
