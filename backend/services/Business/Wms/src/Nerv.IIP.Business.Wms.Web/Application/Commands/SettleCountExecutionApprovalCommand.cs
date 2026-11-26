using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Wms.Web.Application.Commands;

public sealed record SettleCountExecutionApprovalCommand(
    string OrganizationId, string EnvironmentId, StockCountApprovalSettledPayload Outcome) : ICommand<bool>;

public sealed class SettleCountExecutionApprovalCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<SettleCountExecutionApprovalCommand, bool>
{
    public async Task<bool> Handle(SettleCountExecutionApprovalCommand request, CancellationToken cancellationToken)
    {
        var outcome = request.Outcome;
        var count = await dbContext.CountExecutions.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId
            && x.CountNo == outcome.CountTaskCode, cancellationToken);
        if (count is null || WmsInventoryReservationIdempotencyKeys.ForCountExecution(count) != outcome.CountTaskKey)
        {
            return false;
        }

        if (count.Status == CountExecutionStatus.Open)
        {
            // Inventory can finish before WMS commits the original receipt. Let CAP retry.
            throw new InvalidOperationException("仓储盘点受理回执尚未落库。");
        }

        if (count.Status != CountExecutionStatus.PendingApproval)
        {
            return false;
        }

        var receipt = await dbContext.InventoryMovementRequests.SingleOrDefaultAsync(x =>
            x.OrganizationId == count.OrganizationId && x.EnvironmentId == count.EnvironmentId
            && x.SourceDocumentId == count.CountNo && x.SourceDocumentLineId == null
            && x.MovementType == InventoryMovementTypes.CountAdjustment && x.IdempotencyKey == outcome.AdjustmentKey,
            cancellationToken);
        if (receipt is null)
        {
            return false;
        }

        if (outcome.MovementId is { } movementId && outcome.ConfirmedAtUtc is { } confirmedAt)
        {
            receipt.MarkPosted(movementId);
            count.ApplyPostedAdjustment(confirmedAt, receipt.Quantity);
        }
        else if (outcome.RequiresRecount)
        {
            CompleteCountExecutionCommandHandler.RecordRecountRequired(count, receipt);
        }
        else
        {
            return false;
        }

        return true;
    }
}
