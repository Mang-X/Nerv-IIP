using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Approval;
using Nerv.IIP.Contracts.Approval;
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
    IStockCountApprovalClient approvalClient,
    IOptions<StockCountAdjustmentApprovalOptions>? approvalOptions = null)
    : ICommandHandler<ConfirmStockCountAdjustmentCommand, ConfirmStockCountAdjustmentResult>
{
    private readonly StockCountAdjustmentApprovalOptions approvalOptions = approvalOptions?.Value ?? new StockCountAdjustmentApprovalOptions();
    private readonly IStockCountApprovalClient approvalClient = approvalClient ?? throw new ArgumentNullException(nameof(approvalClient));

    public async Task<ConfirmStockCountAdjustmentResult> Handle(ConfirmStockCountAdjustmentCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.StockCountTasks.SingleOrDefaultAsync(x => x.Id == request.CountTaskId, cancellationToken)
            ?? throw new KnownException($"未找到盘点任务：{request.CountTaskId}。");
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
                throw new KnownException("盘点调整幂等键与已有盘点数量冲突，请更换幂等键。");
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
            ?? throw new KnownException("未找到盘点任务对应的库存台账。");

        var varianceQuantity = request.CountedQuantity - ledger.OnHandQuantity;
        var varianceAmount = Math.Round(Math.Abs(varianceQuantity * ledger.MovingAverageUnitCost), 6, MidpointRounding.ToEven);
        if (approvalOptions.RequiresApproval(varianceQuantity, varianceAmount))
        {
            try
            {
                task.SubmitForApproval(ledger, request.CountedQuantity);
            }
            catch (StockCountRecountRequiredException)
            {
                throw new KnownException("盘点快照已过期，请先重新盘点后再提交审批。");
            }

            var approval = await approvalClient.StartApprovalAsync(
                new StockCountApprovalRequest(
                    task.OrganizationId,
                    task.EnvironmentId,
                    approvalOptions.TemplateCode,
                    // #1702：发起侧与回写消费侧共用审批契约的来源服务常量（漂移即回写静默丢事件）。
                    ApprovalSourceServices.Inventory,
                    approvalOptions.DocumentType,
                    task.CountTaskCode,
                    "system:inventory",
                    varianceAmount),
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
        catch (StockCountRecountRequiredException)
        {
            throw new KnownException("盘点快照已过期，请先重新盘点后再确认调整。");
        }
        catch (InventoryDomainException exception) when (IsCommittedStockGuard(exception))
        {
            throw new KnownException("已过账库存不允许再次调整，请刷新后重试。");
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
