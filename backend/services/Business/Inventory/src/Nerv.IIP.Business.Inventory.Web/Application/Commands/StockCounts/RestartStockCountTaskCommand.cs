using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;

public sealed record RestartStockCountTaskCommand(
    StockCountTaskId CountTaskId) : ICommand<RestartStockCountTaskResult>;

public sealed record RestartStockCountTaskResult(
    StockCountTaskId CountTaskId,
    string Status,
    long ExpectedLedgerVersion);

public sealed class RestartStockCountTaskCommandValidator : AbstractValidator<RestartStockCountTaskCommand>
{
    public RestartStockCountTaskCommandValidator()
    {
        RuleFor(x => x.CountTaskId).NotEmpty();
    }
}

public sealed class RestartStockCountTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RestartStockCountTaskCommand, RestartStockCountTaskResult>
{
    public async Task<RestartStockCountTaskResult> Handle(RestartStockCountTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await dbContext.StockCountTasks.SingleOrDefaultAsync(x => x.Id == request.CountTaskId, cancellationToken)
            ?? throw new KnownException($"Stock count task '{request.CountTaskId}' was not found.");

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
            ?? throw new KnownException("Stock ledger does not exist for the requested count recount.");

        try
        {
            task.RestartRecount(ledger);
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message);
        }

        return new RestartStockCountTaskResult(task.Id, task.Status, task.ExpectedLedgerVersion);
    }
}
