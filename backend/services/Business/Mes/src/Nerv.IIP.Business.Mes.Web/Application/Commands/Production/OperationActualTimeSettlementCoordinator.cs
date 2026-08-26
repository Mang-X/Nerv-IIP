using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

internal static class OperationActualTimeSettlementCoordinator
{
    internal static async Task CompleteAsync(
        ApplicationDbContext dbContext,
        OperationTask operationTask,
        DateTimeOffset completedAtUtc,
        IReadOnlyCollection<string> pendingProductionReportNos,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(operationTask);
        ArgumentNullException.ThrowIfNull(pendingProductionReportNos);

        var persistedReportNos = await dbContext.ProductionReports
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == operationTask.OrganizationId &&
                x.EnvironmentId == operationTask.EnvironmentId &&
                x.WorkOrderId == operationTask.WorkOrderId &&
                x.OperationTaskId == operationTask.OperationTaskIdValue)
            .Select(x => x.ReportNo)
            .ToArrayAsync(cancellationToken);
        var coveredReportNos = persistedReportNos
            .Concat(pendingProductionReportNos)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        operationTask.Complete(completedAtUtc, coveredReportNos);
        var snapshot = operationTask.GetDomainEvents()
            .OfType<OperationActualTimeSettledDomainEvent>()
            .Last(x => x.Settlement.SettlementRevision == operationTask.ActualTimeSettlementRevision)
            .Settlement;
        dbContext.OperationActualTimeSettlements.Add(OperationActualTimeSettlement.Capture(snapshot));
    }

    internal static async Task VoidAsync(
        ApplicationDbContext dbContext,
        OperationTask operationTask,
        DateTimeOffset voidedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(operationTask);
        if (operationTask.ActualTimeSettlementRevision <= 0)
        {
            throw new InvalidOperationException(
                "Completed operation task has no governed actual-time settlement and cannot be reopened by report reversal.");
        }

        var settlement = await dbContext.OperationActualTimeSettlements
            .Include(x => x.CoveredReports)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == operationTask.OrganizationId &&
                x.EnvironmentId == operationTask.EnvironmentId &&
                x.OperationTaskId == operationTask.OperationTaskIdValue &&
                x.Revision == operationTask.ActualTimeSettlementRevision &&
                x.VoidedAtUtc == null,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Completed operation task has no matching active actual-time settlement.");
        var snapshot = settlement.Snapshot();
        settlement.Void(voidedAtUtc);
        operationTask.ReopenAfterReportReversal(snapshot, voidedAtUtc);
    }
}
