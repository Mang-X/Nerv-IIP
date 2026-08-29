using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

public sealed record ListWorkCenterMachineOverheadReconciliationsQuery(
    string OrganizationId,
    string EnvironmentId,
    string AccountingPeriodCode,
    string? WorkCenterId = null,
    int PageNumber = 1,
    int PageSize = 50) : IQuery<ListWorkCenterMachineOverheadReconciliationsResponse>;

public sealed class ListWorkCenterMachineOverheadReconciliationsQueryValidator
    : AbstractValidator<ListWorkCenterMachineOverheadReconciliationsQuery>
{
    public ListWorkCenterMachineOverheadReconciliationsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.AccountingPeriodCode).Must(BeNonBlank).MaximumLength(50);
        RuleFor(x => x.WorkCenterId).Must(BeNonBlank).MaximumLength(100).When(x => x.WorkCenterId is not null);
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }

    private static bool BeNonBlank(string? value) => !string.IsNullOrWhiteSpace(value);
}

public sealed record ListWorkCenterMachineOverheadReconciliationsResponse(
    string OrganizationId,
    string EnvironmentId,
    string AccountingPeriodCode,
    string? WorkCenterId,
    int PageNumber,
    int PageSize,
    int TotalCount,
    IReadOnlyList<WorkCenterMachineOverheadReconciliationItem> Items,
    string? AccountingPeriodStatus,
    string ReconciliationStatus,
    string? ReconciliationUnavailableReason);

public sealed record WorkCenterMachineOverheadReconciliationItem(
    string Id,
    string WorkCenterId,
    string AccountingPeriodCode,
    int Revision,
    int RateRevision,
    string CurrencyCode,
    decimal ActualFixedOverheadAmount,
    decimal ActualVariableOverheadAmount,
    decimal ActualTotalOverheadAmount,
    long AppliedMachineTicks,
    decimal AppliedMachineHours,
    decimal AppliedFixedAmount,
    decimal AppliedVariableAmount,
    decimal AppliedTotalAmount,
    decimal AppliedRoundingDifferenceAmount,
    decimal UnderOverAppliedFixedAmount,
    decimal UnderOverAppliedVariableAmount,
    decimal UnderOverAppliedTotalAmount,
    decimal UnallocatedFixedOverheadAmount,
    decimal OverAppliedFixedOverheadAmount,
    long AbnormalDowntimeTicks,
    decimal AbnormalDowntimeHours,
    string AbnormalDowntimeDisposition,
    bool IsReadyForClose,
    string ReconciliationStatus,
    string? UnavailableReason,
    string RecordedBy,
    string SourceReference,
    string Reason,
    DateTimeOffset RecordedAtUtc);

