using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;

public sealed record CreateStockCountTaskCommand(
    string OrganizationId,
    string EnvironmentId,
    string CountTaskCode,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId) : ICommand<CreateStockCountTaskResult>;

public sealed record CreateStockCountTaskResult(StockCountTaskId CountTaskId, long ExpectedLedgerVersion);

public sealed class CreateStockCountTaskCommandValidator : AbstractValidator<CreateStockCountTaskCommand>
{
    public CreateStockCountTaskCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.CountTaskCode).RequiredInventoryCode(100);
        RuleFor(x => x.SkuCode).RequiredInventoryCode(100);
        RuleFor(x => x.UomCode).RequiredInventoryCode(50);
        RuleFor(x => x.SiteCode).RequiredInventoryCode(100);
        RuleFor(x => x.LocationCode).RequiredInventoryCode(100);
        RuleFor(x => x.LotNo).OptionalInventoryCode(100);
        RuleFor(x => x.SerialNo).OptionalInventoryCode(100);
        RuleFor(x => x.QualityStatus).RequiredInventoryCode(50);
        RuleFor(x => x.OwnerType).RequiredInventoryCode(50);
        RuleFor(x => x.OwnerId).OptionalInventoryCode(100);
    }
}

public sealed class CreateStockCountTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateStockCountTaskCommand, CreateStockCountTaskResult>
{
    public async Task<CreateStockCountTaskResult> Handle(CreateStockCountTaskCommand request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.StockCountTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.CountTaskCode == request.CountTaskCode,
            cancellationToken);
        if (existing is not null)
        {
            return new CreateStockCountTaskResult(existing.Id, existing.ExpectedLedgerVersion);
        }

        var ledger = await dbContext.StockLedgers.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SkuCode == request.SkuCode
                && x.UomCode == request.UomCode
                && x.SiteCode == request.SiteCode
                && x.LocationCode == request.LocationCode
                && x.LotNo == request.LotNo
                && x.SerialNo == request.SerialNo
                && x.QualityStatus == request.QualityStatus
                && x.OwnerType == request.OwnerType
                && x.OwnerId == request.OwnerId,
            cancellationToken)
            ?? throw new KnownException("Stock ledger does not exist for the requested count scope.");

        var task = StockCountTask.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.CountTaskCode,
            ledger.OrganizationId,
            ledger.EnvironmentId,
            ledger.SkuCode,
            ledger.UomCode,
            ledger.SiteCode,
            ledger.LocationCode,
            ledger.LotNo,
            ledger.SerialNo,
            ledger.QualityStatus,
            ledger.OwnerType,
            ledger.OwnerId,
            ledger.LedgerVersion);
        dbContext.StockCountTasks.Add(task);
        return new CreateStockCountTaskResult(task.Id, task.ExpectedLedgerVersion);
    }
}
