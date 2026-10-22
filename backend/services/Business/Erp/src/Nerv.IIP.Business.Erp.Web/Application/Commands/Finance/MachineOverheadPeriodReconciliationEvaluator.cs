using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

internal sealed record MachineOverheadPeriodReconciliationScope(
    IReadOnlyDictionary<string, WorkCenterMachineOverheadRate> LatestRates,
    IReadOnlyDictionary<string, MachineOverheadAppliedSnapshot> AppliedSnapshots,
    IReadOnlyList<string> RequiredWorkCenterIds);

internal sealed record MachineOverheadReconciliationIssue(string WorkCenterId, string ReasonCode);

internal sealed record MachineOverheadPeriodReconciliationEvaluation(
    IReadOnlyDictionary<string, string> LatestReconciliationIds,
    IReadOnlyDictionary<string, MachineOverheadReconciliationIssue> Issues)
{
    public MachineOverheadReconciliationIssue? FirstIssue(IReadOnlyList<string> requiredWorkCenterIds)
        => requiredWorkCenterIds
            .Select(workCenterId => Issues.GetValueOrDefault(workCenterId))
            .FirstOrDefault(issue => issue is not null);
}

internal static class MachineOverheadPeriodReconciliationEvaluator
{
    public static async Task<MachineOverheadPeriodReconciliationScope> ReadScopeAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string accountingPeriodCode,
        string? workCenterId,
        CancellationToken cancellationToken)
    {
        var latestRatesQuery = dbContext.WorkCenterMachineOverheadRates.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.AccountingPeriodCode == accountingPeriodCode
                && !dbContext.WorkCenterMachineOverheadRates.Any(newer =>
                    newer.OrganizationId == x.OrganizationId
                    && newer.EnvironmentId == x.EnvironmentId
                    && newer.AccountingPeriodCode == x.AccountingPeriodCode
                    && newer.WorkCenterId == x.WorkCenterId
                    && newer.Revision > x.Revision));
        if (workCenterId is not null)
            latestRatesQuery = latestRatesQuery.Where(x => x.WorkCenterId == workCenterId);

        var latestRates = await latestRatesQuery
            .OrderBy(x => x.WorkCenterId)
            .ToListAsync(cancellationToken);
        var latestRateByWorkCenter = latestRates.ToDictionary(x => x.WorkCenterId, StringComparer.Ordinal);
        IReadOnlyDictionary<string, MachineOverheadAppliedSnapshot> appliedByWorkCenter = workCenterId is null
            ? await MachineOverheadAppliedSnapshotReader.ReadForPeriodAsync(
                dbContext, organizationId, environmentId, accountingPeriodCode, cancellationToken)
            : await MachineOverheadAppliedSnapshotReader.ReadForWorkCentersAsync(
                dbContext, organizationId, environmentId, accountingPeriodCode, [workCenterId], cancellationToken);
        var requiredWorkCenterIds = latestRates
            .Where(x => x.Applicability == MachineOverheadApplicability.Applicable)
            .Select(x => x.WorkCenterId)
            .Concat(appliedByWorkCenter.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new(latestRateByWorkCenter, appliedByWorkCenter, requiredWorkCenterIds);
    }

    public static async Task<MachineOverheadPeriodReconciliationEvaluation> EvaluateAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string accountingPeriodCode,
        MachineOverheadPeriodReconciliationScope scope,
        CancellationToken cancellationToken)
    {
        var latestReconciliations = await dbContext.WorkCenterMachineOverheadReconciliations.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.AccountingPeriodCode == accountingPeriodCode
                && scope.RequiredWorkCenterIds.Contains(x.WorkCenterId)
                && !dbContext.WorkCenterMachineOverheadReconciliations.Any(newer =>
                    newer.OrganizationId == x.OrganizationId
                    && newer.EnvironmentId == x.EnvironmentId
                    && newer.AccountingPeriodCode == x.AccountingPeriodCode
                    && newer.WorkCenterId == x.WorkCenterId
                    && newer.Revision > x.Revision))
            .ToListAsync(cancellationToken);
        var reconciliationByWorkCenter = latestReconciliations
            .ToDictionary(x => x.WorkCenterId, StringComparer.Ordinal);
        var issues = new Dictionary<string, MachineOverheadReconciliationIssue>(StringComparer.Ordinal);

        foreach (var workCenterId in scope.RequiredWorkCenterIds)
        {
            string? reasonCode = null;
            if (!reconciliationByWorkCenter.TryGetValue(workCenterId, out var reconciliation))
            {
                reasonCode = "reconciliation_not_recorded";
            }
            else if (!scope.LatestRates.TryGetValue(workCenterId, out var rate))
            {
                reasonCode = "machine_overhead_rate_not_configured";
            }
            else if (rate.Applicability == MachineOverheadApplicability.Applicable
                && (reconciliation.WorkCenterMachineOverheadRateId != rate.Id
                    || reconciliation.RateRevision != rate.Revision))
            {
                reasonCode = "machine_overhead_rate_changed";
            }
            else if (rate.Applicability == MachineOverheadApplicability.Applicable
                && !string.Equals(reconciliation.CurrencyCode, rate.CurrencyCode, StringComparison.Ordinal))
            {
                reasonCode = "currency_conflict";
            }
            else if (!reconciliation.IsReadyForClose)
            {
                reasonCode = "abnormal_downtime_pending";
            }
            else
            {
                var current = scope.AppliedSnapshots.GetValueOrDefault(workCenterId)
                    ?? new MachineOverheadAppliedSnapshot(0, 0m, 0m, 0m, new HashSet<string>(StringComparer.Ordinal));
                var expectedCurrency = rate.Applicability == MachineOverheadApplicability.Applicable
                    ? rate.CurrencyCode
                    : reconciliation.CurrencyCode;
                if (current.CurrencyCodes.Any(currency => !string.Equals(currency, expectedCurrency, StringComparison.Ordinal)))
                {
                    reasonCode = "active_settlement_currency_conflict";
                }
                else if (reconciliation.AppliedMachineTicks != current.AppliedMachineTicks
                    || reconciliation.AppliedFixedAmount != current.AppliedFixedAmount
                    || reconciliation.AppliedVariableAmount != current.AppliedVariableAmount
                    || reconciliation.AppliedTotalAmount != current.AppliedTotalAmount)
                {
                    reasonCode = "active_settlement_changed";
                }
            }

            if (reasonCode is not null)
                issues.Add(workCenterId, new(workCenterId, reasonCode));
        }

        return new(
            reconciliationByWorkCenter.ToDictionary(
                pair => pair.Key, pair => pair.Value.Id.ToString(), StringComparer.Ordinal),
            issues);
    }
}
