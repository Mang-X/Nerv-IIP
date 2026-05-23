using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;

public sealed record PostStockMovementCommand(
    string OrganizationId,
    string EnvironmentId,
    string MovementType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal Quantity) : ICommand<PostStockMovementResult>;

public sealed record PostStockMovementResult(StockMovementId MovementId, decimal OnHandQuantity, decimal AvailableQuantity);

public sealed class PostStockMovementCommandValidator : AbstractValidator<PostStockMovementCommand>
{
    public PostStockMovementCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MovementType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SourceService).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.QualityStatus).NotEmpty().MaximumLength(50);
        RuleFor(x => x.OwnerType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Quantity).NotEqual(0);
    }
}

public sealed class PostStockMovementCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<PostStockMovementCommand, PostStockMovementResult>
{
    public async Task<PostStockMovementResult> Handle(PostStockMovementCommand request, CancellationToken cancellationToken)
    {
        var movement = StockMovement.Post(
            request.OrganizationId,
            request.EnvironmentId,
            request.MovementType,
            request.SourceService,
            request.SourceDocumentId,
            request.SourceDocumentLineId,
            request.IdempotencyKey,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.LotNo,
            request.SerialNo,
            request.QualityStatus,
            request.OwnerType,
            request.OwnerId,
            request.Quantity);
        var existingMovement = await dbContext.StockMovements.SingleOrDefaultAsync(
            x => x.OrganizationId == movement.OrganizationId
                && x.EnvironmentId == movement.EnvironmentId
                && x.SourceService == movement.SourceService
                && x.SourceDocumentId == movement.SourceDocumentId
                && x.IdempotencyKey == movement.IdempotencyKey,
            cancellationToken);
        var ledger = await GetOrCreateLedgerAsync(movement, cancellationToken);
        if (existingMovement is not null)
        {
            if (!existingMovement.HasSamePayload(movement))
            {
                throw new KnownException("Stock movement idempotency key conflicts with an existing movement payload.");
            }

            return new PostStockMovementResult(existingMovement.Id, ledger.OnHandQuantity, ledger.AvailableQuantity);
        }

        var applied = ledger.ApplyMovement(movement);
        if (ReferenceEquals(applied, movement))
        {
            dbContext.StockMovements.Add(movement);
        }

        return new PostStockMovementResult(applied.Id, ledger.OnHandQuantity, ledger.AvailableQuantity);
    }

    private async Task<StockLedger> GetOrCreateLedgerAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        var ledger = await dbContext.StockLedgers.SingleOrDefaultAsync(
            x => x.OrganizationId == movement.OrganizationId
                && x.EnvironmentId == movement.EnvironmentId
                && x.SkuCode == movement.SkuCode
                && x.UomCode == movement.UomCode
                && x.SiteCode == movement.SiteCode
                && x.LocationCode == movement.LocationCode
                && x.LotNo == movement.LotNo
                && x.SerialNo == movement.SerialNo
                && x.QualityStatus == movement.QualityStatus
                && x.OwnerType == movement.OwnerType
                && x.OwnerId == movement.OwnerId,
            cancellationToken);
        if (ledger is not null)
        {
            return ledger;
        }

        ledger = StockLedger.Create(
            movement.OrganizationId,
            movement.EnvironmentId,
            movement.SkuCode,
            movement.UomCode,
            movement.SiteCode,
            movement.LocationCode,
            movement.LotNo,
            movement.SerialNo,
            movement.QualityStatus,
            movement.OwnerType,
            movement.OwnerId);
        dbContext.StockLedgers.Add(ledger);
        return ledger;
    }
}
