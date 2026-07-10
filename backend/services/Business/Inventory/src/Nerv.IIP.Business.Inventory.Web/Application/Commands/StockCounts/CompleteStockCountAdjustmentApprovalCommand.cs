using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Contracts.Approval;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;

public sealed record CompleteStockCountAdjustmentApprovalCommand(
    string OrganizationId,
    string EnvironmentId,
    string CountTaskCode,
    string ApprovalChainId,
    string ApprovalResult) : ICommand<CompleteStockCountAdjustmentApprovalResult>;

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
            && x.ApprovalChainId == request.ApprovalChainId,
            cancellationToken);
        if (adjustment is null || adjustment.Status != StockCountAdjustmentStatuses.PendingApproval)
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

        try
        {
            if (string.Equals(request.ApprovalResult, ApprovalResults.Approved, StringComparison.OrdinalIgnoreCase))
            {
                var movement = task.ConfirmApprovedAdjustment(ledger, adjustment.IdempotencyKey);
                dbContext.StockMovements.Add(movement);
                adjustment.MarkPosted(movement);
                return new CompleteStockCountAdjustmentApprovalResult(true);
            }

            if (string.Equals(request.ApprovalResult, ApprovalResults.Rejected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.ApprovalResult, ApprovalResults.Returned, StringComparison.OrdinalIgnoreCase))
            {
                task.RequireRecountAfterApprovalRejection(ledger);
                adjustment.VoidAfterApprovalRejection();
                return new CompleteStockCountAdjustmentApprovalResult(true);
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }

        return new CompleteStockCountAdjustmentApprovalResult(false);
    }
}
