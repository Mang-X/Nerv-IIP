using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

public sealed record ListWorkCenterMachineOverheadRatesQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    string AccountingPeriodCode,
    int PageNumber = 1,
    int PageSize = 50) : IQuery<ListWorkCenterMachineOverheadRatesResponse>;

public sealed class ListWorkCenterMachineOverheadRatesQueryValidator
    : AbstractValidator<ListWorkCenterMachineOverheadRatesQuery>
{
    public ListWorkCenterMachineOverheadRatesQueryValidator()
    {
        RuleFor(x => x.OrganizationId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.AccountingPeriodCode).Must(BeNonBlank).MaximumLength(50);
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }

    private static bool BeNonBlank(string value) => !string.IsNullOrWhiteSpace(value);
}

public sealed record ListWorkCenterMachineOverheadRatesResponse(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    string AccountingPeriodCode,
    int? CurrentRevision,
    int PageNumber,
    int PageSize,
    int TotalCount,
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
        var scoped = dbContext.WorkCenterMachineOverheadRates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId
                && x.AccountingPeriodCode == accountingPeriodCode);
        var totalCount = await scoped.CountAsync(cancellationToken);
        var currentRevision = await scoped.Select(x => (int?)x.Revision).MaxAsync(cancellationToken);
        var items = await scoped
            .OrderByDescending(x => x.Revision)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
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
            currentRevision,
            request.PageNumber,
            request.PageSize,
            totalCount,
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

internal sealed class ClosedAccountingPeriodForMachineOverheadSettlementException(string periodCode)
    : Exception($"Accounting period '{periodCode}' is closed and cannot accept machine-overhead settlement.")
{
    public string PeriodCode { get; } = periodCode;
}

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
        var periods = await dbContext.AccountingPeriods
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.StartDate <= completionDate
                && x.EndDate >= completionDate)
            .Select(x => new { x.PeriodCode, x.Status })
            .Take(2)
            .ToListAsync(cancellationToken);
        if (periods.Count != 1)
        {
            throw new KnownException(
                $"结算完成时点『{request.CompletedAtUtc:O}』未唯一匹配会计期间『{organizationId}·{environmentId}』。");
        }

        var period = periods[0];
        if (period.Status != AccountingPeriodStatus.Open)
            throw new ClosedAccountingPeriodForMachineOverheadSettlementException(period.PeriodCode);

        var periodCode = period.PeriodCode;
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
