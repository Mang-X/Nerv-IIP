using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

public sealed record ListWorkCenterMachineOverheadReconciliationsQuery(
    string OrganizationId,
    string EnvironmentId,
    string AccountingPeriodCode,
    string? WorkCenterId = null) : IRequest<IReadOnlyList<WorkCenterMachineOverheadReconciliationItem>>;

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
    string RecordedBy,
    string SourceReference,
    string Reason,
    DateTimeOffset RecordedAtUtc);

public sealed class ListWorkCenterMachineOverheadReconciliationsQueryHandler(ApplicationDbContext dbContext)
    : IRequestHandler<ListWorkCenterMachineOverheadReconciliationsQuery, IReadOnlyList<WorkCenterMachineOverheadReconciliationItem>>
{
    public async Task<IReadOnlyList<WorkCenterMachineOverheadReconciliationItem>> Handle(
        ListWorkCenterMachineOverheadReconciliationsQuery request,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var periodCode = request.AccountingPeriodCode.Trim();
        var workCenterId = request.WorkCenterId?.Trim();
        var query = dbContext.WorkCenterMachineOverheadReconciliations.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.AccountingPeriodCode == periodCode);
        if (!string.IsNullOrEmpty(workCenterId)) query = query.Where(x => x.WorkCenterId == workCenterId);

        return await query
            .OrderBy(x => x.WorkCenterId)
            .ThenByDescending(x => x.Revision)
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
                x.RecordedBy, x.SourceReference, x.Reason, x.RecordedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
