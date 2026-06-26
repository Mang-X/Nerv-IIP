using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;

public sealed record ConfirmStockCountAdjustmentCommand(
    StockCountTaskId CountTaskId,
    decimal CountedQuantity,
    string IdempotencyKey) : ICommand<ConfirmStockCountAdjustmentResult>;

public sealed record ConfirmStockCountAdjustmentResult(StockMovementId MovementId, decimal VarianceQuantity, decimal OnHandQuantity);

public sealed class ConfirmStockCountAdjustmentCommandValidator : AbstractValidator<ConfirmStockCountAdjustmentCommand>
{
    public ConfirmStockCountAdjustmentCommandValidator()
    {
        RuleFor(x => x.CountTaskId).NotEmpty();
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.IdempotencyKey).RequiredInventoryCode(InventoryValidationRules.IdempotencyKeyMaxLength);
    }
}

public sealed class ConfirmStockCountAdjustmentCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ConfirmStockCountAdjustmentCommand, ConfirmStockCountAdjustmentResult>
{
    public async Task<ConfirmStockCountAdjustmentResult> Handle(ConfirmStockCountAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.StockCountTasks.SingleOrDefaultAsync(x => x.Id == request.CountTaskId, cancellationToken)
            ?? throw new KnownException($"Stock count task '{request.CountTaskId}' was not found.");
        var existingAdjustment = await dbContext.StockCountAdjustments.SingleOrDefaultAsync(
            x => x.OrganizationId == task.OrganizationId
                && x.EnvironmentId == task.EnvironmentId
                && x.CountTaskCode == task.CountTaskCode
                && x.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (existingAdjustment is not null)
        {
            if (existingAdjustment.CountedQuantity != request.CountedQuantity)
            {
                throw new KnownException("Stock count adjustment idempotency key conflicts with an existing counted quantity.");
            }

            return new ConfirmStockCountAdjustmentResult(
                new StockMovementId(Guid.Parse(existingAdjustment.MovementId)),
                existingAdjustment.VarianceQuantity,
                existingAdjustment.CountedQuantity);
        }

        var ledger = await dbContext.StockLedgers.SingleOrDefaultAsync(
            x => x.OrganizationId == task.LedgerOrganizationId
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
            cancellationToken)
            ?? throw new KnownException("Stock ledger does not exist for the requested count adjustment.");

        StockMovement movement;
        try
        {
            movement = task.ConfirmAdjustment(ledger, request.CountedQuantity, request.IdempotencyKey);
        }
        catch (StockCountRecountRequiredException exception)
        {
            throw new KnownException(exception.Message);
        }
        catch (InventoryDomainException exception) when (IsReservedStockGuard(exception))
        {
            throw new KnownException(exception.Message);
        }

        dbContext.StockMovements.Add(movement);
        var adjustment = StockCountAdjustment.Record(task, movement, request.IdempotencyKey);
        dbContext.StockCountAdjustments.Add(adjustment);
        return new ConfirmStockCountAdjustmentResult(movement.Id, task.VarianceQuantity ?? 0, ledger.OnHandQuantity);
    }

    private static bool IsReservedStockGuard(InventoryDomainException exception)
    {
        return exception.Reason == InventoryDomainFailureReason.CommittedStockProtection;
    }
}
