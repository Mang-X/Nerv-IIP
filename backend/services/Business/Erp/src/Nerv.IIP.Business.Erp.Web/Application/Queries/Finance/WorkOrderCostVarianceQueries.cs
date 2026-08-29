using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

public sealed record GetWorkOrderCostVarianceQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    int PageNumber = 1,
    int PageSize = 50) : IQuery<WorkOrderCostVarianceResponse>;

public sealed class GetWorkOrderCostVarianceQueryValidator : AbstractValidator<GetWorkOrderCostVarianceQuery>
{
    public GetWorkOrderCostVarianceQueryValidator()
    {
        RuleFor(x => x.OrganizationId).Must(NotBlank).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(NotBlank).MaximumLength(100);
        RuleFor(x => x.WorkOrderId).Must(NotBlank).MaximumLength(100);
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }

    private static bool NotBlank(string value) => !string.IsNullOrWhiteSpace(value);
}

public sealed record WorkOrderCostVarianceResponse(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string? CurrencyCode,
    string LaborCostBasis,
    string LaborVarianceStatus,
    string? UnavailableReason,
    decimal? ActualLaborHours,
    decimal? ActualLaborCost,
    decimal? StandardLaborHours,
    decimal? StandardLaborCost,
    decimal? LaborEfficiencyVarianceHours,
    decimal? LaborEfficiencyVarianceAmount,
    string? LaborEfficiencyVarianceDirection,
    string LaborRateVarianceStatus,
    string LaborRateVarianceReason,
    decimal? MaterialCost,
    decimal? TotalAccumulatedCost,
    decimal? CapitalizedCost,
    decimal? CapitalizationVarianceAmount,
    decimal? ActualMachineHours,
    string MachineCostStatus,
    string? MachineCostUnavailableReason,
    int PageNumber,
    int PageSize,
    int TotalOperations,
    IReadOnlyList<OperationLaborVarianceItem> Operations,
    decimal? AppliedFixedMachineOverhead,
    decimal? AppliedVariableMachineOverhead,
    decimal? AppliedMachineOverheadTotal,
    IReadOnlyList<OperationMachineOverheadItem> MachineOverheadOperations);

public sealed record OperationMachineOverheadItem(
    string OperationTaskId,
    string WorkCenterId,
    string SettlementId,
    long SettlementRevision,
    string Status,
    string? UnavailableReason,
    decimal? ActualMachineHours,
    decimal? AppliedFixedMachineOverhead,
    decimal? AppliedVariableMachineOverhead,
    decimal? AppliedMachineOverheadTotal,
    string AccountingPeriodCode,
    string CurrencyCode,
    string? DeviceAssetId,
    string? MachineTimeBasisCode,
    string WorkCenterMachineOverheadRateId,
    int RateRevision,
    DateTimeOffset CompletedAtUtc,
    string SourceEventId);

public sealed record OperationLaborVarianceItem(
    string OperationTaskId,
    string WorkCenterId,
    long SettlementRevision,
    string Status,
    string? UnavailableReason,
    long ActualLaborTicks,
    decimal ActualLaborHours,
    decimal ActualLaborCost,
    decimal? StandardLaborHours,
    decimal? StandardLaborCost,
    decimal? LaborEfficiencyVarianceHours,
    decimal? LaborEfficiencyVarianceAmount,
    string? LaborEfficiencyVarianceDirection,
    string CurrencyCode,
    string WorkCenterCostRateId,
    int RateRevision,
    decimal HourlyRate,
    string RateBasis,
    DateTimeOffset RateBasisAtUtc,
    IReadOnlyList<CoveredLaborReportItem> CoveredReports);

public sealed record CoveredLaborReportItem(
    string ReportNo,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    string UomCode,
    decimal? TheoreticalRatePerHour,
    DateTimeOffset ReportedAtUtc,
    bool IsReversal,
    string? ReversedReportNo);

public sealed class GetWorkOrderCostVarianceQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetWorkOrderCostVarianceQuery, WorkOrderCostVarianceResponse>
{
    private const string MachineOverheadNotApplicableReason = "machine_overhead_not_applicable";
    private const string NumericOverflowReason = "numeric_overflow";
    private const string WorkOrderNotCompletedReason = "work_order_not_completed";

    public async Task<WorkOrderCostVarianceResponse> Handle(
        GetWorkOrderCostVarianceQuery request,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var workOrderId = request.WorkOrderId.Trim();
        var cost = await dbContext.WorkOrderCosts
            .AsNoTracking()
            .Include(x => x.Details)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkOrderId == workOrderId, cancellationToken);

        if (cost is null)
            return Unavailable(organizationId, environmentId, workOrderId, request, "work_order_cost_not_found");

