using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.MasterData;

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
    decimal Quantity,
    decimal? UnitCost = null,
    StockReservationId? ReservationId = null,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null,
    int? ShelfLifeDays = null,
    DateOnly? AsOfDate = null,
    bool AllowExpiredStock = false,
    bool ExpiryOverridePermissionGranted = false) : ICommand<PostStockMovementResult>;

public sealed record PostStockMovementResult(StockMovementId MovementId, decimal OnHandQuantity, decimal AvailableQuantity);

public sealed class PostStockMovementCommandValidator : AbstractValidator<PostStockMovementCommand>
{
    public PostStockMovementCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.MovementType).RequiredInventoryCode(50);
        RuleFor(x => x.SourceService).RequiredInventoryCode(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SourceDocumentLineId).MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).RequiredInventoryCode(InventoryValidationRules.IdempotencyKeyMaxLength);
        RuleFor(x => x.SkuCode).RequiredInventoryCode(100);
        RuleFor(x => x.UomCode).RequiredInventoryCode(50);
        RuleFor(x => x.SiteCode).RequiredInventoryCode(100);
        RuleFor(x => x.LocationCode).RequiredInventoryCode(100);
        RuleFor(x => x.LotNo).OptionalInventoryCode(100);
        RuleFor(x => x.SerialNo).OptionalInventoryCode(100);
        RuleFor(x => x.QualityStatus).RequiredInventoryCode(50);
        RuleFor(x => x.OwnerType).RequiredInventoryCode(50);
        RuleFor(x => x.OwnerId).OptionalInventoryCode(100);
        RuleFor(x => x.Quantity).NotEqual(0);
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost is not null);
        RuleFor(x => x.ShelfLifeDays).GreaterThan(0).LessThanOrEqualTo(3660).When(x => x.ShelfLifeDays is not null);
        RuleFor(x => x.ExpiryDate).GreaterThanOrEqualTo(x => x.ProductionDate!.Value).When(x => x.ProductionDate is not null && x.ExpiryDate is not null);
    }
}

