using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
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
    string? OwnerId,
    string? IdempotencyKey = null) : ICommand<CreateStockCountTaskResult>;

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
        RuleFor(x => x.IdempotencyKey).OptionalInventoryCode(InventoryValidationRules.IdempotencyKeyMaxLength);
    }
}

public sealed class CreateStockCountTaskCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateStockCountTaskCommand, CreateStockCountTaskResult>
{
    public async Task<CreateStockCountTaskResult> Handle(CreateStockCountTaskCommand request, CancellationToken cancellationToken)
    {
        var qualityStatus = StockQualityStatus.Normalize(request.QualityStatus);
        var ownerType = StockOwnerType.Normalize(request.OwnerType);
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? request.CountTaskCode.Trim()
            : request.IdempotencyKey.Trim();
        var existing = await dbContext.StockCountTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!existing.HasSameCreationScope(
                    request.CountTaskCode,
                    request.SkuCode,
                    request.UomCode,
                    request.SiteCode,
                    request.LocationCode,
                    request.LotNo,
                    request.SerialNo,
                    request.QualityStatus,
                    request.OwnerType,
                    request.OwnerId))
            {
                throw new KnownException("Stock count task idempotency key conflicts with an existing count scope.");
            }

            return new CreateStockCountTaskResult(existing.Id, existing.ExpectedLedgerVersion);
        }

        var existingCountCode = await dbContext.StockCountTasks.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.CountTaskCode == request.CountTaskCode,
            cancellationToken);
        if (existingCountCode is not null)
        {
            throw new KnownException("Stock count task code conflicts with an existing idempotency key.");
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
                && x.QualityStatus == qualityStatus
                && x.OwnerType == ownerType
                && x.OwnerId == request.OwnerId,
            cancellationToken)
            ?? throw new KnownException("Stock ledger does not exist for the requested count scope.");

        var task = StockCountTask.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.CountTaskCode,
            idempotencyKey,
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
        ledger.FreezeForCount(task.CountTaskCode);
        dbContext.StockCountTasks.Add(task);
        return new CreateStockCountTaskResult(task.Id, task.ExpectedLedgerVersion);
    }
}