        var settlements = await dbContext.OperationLaborSettlements.AsNoTracking()
            .Where(settlement => settlement.OrganizationId == organizationId
                && settlement.EnvironmentId == environmentId
                && settlement.WorkOrderId == workOrderId
                && dbContext.OperationLaborSettlementStates.Any(state =>
                    state.OrganizationId == settlement.OrganizationId
                    && state.EnvironmentId == settlement.EnvironmentId
                    && state.OperationTaskId == settlement.OperationTaskId
                    && state.ActiveRevision == settlement.SettlementRevision))
            .OrderBy(x => x.OperationTaskId)
            .ThenBy(x => x.SettlementRevision)
            .ToListAsync(cancellationToken);
        var covered = await dbContext.OperationLaborCoveredReports.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkOrderId == workOrderId)
            .ToListAsync(cancellationToken);
        var snapshots = await dbContext.OperationLaborReportSnapshots.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkOrderId == workOrderId)
            .ToDictionaryAsync(x => x.ReportNo, StringComparer.Ordinal, cancellationToken);

        var completionReason = cost.CompletedAtUtc.HasValue ? null : WorkOrderNotCompletedReason;
        var operationResults = settlements
            .Select(settlement => BuildOperation(settlement, covered, snapshots, completionReason))
            .ToArray();
        var page = operationResults
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToArray();
        var currencies = settlements.Select(x => x.CurrencyCode).Distinct(StringComparer.Ordinal).ToArray();
        var unavailableReason = completionReason ?? (settlements.Count == 0
            ? "operation_not_settled"
            : operationResults.FirstOrDefault(x => x.Status == "unavailable")?.UnavailableReason);
        if (unavailableReason is null && currencies.Length > 1)
            unavailableReason = "currency_conflict";

        var hasNumericOverflow = false;
        decimal? actualLaborHours = settlements.Count == 0
            ? null
            : TryCalculate(() => settlements.Sum(x => x.ActualLaborHours), ref hasNumericOverflow);
        decimal? actualLaborCost = settlements.Count == 0
            ? null
            : TryCalculate(() => settlements.Sum(x => x.Amount), ref hasNumericOverflow);
        decimal? standardLaborHours = unavailableReason is null
            ? TryCalculate(() => operationResults.Sum(x => x.StandardLaborHours!.Value), ref hasNumericOverflow)
            : null;
        decimal? standardLaborCost = unavailableReason is null
            ? TryCalculate(() => operationResults.Sum(x => x.StandardLaborCost!.Value), ref hasNumericOverflow)
            : null;
        decimal? varianceHours = unavailableReason is null && actualLaborHours.HasValue && standardLaborHours.HasValue
            ? TryCalculate(() => actualLaborHours.Value - standardLaborHours.Value, ref hasNumericOverflow)
            : null;
        decimal? varianceAmount = unavailableReason is null && actualLaborCost.HasValue && standardLaborCost.HasValue
            ? TryCalculate(() => actualLaborCost.Value - standardLaborCost.Value, ref hasNumericOverflow)
            : null;
        var materialCost = TryCalculate(() => cost.MaterialCost, ref hasNumericOverflow);
        var totalAccumulatedCost = TryCalculate(() => cost.TotalAccumulatedCost, ref hasNumericOverflow);
        var capitalizedCost = TryCalculate(() => cost.CapitalizedCost, ref hasNumericOverflow);
        decimal? capitalizationVarianceAmount = completionReason is null
            && totalAccumulatedCost.HasValue
            && capitalizedCost.HasValue
                ? TryCalculate(() => totalAccumulatedCost.Value - capitalizedCost.Value, ref hasNumericOverflow)
                : null;
        var machineSettlements = await dbContext.OperationMachineOverheadSettlements.AsNoTracking()
            .Where(settlement => settlement.OrganizationId == organizationId
                && settlement.EnvironmentId == environmentId
                && settlement.WorkOrderId == workOrderId
                && dbContext.OperationMachineOverheadSettlementStates.Any(state =>
                    state.OrganizationId == settlement.OrganizationId
                    && state.EnvironmentId == settlement.EnvironmentId
                    && state.OperationTaskId == settlement.OperationTaskId
                    && state.ActiveRevision == settlement.SettlementRevision))
            .OrderBy(x => x.OperationTaskId)
            .ThenBy(x => x.SettlementRevision)
            .ToListAsync(cancellationToken);
        var machineOperations = machineSettlements.Select(BuildMachineOperation).ToArray();
        var applicableMachineSettlements = machineSettlements
            .Where(x => x.Applicability == MachineOverheadApplicability.Applicable)
            .ToArray();
        var machineCurrencies = applicableMachineSettlements
            .Select(x => x.CurrencyCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        decimal? machineHours = null;
        decimal? appliedFixedMachineOverhead = null;
        decimal? appliedVariableMachineOverhead = null;
        decimal? appliedMachineOverheadTotal = null;
        string machineCostStatus;
        string? machineCostUnavailableReason;
        try
        {
            if (machineSettlements.Count == 0)
            {
                machineCostStatus = "unavailable";
                machineCostUnavailableReason = "operation_not_settled";
            }
            else if (applicableMachineSettlements.Length == 0)
            {
                machineCostStatus = "notApplicable";
                machineCostUnavailableReason = MachineOverheadNotApplicableReason;
            }
            else if (machineCurrencies.Length != 1)
            {
                machineCostStatus = "unavailable";
                machineCostUnavailableReason = "currency_conflict";
            }
            else
            {
                machineHours = Round(applicableMachineSettlements.Sum(x => x.ActualMachineHours!.Value));
                appliedFixedMachineOverhead = Round(applicableMachineSettlements.Sum(x => x.FixedAmount));
                appliedVariableMachineOverhead = Round(applicableMachineSettlements.Sum(x => x.VariableAmount));
                appliedMachineOverheadTotal = Round(applicableMachineSettlements.Sum(x => x.Amount));
                machineCostStatus = "available";
                machineCostUnavailableReason = null;
            }
        }
        catch (OverflowException)
        {
            machineHours = null;
            appliedFixedMachineOverhead = null;
            appliedVariableMachineOverhead = null;
            appliedMachineOverheadTotal = null;
            machineCostStatus = "unavailable";
            machineCostUnavailableReason = NumericOverflowReason;
        }

        if (hasNumericOverflow)
            unavailableReason = NumericOverflowReason;

        return new(
            organizationId,
            environmentId,
            workOrderId,
            currencies.Length == 1 ? currencies[0] : null,
            "actualOperation",
            unavailableReason is null ? "available" : "unavailable",
            unavailableReason,
            actualLaborHours,
            actualLaborCost,
            standardLaborHours,
            standardLaborCost,
            varianceHours,
            varianceAmount,
            varianceAmount is null ? null : Direction(varianceAmount.Value),
            "notApplicable",
            "actual_payroll_rate_not_modeled",
            materialCost,
            totalAccumulatedCost,
            capitalizedCost,
            capitalizationVarianceAmount,
            machineHours,
            machineCostStatus,
            machineCostUnavailableReason,
            request.PageNumber,
            request.PageSize,
            operationResults.Length,
            page,
            appliedFixedMachineOverhead,
            appliedVariableMachineOverhead,
            appliedMachineOverheadTotal,
            machineOperations);
    }

    private static OperationLaborVarianceItem BuildOperation(
        OperationLaborSettlement settlement,
        IReadOnlyCollection<OperationLaborCoveredReport> covered,
        IReadOnlyDictionary<string, OperationLaborReportSnapshot> snapshots,
        string? finalityReason)
    {
        var reportNos = covered
            .Where(x => x.OperationTaskId == settlement.OperationTaskId
                && x.SettlementRevision == settlement.SettlementRevision)
            .Select(x => x.ReportNo)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var reports = reportNos
            .Where(snapshots.ContainsKey)
            .Select(reportNo => snapshots[reportNo])
            .ToArray();
        var reason = finalityReason ?? ValidateBasis(settlement, reportNos, reports, snapshots);
        decimal? standardHours = null;
        decimal? standardCost = null;
        decimal? varianceHours = null;
        decimal? varianceAmount = null;
        if (reason is null)
        {
            try
            {
                var rate = reports[0].TheoreticalRatePerHour!.Value;
                var netGood = reports.Sum(x => x.IsReversal
                    ? -Math.Abs(snapshots[x.ReversedReportNo!].GoodQuantity)
                    : x.GoodQuantity);
                if (netGood < 0m)
                {
                    reason = "negative_net_good_quantity";
                }
                else
                {
                    var unroundedStandardHours = netGood / rate;
                    standardHours = Round(unroundedStandardHours);
                    standardCost = Round(unroundedStandardHours * settlement.HourlyRate);
                    varianceHours = Round(settlement.ActualLaborHours - unroundedStandardHours);
                    varianceAmount = Round(settlement.Amount - standardCost.Value);
                }
            }
            catch (OverflowException)
            {
                reason = NumericOverflowReason;
            }
        }

        return new(
            settlement.OperationTaskId,
            settlement.WorkCenterId,
            settlement.SettlementRevision,
            reason is null ? "available" : "unavailable",
            reason,
            settlement.ActualLaborTicks,
            Round(settlement.ActualLaborHours),
            Round(settlement.Amount),
            standardHours,
            standardCost,
            varianceHours,
            varianceAmount,
            varianceAmount is null ? null : Direction(varianceAmount.Value),
            settlement.CurrencyCode,
            settlement.WorkCenterCostRateId.ToString(),
            settlement.RateRevision,
            settlement.HourlyRate,
            settlement.RateBasis,
            settlement.RateBasisAtUtc,
            reports.Select(x => new CoveredLaborReportItem(
                x.ReportNo, x.GoodQuantity, x.ScrapQuantity, x.ReworkQuantity,
                x.UomCode, x.TheoreticalRatePerHour, x.ReportedAtUtc,
                x.IsReversal, x.ReversedReportNo)).ToArray());
    }

    private static string? ValidateBasis(
        OperationLaborSettlement settlement,
        IReadOnlyCollection<string> reportNos,
        IReadOnlyCollection<OperationLaborReportSnapshot> reports,
        IReadOnlyDictionary<string, OperationLaborReportSnapshot> snapshots)
    {
        if (reportNos.Count == 0) return "missing_output_basis";
        if (reports.Count != reportNos.Count) return "missing_report_snapshot";
        if (reports.Any(x => !x.HasValidNumericScale)) return "numeric_scale_out_of_range";
        if (reports.Any(x => x.OperationTaskId != settlement.OperationTaskId
                || x.WorkCenterId != settlement.WorkCenterId))
            return "report_scope_conflict";
        if (reports.Any(x => x.TheoreticalRatePerHour is null or <= 0m))
            return "invalid_theoretical_rate";
        if (reports.Select(x => x.TheoreticalRatePerHour).Distinct().Skip(1).Any())
            return "conflicting_theoretical_rate";
        if (reports.Select(x => x.UomCode).Distinct(StringComparer.Ordinal).Skip(1).Any())
            return "conflicting_uom";
        if (reports.Any(x => !x.IsReversal && (x.GoodQuantity < 0m || x.ScrapQuantity < 0m || x.ReworkQuantity < 0m)))
            return "invalid_report_quantity";
        foreach (var reversal in reports.Where(x => x.IsReversal))
        {
            if (reversal.ReversedReportNo is null || !snapshots.TryGetValue(reversal.ReversedReportNo, out var original))
                return "missing_reversed_report_snapshot";
            if (original.IsReversal
                || !reportNos.Contains(original.ReportNo, StringComparer.Ordinal)
                || original.OrganizationId != settlement.OrganizationId
                || original.EnvironmentId != settlement.EnvironmentId
                || original.WorkOrderId != settlement.WorkOrderId
                || original.OperationTaskId != settlement.OperationTaskId
                || original.WorkCenterId != settlement.WorkCenterId
                || original.TheoreticalRatePerHour != reversal.TheoreticalRatePerHour
                || !string.Equals(original.UomCode, reversal.UomCode, StringComparison.Ordinal))
                return "conflicting_reversal_snapshot";
        }
        return null;
    }

    private static OperationMachineOverheadItem BuildMachineOperation(
        OperationMachineOverheadSettlement settlement)
    {
        var isApplicable = settlement.Applicability == MachineOverheadApplicability.Applicable;
        return new(
            settlement.OperationTaskId,
            settlement.WorkCenterId,
            settlement.Id.ToString(),
            settlement.SettlementRevision,
            isApplicable ? "available" : "notApplicable",
            isApplicable ? null : MachineOverheadNotApplicableReason,
            isApplicable ? Round(settlement.ActualMachineHours!.Value) : null,
            isApplicable ? Round(settlement.FixedAmount) : null,
            isApplicable ? Round(settlement.VariableAmount) : null,
            isApplicable ? Round(settlement.Amount) : null,
            settlement.AccountingPeriodCode,
            settlement.CurrencyCode,
            settlement.DeviceAssetId,
            settlement.MachineTimeBasisCode,
            settlement.WorkCenterMachineOverheadRateId.ToString(),
            settlement.RateRevision,
            settlement.CompletedAtUtc,
            settlement.SourceEventId);
    }

    private static WorkOrderCostVarianceResponse Unavailable(
        string organizationId,
        string environmentId,
        string workOrderId,
        GetWorkOrderCostVarianceQuery request,
        string reason)
        => new(
            organizationId, environmentId, workOrderId, null, "actualOperation",
            "unavailable", reason, null, null, null, null, null, null, null,
            "notApplicable", "actual_payroll_rate_not_modeled",
            null, null, null, null, null, "unavailable", "operation_not_settled",
            request.PageNumber, request.PageSize, 0, [], null, null, null, []);

    private static decimal? TryCalculate(Func<decimal> calculation, ref bool hasNumericOverflow)
    {
        try
        {
            return Round(calculation());
        }
        catch (OverflowException)
        {
            hasNumericOverflow = true;
            return null;
        }
    }

    private static decimal Round(decimal value)
        => decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static string Direction(decimal value)
        => value > 0m ? "unfavorable" : value < 0m ? "favorable" : "neutral";
}
