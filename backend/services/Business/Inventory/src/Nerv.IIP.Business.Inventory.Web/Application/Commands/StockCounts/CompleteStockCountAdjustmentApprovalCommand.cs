using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Contracts.Approval;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;

public sealed record CompleteStockCountAdjustmentApprovalCommand(
    string OrganizationId,
    string EnvironmentId,
    string CountTaskCode,
    string ApprovalChainId,
    string ApprovalResult,
    string? AdjustmentKey = null) : ICommand<CompleteStockCountAdjustmentApprovalResult>;

public sealed record CompleteStockCountAdjustmentApprovalResult(bool Applied);

public sealed class CompleteStockCountAdjustmentApprovalCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CompleteStockCountAdjustmentApprovalCommand, CompleteStockCountAdjustmentApprovalResult>
{
    public async Task<CompleteStockCountAdjustmentApprovalResult> Handle(
        CompleteStockCountAdjustmentApprovalCommand request,
        CancellationToken cancellationToken)
    {
        var adjustment = await dbContext.StockCountAdjustments.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.CountTaskCode == request.CountTaskCode
            && x.ApprovalChainId == request.ApprovalChainId
            && (request.AdjustmentKey == null || x.IdempotencyKey == request.AdjustmentKey),
            cancellationToken);
        if (adjustment is null && request.AdjustmentKey is not null)
        {
            // The approval event can arrive before Inventory commits the adjustment.
            // Let CAP retry the delivery after the transaction becomes visible.
            throw new InvalidOperationException($"盘点调整尚未登记，审批链 ID = {request.ApprovalChainId}");
        }

        if (adjustment?.Status != StockCountAdjustmentStatuses.PendingApproval)
        {
            return new CompleteStockCountAdjustmentApprovalResult(false);
        }

        var task = await dbContext.StockCountTasks.SingleAsync(x =>
            x.OrganizationId == adjustment.OrganizationId
            && x.EnvironmentId == adjustment.EnvironmentId
            && x.CountTaskCode == adjustment.CountTaskCode,
            cancellationToken);
        var ledger = await dbContext.StockLedgers.SingleAsync(x =>
            x.OrganizationId == task.LedgerOrganizationId
            && x.EnvironmentId == task.LedgerEnvironmentId
            && x.SkuCode == task.SkuCode
            && x.UomCode == task.UomCode
            && x.SiteCode == task.SiteCode
            && x.LocationCode == task.LocationCode
            && x.LotNo == task.LotNo
            && x.SerialNo == task.SerialNo
            && x.QualityStatus == task.QualityStatus
            && x.OwnerType == task.OwnerType
            && x.OwnerId == task.OwnerId,
            cancellationToken);

        if (string.Equals(request.ApprovalResult, ApprovalResults.Approved, StringComparison.OrdinalIgnoreCase))
        {
            ApplyOutcome(dbContext, adjustment, task, ledger, approved: true);
            return new CompleteStockCountAdjustmentApprovalResult(true);
        }

        if (string.Equals(request.ApprovalResult, ApprovalResults.Rejected, StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.ApprovalResult, ApprovalResults.Returned, StringComparison.OrdinalIgnoreCase))
        {
            ApplyOutcome(dbContext, adjustment, task, ledger, approved: false);
            return new CompleteStockCountAdjustmentApprovalResult(true);
        }

        return new CompleteStockCountAdjustmentApprovalResult(false);
    }

    internal static StockMovement? ApplyOutcome(
        ApplicationDbContext dbContext, StockCountAdjustment adjustment, StockCountTask task, StockLedger ledger, bool approved)
    {
        try
        {
            if (approved)
            {
                var movement = task.ConfirmApprovedAdjustment(ledger, adjustment.IdempotencyKey);
                dbContext.StockMovements.Add(movement);
                adjustment.MarkPosted(movement, task.IdempotencyKey);
                return movement;
            }

            task.RequireRecountAfterApprovalRejection(ledger);
            adjustment.VoidAfterApprovalRejection(task.IdempotencyKey);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
}
