using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
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
    MachineOverheadApplicability Applicability,
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
                x.Applicability,
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
        var period = await MachineOverheadAccountingPeriods.MatchUniqueAsync(
                dbContext,
                organizationId,
                environmentId,
                DateOnly.FromDateTime(request.CompletedAtUtc.UtcDateTime),
                cancellationToken)
            ?? throw new KnownException(
                $"结算完成时点『{request.CompletedAtUtc:O}』未唯一匹配会计期间『{organizationId}·{environmentId}』。");
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

/// <summary>
/// 机器制造费用按日期取会计期间的唯一判定：期间必须唯一匹配，没有或重叠都视为无期间。
/// 结算与生产准备检查共用这一处，保证准备检查说「已就绪」时结算也取得到同一个期间。
/// </summary>
internal static class MachineOverheadAccountingPeriods
{
    public sealed record MatchedPeriod(string PeriodCode, AccountingPeriodStatus Status);

    public static async Task<MatchedPeriod?> MatchUniqueAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var periods = await dbContext.AccountingPeriods
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.StartDate <= date
                && x.EndDate >= date)
            .Select(x => new MatchedPeriod(x.PeriodCode, x.Status))
            .Take(2)
            .ToListAsync(cancellationToken);
        return periods.Count == 1 ? periods[0] : null;
    }
}

/// <summary>
/// 按日期返回当前会计期间，以及该期间已配置机器制造费用率（适用或明确不适用）的工作中心，供 MES 生产准备检查消费（#3795）。
/// 期间未唯一匹配时 <see cref="MachineOverheadRatePeriodCoverageResponse.AccountingPeriodCode"/> 为空。
/// </summary>
public sealed record GetMachineOverheadRatePeriodCoverageQuery(
    string OrganizationId,
    string EnvironmentId,
    DateOnly Date) : IQuery<MachineOverheadRatePeriodCoverageResponse>;

public sealed class GetMachineOverheadRatePeriodCoverageQueryValidator
    : AbstractValidator<GetMachineOverheadRatePeriodCoverageQuery>
{
    public GetMachineOverheadRatePeriodCoverageQueryValidator()
    {
        RuleFor(x => x.OrganizationId).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
        RuleFor(x => x.Date).NotEmpty();
    }
}

public sealed record MachineOverheadRatePeriodCoverageResponse(
    string? AccountingPeriodCode,
    IReadOnlyList<string> ConfiguredWorkCenterIds);

public sealed class GetMachineOverheadRatePeriodCoverageQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetMachineOverheadRatePeriodCoverageQuery, MachineOverheadRatePeriodCoverageResponse>
{
    public async Task<MachineOverheadRatePeriodCoverageResponse> Handle(
        GetMachineOverheadRatePeriodCoverageQuery request,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var period = await MachineOverheadAccountingPeriods.MatchUniqueAsync(
            dbContext, organizationId, environmentId, request.Date, cancellationToken);
        if (period is null)
            return new MachineOverheadRatePeriodCoverageResponse(null, []);

        var workCenterIds = await dbContext.WorkCenterMachineOverheadRates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.AccountingPeriodCode == period.PeriodCode)
            .Select(x => x.WorkCenterId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);
        return new MachineOverheadRatePeriodCoverageResponse(period.PeriodCode, workCenterIds);
    }
}
