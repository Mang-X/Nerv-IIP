using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;

public sealed record CancelStockCountTaskCommand(
    StockCountTaskId CountTaskId,
    string Reason) : ICommand<CancelStockCountTaskResult>;

public sealed record CancelStockCountTaskResult(StockCountTaskId CountTaskId, string Status);

public sealed class CancelStockCountTaskCommandValidator : AbstractValidator<CancelStockCountTaskCommand>
{
    public CancelStockCountTaskCommandValidator()
    {
        RuleFor(x => x.CountTaskId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
    }
}

public sealed class CancelStockCountTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CancelStockCountTaskCommand, CancelStockCountTaskResult>
{
    public async Task<CancelStockCountTaskResult> Handle(CancelStockCountTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.StockCountTasks.SingleOrDefaultAsync(x => x.Id == request.CountTaskId, cancellationToken)
            ?? throw new KnownException($"未找到盘点任务：{request.CountTaskId}。");

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

        try
        {
            task.Cancel(ledger, request.Reason);
        }
        catch (InvalidOperationException)
        {
            throw new KnownException("盘点任务当前状态不支持取消，请刷新后重试。");
        }

        return new CancelStockCountTaskResult(task.Id, task.Status);
    }
}