public sealed class ListWorkCenterMachineOverheadReconciliationsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListWorkCenterMachineOverheadReconciliationsQuery, ListWorkCenterMachineOverheadReconciliationsResponse>
{
    public async Task<ListWorkCenterMachineOverheadReconciliationsResponse> Handle(
        ListWorkCenterMachineOverheadReconciliationsQuery request,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var periodCode = request.AccountingPeriodCode.Trim();
        var workCenterId = request.WorkCenterId?.Trim();
        var period = await dbContext.AccountingPeriods.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.PeriodCode == periodCode, cancellationToken);
        var query = dbContext.WorkCenterMachineOverheadReconciliations.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.AccountingPeriodCode == periodCode);
        if (!string.IsNullOrEmpty(workCenterId)) query = query.Where(x => x.WorkCenterId == workCenterId);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.WorkCenterId)
            .ThenByDescending(x => x.Revision)
            .ThenBy(x => x.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new WorkCenterMachineOverheadReconciliationItem(
                x.Id.ToString(), x.WorkCenterId, x.AccountingPeriodCode, x.Revision, x.RateRevision,
                x.CurrencyCode, x.ActualFixedOverheadAmount, x.ActualVariableOverheadAmount,
                x.ActualTotalOverheadAmount, x.AppliedMachineTicks, x.AppliedMachineHours,
                x.AppliedFixedAmount, x.AppliedVariableAmount, x.AppliedTotalAmount,
                x.AppliedRoundingDifferenceAmount,
                x.UnderOverAppliedFixedAmount, x.UnderOverAppliedVariableAmount,
                x.UnderOverAppliedTotalAmount, x.UnallocatedFixedOverheadAmount,
                x.OverAppliedFixedOverheadAmount, x.AbnormalDowntimeTicks, x.AbnormalDowntimeHours,
                x.AbnormalDowntimeDisposition.ToString(),
                x.AbnormalDowntimeDisposition != Domain.AggregatesModel.MachineOverheadReconciliationAggregate.AbnormalDowntimeDisposition.Pending,
                x.AbnormalDowntimeDisposition == Domain.AggregatesModel.MachineOverheadReconciliationAggregate.AbnormalDowntimeDisposition.Pending
                    ? "unavailable"
                    : "available",
                x.AbnormalDowntimeDisposition == Domain.AggregatesModel.MachineOverheadReconciliationAggregate.AbnormalDowntimeDisposition.Pending
                    ? "abnormal_downtime_pending"
                    : null,
                x.RecordedBy, x.SourceReference, x.Reason, x.RecordedAtUtc))
            .ToListAsync(cancellationToken);

        var status = await ResolveReconciliationStatusAsync(
            organizationId, environmentId, periodCode, workCenterId, period, cancellationToken);
        var statusItems = items
            .Select(item => status.LatestReconciliationIds.GetValueOrDefault(item.WorkCenterId) == item.Id
                ? item with
                {
                    ReconciliationStatus = status.Issues.TryGetValue(item.WorkCenterId, out var issue)
                        ? "unavailable"
                        : "available",
                    UnavailableReason = status.Issues.GetValueOrDefault(item.WorkCenterId)?.ReasonCode,
                }
                : item with
                {
                    ReconciliationStatus = "unavailable",
                    UnavailableReason = "superseded_reconciliation",
                })
            .ToArray();

        return new(
            organizationId, environmentId, periodCode, workCenterId,
            request.PageNumber, request.PageSize, totalCount, statusItems,
            period?.Status == AccountingPeriodStatus.Closed ? "closed" : period is null ? null : "open",
            status.Status,
            status.UnavailableReason);
    }

    private async Task<ReconciliationReadStatus> ResolveReconciliationStatusAsync(
        string organizationId,
        string environmentId,
        string periodCode,
        string? workCenterId,
        AccountingPeriod? period,
        CancellationToken cancellationToken)
    {
        if (period is null)
            return ReconciliationReadStatus.Unavailable("accounting_period_not_found");

        var scope = await MachineOverheadPeriodReconciliationEvaluator.ReadScopeAsync(
            dbContext, organizationId, environmentId, periodCode, workCenterId, cancellationToken);
        if (scope.RequiredWorkCenterIds.Count == 0)
        {
            return scope.LatestRates.Count > 0
                && scope.LatestRates.Values.All(x => x.Applicability == MachineOverheadApplicability.NotApplicable)
                    ? ReconciliationReadStatus.NotApplicable()
                    : ReconciliationReadStatus.Unavailable("machine_overhead_rate_not_configured");
        }

        var evaluation = await MachineOverheadPeriodReconciliationEvaluator.EvaluateAsync(
            dbContext, organizationId, environmentId, periodCode, scope, cancellationToken);
        var issue = evaluation.FirstIssue(scope.RequiredWorkCenterIds);
        return new(
            issue is null ? "available" : "unavailable",
            issue?.ReasonCode,
            evaluation.LatestReconciliationIds,
            evaluation.Issues);
    }

    private sealed record ReconciliationReadStatus(
        string Status,
        string? UnavailableReason,
        IReadOnlyDictionary<string, string> LatestReconciliationIds,
        IReadOnlyDictionary<string, MachineOverheadReconciliationIssue> Issues)
    {
        public static ReconciliationReadStatus Unavailable(string reason)
            => new("unavailable", reason,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, MachineOverheadReconciliationIssue>(StringComparer.Ordinal));

        public static ReconciliationReadStatus NotApplicable()
            => new("notApplicable", "machine_overhead_not_applicable",
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, MachineOverheadReconciliationIssue>(StringComparer.Ordinal));
    }
}
