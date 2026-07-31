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
    bool ExpiryOverridePermissionGranted = false,
    string? TransferInSiteCode = null,
    string? TransferInLocationCode = null,
    decimal? TransferInQuantity = null) : ICommand<PostStockMovementResult>;

public sealed record PostStockMovementResult(
    StockMovementId MovementId,
    decimal OnHandQuantity,
    decimal AvailableQuantity,
    StockMovementId? TransferInMovementId = null,
    decimal? TransferInOnHandQuantity = null);

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
        RuleFor(x => x.TransferInSiteCode).OptionalInventoryCode(100);
        RuleFor(x => x.TransferInLocationCode).OptionalInventoryCode(100);
        RuleFor(x => x.ShelfLifeDays).GreaterThan(0).LessThanOrEqualTo(3660).When(x => x.ShelfLifeDays is not null);
        RuleFor(x => x.ExpiryDate).GreaterThanOrEqualTo(x => x.ProductionDate!.Value).When(x => x.ProductionDate is not null && x.ExpiryDate is not null);
    }
}

public sealed class PostStockMovementCommandHandler(
    ApplicationDbContext dbContext,
    IInventorySkuExpiryPolicyProvider? skuExpiryPolicyProvider = null)
    : ICommandHandler<PostStockMovementCommand, PostStockMovementResult>
{
    private const string TransferMovementType = "transfer";
    private const string TransferOutLegSuffix = ":out";
    private const string TransferInLegSuffix = ":in";

    /// <summary>调拨基础幂等键上限：列宽 128 减去最长腿后缀（:out，4 位），两腿拼接后都不越界。</summary>
    public const int TransferBaseIdempotencyKeyMaxLength =
        InventoryValidationRules.IdempotencyKeyMaxLength - 4;

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
        var isTransfer = IsTransfer(request.MovementType);
        ValidateTransferLegsOrReject(request, isTransfer);

        // 调拨拆成配平的两腿，各自带独立幂等键；非调拨仍沿用调用方的原始幂等键，行为不变。
        var outboundKey = isTransfer ? TransferLegKey(request.IdempotencyKey, TransferOutLegSuffix) : request.IdempotencyKey;
        var inboundKey = isTransfer ? TransferLegKey(request.IdempotencyKey, TransferInLegSuffix) : null;
        var movement = CreateMovementOrReject(request, outboundKey);
        var existingMovement = await FindMovementByIdempotencyKeyAsync(movement, outboundKey, cancellationToken);
        if (existingMovement is not null)
        {
            if (!existingMovement.HasSamePayload(movement))
            {
                throw new InventoryPostingRejectedException(
                    InventoryPostingFailureCodes.IdempotencyConflict,
                    "Stock movement idempotency key conflicts with an existing movement payload.");
            }

            var existingLedger = await FindLedgerAsync(existingMovement, cancellationToken);
            if (!isTransfer)
            {
                return new PostStockMovementResult(
                    existingMovement.Id,
                    existingLedger?.OnHandQuantity ?? 0m,
                    existingLedger?.AvailableQuantity ?? 0m);
            }

            var existingInbound = await FindMovementByIdempotencyKeyAsync(movement, inboundKey!, cancellationToken);
            var existingInboundLedger = existingInbound is null ? null : await FindLedgerAsync(existingInbound, cancellationToken);
            return new PostStockMovementResult(
                existingMovement.Id,
                existingLedger?.OnHandQuantity ?? 0m,
                existingLedger?.AvailableQuantity ?? 0m,
                existingInbound?.Id,
                existingInboundLedger?.OnHandQuantity);
        }

        var provenance = ResolveExpiryProvenance(request);
        var ledger = await GetOrCreateLedgerAsync(movement, provenance.ShelfLifeDays, provenance.Source, cancellationToken);
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

        if (!isTransfer)
        {
            return new PostStockMovementResult(applied.Id, ledger.OnHandQuantity, ledger.AvailableQuantity);
        }

        var inboundMovement = CreateTransferInMovementOrReject(request, inboundKey!, movement, ledger.MovingAverageUnitCost);
        var inboundLedger = await GetOrCreateLedgerAsync(inboundMovement, provenance.ShelfLifeDays, provenance.Source, cancellationToken);
        StockMovement appliedInbound;
        try
        {
            appliedInbound = inboundLedger.ApplyMovement(inboundMovement);
        }
        catch (InventoryDomainException exception)
        {
            throw InventoryPostingRejectedException.FromDomain(exception);
        }

        if (ReferenceEquals(appliedInbound, inboundMovement))
        {
            dbContext.StockMovements.Add(inboundMovement);
        }

        return new PostStockMovementResult(
            applied.Id,
            ledger.OnHandQuantity,
            ledger.AvailableQuantity,
            appliedInbound.Id,
            inboundLedger.OnHandQuantity);
    }

    private Task<StockMovement?> FindMovementByIdempotencyKeyAsync(
        StockMovement movement,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return dbContext.StockMovements.SingleOrDefaultAsync(
            x => x.OrganizationId == movement.OrganizationId
                && x.EnvironmentId == movement.EnvironmentId
                && x.SourceService == movement.SourceService
                && x.SourceDocumentId == movement.SourceDocumentId
                && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
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

    private async Task<StockLedger> GetOrCreateLedgerAsync(
        StockMovement movement,
        int? shelfLifeDays,
        string? expiryDateSource,
        CancellationToken cancellationToken)
    {
        var ledger = await FindLedgerAsync(movement, cancellationToken);
        if (ledger is not null)
        {
            if (movement.Quantity > 0)
            {
                ledger.MergeExpiryProvenance(shelfLifeDays, expiryDateSource);
            }
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
            movement.ExpiryDate,
            shelfLifeDays,
            expiryDateSource);
        dbContext.StockLedgers.Add(ledger);
        return ledger;
    }

    private static (int? ShelfLifeDays, string? Source) ResolveExpiryProvenance(PostStockMovementCommand request)
    {
        if (request.ExpiryDate is not null)
        {
            return (null, StockExpiryDateSource.Direct);
        }

        return request.ProductionDate is not null && request.ShelfLifeDays is not null
            ? (request.ShelfLifeDays, StockExpiryDateSource.Derived)
            : (null, null);
    }

    /// <summary>
    /// 调拨（transfer）必须一次提交配平的两腿：出库腿扣当前库位、入库腿加目标库位，数量等额相消。
    /// 缺腿或数量不配平一律整笔拒绝——单腿调拨过账会凭空增减库存（走查实证 +1L）。
    /// </summary>
    private static void ValidateTransferLegsOrReject(PostStockMovementCommand request, bool isTransfer)
    {
        var hasAnyTransferInField = !string.IsNullOrWhiteSpace(request.TransferInSiteCode)
            || !string.IsNullOrWhiteSpace(request.TransferInLocationCode)
            || request.TransferInQuantity is not null;
        if (!isTransfer)
        {
            if (hasAnyTransferInField)
            {
                throw new InventoryPostingRejectedException(
                    InventoryPostingFailureCodes.TransferLegsUnbalanced,
                    "只有调拨（transfer）移动才携带入库腿；入库、出库、调整类过账请不要填写目标库位与入库数量。");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(request.TransferInLocationCode) || request.TransferInQuantity is null)
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.TransferLegsUnbalanced,
                "调拨过账必须一次提交两腿：出库腿（当前库位、数量为负）与入库腿（目标库位、等额为正）。单腿调拨会凭空增减库存，已整笔拒绝。");
        }

        // 两腿要在调用方幂等键上各追加 :out / :in，不能截断——截断会让仅末几位不同的两个长键
        // 落到同一腿键上：payload 不同报假冲突、payload 相同第二笔被当重放静默吞掉。宁可拒绝。
        if (request.IdempotencyKey.Length > TransferBaseIdempotencyKeyMaxLength)
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.TransferLegsUnbalanced,
                $"调拨幂等键最长 {TransferBaseIdempotencyKeyMaxLength} 位（两腿需分别追加 {TransferOutLegSuffix} / {TransferInLegSuffix} 后缀），当前 {request.IdempotencyKey.Length} 位，请缩短后重试。");
        }

        if (request.Quantity >= 0)
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.TransferLegsUnbalanced,
                $"调拨出库腿数量必须为负数（从当前库位扣减），当前为 {request.Quantity}。");
        }

        if (request.TransferInQuantity <= 0)
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.TransferLegsUnbalanced,
                $"调拨入库腿数量必须为正数（加到目标库位），当前为 {request.TransferInQuantity}。");
        }

        var netQuantity = request.Quantity + request.TransferInQuantity.Value;
        if (netQuantity != 0m)
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.TransferLegsUnbalanced,
                $"调拨两腿数量必须配平：出库腿 {request.Quantity}、入库腿 {request.TransferInQuantity}，合计 {netQuantity} 不为零，已整笔拒绝。");
        }

        if (string.Equals(ResolveTransferInSiteCode(request).Trim(), request.SiteCode?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.TransferInLocationCode.Trim(), request.LocationCode?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.TransferLegsUnbalanced,
                "调拨的入库库位不能与出库库位相同，请选择不同的目标库位。");
        }
    }

    private static bool IsTransfer(string movementType)
    {
        return string.Equals(movementType?.Trim(), TransferMovementType, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveTransferInSiteCode(PostStockMovementCommand request)
    {
        return string.IsNullOrWhiteSpace(request.TransferInSiteCode) ? request.SiteCode : request.TransferInSiteCode;
    }

    /// <summary>
    /// 两腿幂等键 = 调用方幂等键 + 后缀。基础键长度已在 <see cref="ValidateTransferLegsOrReject"/> 里先行拒绝，
    /// 这里只做拼接：绝不截断，截断会把不同的键折叠成同一腿键。
    /// </summary>
    private static string TransferLegKey(string idempotencyKey, string suffix)
    {
        return idempotencyKey + suffix;
    }

    private static StockMovement CreateTransferInMovementOrReject(
        PostStockMovementCommand request,
        string inboundKey,
        StockMovement outboundMovement,
        decimal sourceMovingAverageUnitCost)
    {
        try
        {
            return StockMovement.Post(
                request.OrganizationId,
                request.EnvironmentId,
                outboundMovement.MovementType,
                request.SourceService,
                request.SourceDocumentId,
                request.SourceDocumentLineId,
                inboundKey,
                request.SkuCode,
                request.UomCode,
                ResolveTransferInSiteCode(request),
                request.TransferInLocationCode!,
                request.LotNo,
                request.SerialNo,
                request.QualityStatus,
                outboundMovement.OwnerType,
                request.OwnerId,
                request.TransferInQuantity!.Value,
                request.UnitCost ?? sourceMovingAverageUnitCost,
                outboundMovement.ProductionDate,
                outboundMovement.ExpiryDate);
        }
        catch (ArgumentException exception) when (IsUnsupportedMovementOrQuality(exception))
        {
            throw new InventoryPostingRejectedException(
                InventoryPostingFailureCodes.PostingRejected,
                exception.Message,
                exception);
        }
    }

    private static StockMovement CreateMovementOrReject(PostStockMovementCommand request, string idempotencyKey)
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
                idempotencyKey,
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
