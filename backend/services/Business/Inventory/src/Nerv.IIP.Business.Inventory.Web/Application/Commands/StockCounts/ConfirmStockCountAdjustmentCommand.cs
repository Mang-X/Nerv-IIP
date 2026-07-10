using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Approval;
using Microsoft.Extensions.Options;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;

public sealed record ConfirmStockCountAdjustmentCommand(
    StockCountTaskId CountTaskId,
    decimal CountedQuantity,
    string IdempotencyKey) : ICommand<ConfirmStockCountAdjustmentResult>;

public sealed record ConfirmStockCountAdjustmentResult(
    StockMovementId? MovementId,
    decimal VarianceQuantity,
    decimal OnHandQuantity,
    string Status,
    string? ApprovalChainId);

public sealed class ConfirmStockCountAdjustmentCommandValidator : AbstractValidator<ConfirmStockCountAdjustmentCommand>
{
    public ConfirmStockCountAdjustmentCommandValidator()
    {
        RuleFor(x => x.CountTaskId).NotEmpty();
        RuleFor(x => x.CountedQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.IdempotencyKey).RequiredInventoryCode(InventoryValidationRules.IdempotencyKeyMaxLength);
    }
}

public sealed class ConfirmStockCountAdjustmentCommandHandler(
    ApplicationDbContext dbContext,
    IOptions<StockCountAdjustmentApprovalOptions>? approvalOptions = null,
    IStockCountApprovalClient? approvalClient = null)
    : ICommandHandler<ConfirmStockCountAdjustmentCommand, ConfirmStockCountAdjustmentResult>
{
    private readonly StockCountAdjustmentApprovalOptions approvalOptions = approvalOptions?.Value ?? new StockCountAdjustmentApprovalOptions();
    private readonly IStockCountApprovalClient approvalClient = approvalClient ?? new GeneratedStockCountApprovalClient();

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
                string.IsNullOrWhiteSpace(existingAdjustment.MovementId) ? null : new StockMovementId(Guid.Parse(existingAdjustment.MovementId)),
                existingAdjustment.VarianceQuantity,
                OnHandQuantity: await dbContext.StockLedgers
                    .Where(x => x.OrganizationId == task.LedgerOrganizationId
                        && x.EnvironmentId == task.LedgerEnvironmentId
                        && x.SkuCode == task.SkuCode
                        && x.UomCode == task.UomCode
                        && x.SiteCode == task.SiteCode
                        && x.LocationCode == task.LocationCode
                        && x.LotNo == task.LotNo
                        && x.SerialNo == task.SerialNo
                        && x.QualityStatus == task.QualityStatus
                        && x.OwnerType == task.OwnerType
                        && x.OwnerId == task.OwnerId)
                    .Select(x => x.OnHandQuantity)
                    .SingleAsync(cancellationToken),
                existingAdjustment.Status,
                existingAdjustment.ApprovalChainId);
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

        var varianceQuantity = request.CountedQuantity - ledger.OnHandQuantity;
        var varianceAmount = Math.Round(Math.Abs(varianceQuantity * ledger.MovingAverageUnitCost), 6, MidpointRounding.ToEven);
        if (approvalOptions.RequiresApproval(varianceQuantity, varianceAmount))
        {
            try
            {
                task.SubmitForApproval(ledger, request.CountedQuantity);
            }
            catch (StockCountRecountRequiredException exception)
            {
                throw new KnownException(exception.Message);
            }

            var approval = await approvalClient.StartApprovalAsync(
                new StockCountApprovalRequest(
                    task.OrganizationId,
                    task.EnvironmentId,
                    approvalOptions.TemplateCode,
                    "inventory",
                    "inventory-count-variance",
                    task.CountTaskCode,
                    "system:inventory"),
                cancellationToken);
            var pendingAdjustment = StockCountAdjustment.RecordPendingApproval(task, request.IdempotencyKey, approval.ChainId, varianceAmount);
            dbContext.StockCountAdjustments.Add(pendingAdjustment);
            return new ConfirmStockCountAdjustmentResult(null, varianceQuantity, ledger.OnHandQuantity, pendingAdjustment.Status, approval.ChainId);
        }

        StockMovement movement;
        try
        {
            movement = task.ConfirmAdjustment(ledger, request.CountedQuantity, request.IdempotencyKey);
        }
        catch (StockCountRecountRequiredException exception)
        {
            throw new KnownException(exception.Message);
        }
        catch (InventoryDomainException exception) when (IsCommittedStockGuard(exception))
        {
            throw new KnownException(exception.Message);
        }

        dbContext.StockMovements.Add(movement);
        var adjustment = StockCountAdjustment.Record(task, movement, request.IdempotencyKey);
        dbContext.StockCountAdjustments.Add(adjustment);
        return new ConfirmStockCountAdjustmentResult(movement.Id, task.VarianceQuantity ?? 0, ledger.OnHandQuantity, adjustment.Status, null);
    }

    private static bool IsCommittedStockGuard(InventoryDomainException exception)
    {
        return exception.Reason == InventoryDomainFailureReason.CommittedStockProtection;
    }
}