public sealed class PostStockMovementCommandHandler(
    ApplicationDbContext dbContext,
    IInventorySkuExpiryPolicyProvider? skuExpiryPolicyProvider = null)
    : ICommandHandler<PostStockMovementCommand, PostStockMovementResult>
{
    private static readonly HashSet<string> ExternalMovementTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "inbound",
        "outbound",
        "transfer",
        "adjustment",
    };

    public async Task<PostStockMovementResult> Handle(PostStockMovementCommand request, CancellationToken cancellationToken)
    {
        request = await ApplySkuShelfLifeDefaultAsync(request, cancellationToken);
        var movement = CreateMovementOrReject(request);
        var existingMovement = await dbContext.StockMovements.SingleOrDefaultAsync(
            x => x.OrganizationId == movement.OrganizationId
                && x.EnvironmentId == movement.EnvironmentId
                && x.SourceService == movement.SourceService
                && x.SourceDocumentId == movement.SourceDocumentId
                && x.IdempotencyKey == movement.IdempotencyKey,
            cancellationToken);
        if (existingMovement is not null)
        {
            if (!existingMovement.HasSamePayload(movement))
            {
                throw new InventoryPostingRejectedException(
                    InventoryPostingFailureCodes.IdempotencyConflict,
                    "Stock movement idempotency key conflicts with an existing movement payload.");
            }

            var existingLedger = await FindLedgerAsync(existingMovement, cancellationToken);
            return new PostStockMovementResult(
                existingMovement.Id,
                existingLedger?.OnHandQuantity ?? 0m,
                existingLedger?.AvailableQuantity ?? 0m);
        }

        var ledger = await GetOrCreateLedgerAsync(movement, cancellationToken);
        if (request.Quantity < 0 && ledger.IsExpired(GetBusinessDate(request)) && !HasExpiredStockOverride(request.AllowExpiredStock, request.ExpiryOverridePermissionGranted))
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.PostingRejected,
                "Expired stock cannot be posted by regular outbound movement without expiry override permission.");
        }

        if (request.ReservationId is not null)
        {
            if (request.Quantity > 0)
            {
                throw new InventoryPostingRejectedException(
                    InventoryPostingFailureCodes.ReservationAllocationRejected,
                    "Only outbound movements can allocate an existing stock reservation.");
            }

            var reservation = await dbContext.StockReservations.SingleOrDefaultAsync(x => x.Id == request.ReservationId, cancellationToken)
                ?? throw new InventoryPostingRejectedException(
                    InventoryPostingFailureCodes.ReservationNotFound,
                    $"Stock reservation '{request.ReservationId}' was not found.");
            try
            {
                ledger.AllocateReservation(reservation, Math.Abs(request.Quantity));
            }
            catch (InventoryDomainException exception)
            {
                throw InventoryPostingRejectedException.FromDomain(exception);
            }
        }

        StockMovement applied;
        try
        {
            applied = ledger.ApplyMovement(movement);
        }
        catch (InventoryDomainException exception)
        {
            throw InventoryPostingRejectedException.FromDomain(exception);
        }

        if (ReferenceEquals(applied, movement))
        {
            dbContext.StockMovements.Add(movement);
        }

        return new PostStockMovementResult(applied.Id, ledger.OnHandQuantity, ledger.AvailableQuantity);
    }

    private async Task<PostStockMovementCommand> ApplySkuShelfLifeDefaultAsync(
        PostStockMovementCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ProductionDate is null
            || request.ExpiryDate is not null
            || request.ShelfLifeDays is not null
            || skuExpiryPolicyProvider is null)
        {
            return request;
        }

        var policy = await skuExpiryPolicyProvider.GetAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            cancellationToken);
        return policy?.ShelfLifeDays is > 0
            ? request with { ShelfLifeDays = policy.ShelfLifeDays }
            : request;
    }

    private Task<StockLedger?> FindLedgerAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        var query = dbContext.StockLedgers.Where(
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
                && x.OwnerId == movement.OwnerId);
        if (movement.ProductionDate is not null)
        {
            query = query.Where(x => x.ProductionDate == movement.ProductionDate);
        }

        if (movement.ExpiryDate is not null)
        {
            query = query.Where(x => x.ExpiryDate == movement.ExpiryDate);
        }

        return query.SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<StockLedger> GetOrCreateLedgerAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        var ledger = await FindLedgerAsync(movement, cancellationToken);
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
            movement.OwnerId,
            movement.ProductionDate,
            movement.ExpiryDate);
        dbContext.StockLedgers.Add(ledger);
        return ledger;
    }

    private static StockMovement CreateMovementOrReject(PostStockMovementCommand request)
    {
        var movementType = NormalizeExternalMovementTypeOrReject(request.MovementType);
        var ownerType = NormalizeOwnerTypeOrReject(request.OwnerType);
        var expiryDate = request.ExpiryDate ?? DeriveExpiryDate(request.ProductionDate, request.ShelfLifeDays);
        try
        {
            return StockMovement.Post(
                request.OrganizationId,
                request.EnvironmentId,
                movementType,
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
                ownerType,
                request.OwnerId,
                request.Quantity,
                request.UnitCost,
                request.ProductionDate,
                expiryDate);
        }
        catch (ArgumentException exception) when (IsUnsupportedMovementOrQuality(exception))
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.PostingRejected,
                exception.Message,
                exception);
        }
    }

    private static DateOnly? DeriveExpiryDate(DateOnly? productionDate, int? shelfLifeDays)
    {
        if (productionDate is null || shelfLifeDays is null)
        {
            return null;
        }

        try
        {
            return productionDate.Value.AddDays(shelfLifeDays.Value);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.PostingRejected,
                "Shelf life days cannot derive an expiry date within the supported date range.",
                exception);
        }
    }

    private static DateOnly GetBusinessDate(PostStockMovementCommand request)
    {
        return request.AsOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static bool HasExpiredStockOverride(bool allowExpiredStock, bool expiryOverridePermissionGranted)
    {
        return allowExpiredStock && expiryOverridePermissionGranted;
    }

    private static string NormalizeExternalMovementTypeOrReject(string movementType)
    {
        var normalized = NormalizeRequired(movementType, nameof(movementType));
        return ExternalMovementTypes.Contains(normalized)
            ? normalized
            : throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.PostingRejected,
                $"Movement type '{movementType}' cannot be posted through the external stock movement command.");
    }

    private static string NormalizeOwnerTypeOrReject(string ownerType)
    {
        try
        {
            return StockOwnerType.Normalize(ownerType);
        }
        catch (ArgumentException exception)
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.PostingRejected,
                exception.Message,
                exception);
        }
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.PostingRejected,
                $"{parameterName} cannot be blank.")
            : value.Trim().ToLowerInvariant();
    }

    private static bool IsUnsupportedMovementOrQuality(ArgumentException exception)
    {
        // Keep these names aligned with StockMovement.Post movementType and StockQualityStatus.Normalize qualityStatus.
        return exception.ParamName is "movementType" or "qualityStatus";
    }
}
