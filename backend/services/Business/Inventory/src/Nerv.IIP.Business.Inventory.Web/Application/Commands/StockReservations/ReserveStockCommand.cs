using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;

namespace Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;

public sealed record ReserveStockCommand(
    string OrganizationId,
    string EnvironmentId,
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
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null,
    DateOnly? AsOfDate = null,
    bool AllowExpiredStock = false,
    bool ExpiryOverridePermissionGranted = false) : ICommand<ReserveStockResult>;

public sealed record ReserveStockResult(
    StockReservationId ReservationId,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    string? LotNo = null,
    DateOnly? ProductionDate = null,
    DateOnly? ExpiryDate = null);

public sealed record ReserveFefoStockCommand(
    string OrganizationId,
    string EnvironmentId,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal Quantity,
    string? LocationCode = null,
    DateOnly? AsOfDate = null,
    bool AllowExpiredStock = false,
    bool ExpiryOverridePermissionGranted = false) : ICommand<ReserveFefoStockResult>;

public sealed record ReserveFefoStockResult(IReadOnlyCollection<ReserveFefoStockAllocationResult> Allocations, decimal ReservedQuantity);

public sealed record ReserveFefoStockAllocationResult(
    StockReservationId ReservationId,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    decimal ReservedQuantity,
    decimal AvailableQuantity);

public sealed class ReserveStockCommandValidator : AbstractValidator<ReserveStockCommand>
{
    public ReserveStockCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
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
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.ExpiryDate).GreaterThanOrEqualTo(x => x.ProductionDate!.Value).When(x => x.ProductionDate is not null && x.ExpiryDate is not null);
    }
}

public sealed class ReserveStockCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ReserveStockCommand, ReserveStockResult>
{
    public async Task<ReserveStockResult> Handle(ReserveStockCommand request, CancellationToken cancellationToken)
    {
        var qualityStatus = StockQualityStatus.Normalize(request.QualityStatus);
        var ownerType = StockOwnerType.Normalize(request.OwnerType);
        var query = dbContext.StockLedgers.Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SkuCode == request.SkuCode
                && x.UomCode == request.UomCode
                && x.SiteCode == request.SiteCode
                && x.LocationCode == request.LocationCode
                && x.LotNo == request.LotNo
                && x.SerialNo == request.SerialNo
                && x.QualityStatus == qualityStatus
                && x.OwnerType == ownerType
                && x.OwnerId == request.OwnerId);
        if (request.ProductionDate is not null)
        {
            query = query.Where(x => x.ProductionDate == request.ProductionDate);
        }

        if (request.ExpiryDate is not null)
        {
            query = query.Where(x => x.ExpiryDate == request.ExpiryDate);
        }

        var ledger = await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException("Stock ledger does not exist for the requested reservation scope.");

        if (ledger.IsExpired(GetBusinessDate(request.AsOfDate)) && !HasExpiredStockOverride(request.AllowExpiredStock, request.ExpiryOverridePermissionGranted))
        {
            throw new KnownException("Expired stock cannot be reserved without expiry override permission.");
        }

        var candidate = StockReservation.Reserve(
            ledger,
            request.SourceService,
            request.SourceDocumentId,
            request.SourceDocumentLineId,
            request.IdempotencyKey,
            request.Quantity);
        var existing = await dbContext.StockReservations.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceService == candidate.SourceService
                && x.SourceDocumentId == candidate.SourceDocumentId
                && x.IdempotencyKey == candidate.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!existing.HasSamePayload(candidate))
            {
                throw new KnownException("Stock reservation idempotency key conflicts with an existing reservation payload.");
            }

            return new ReserveStockResult(existing.Id, existing.OpenQuantity, ledger.AvailableQuantity, existing.LotNo, existing.ProductionDate, existing.ExpiryDate);
        }

        ledger.Reserve(candidate);
        dbContext.StockReservations.Add(candidate);
        return new ReserveStockResult(candidate.Id, candidate.OpenQuantity, ledger.AvailableQuantity, candidate.LotNo, candidate.ProductionDate, candidate.ExpiryDate);
    }

    private static DateOnly GetBusinessDate(DateOnly? asOfDate)
    {
        return asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static bool HasExpiredStockOverride(bool allowExpiredStock, bool expiryOverridePermissionGranted)
    {
        return allowExpiredStock && expiryOverridePermissionGranted;
    }
}

public sealed class ReserveFefoStockCommandValidator : AbstractValidator<ReserveFefoStockCommand>
{
    public ReserveFefoStockCommandValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.SourceService).RequiredInventoryCode(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SourceDocumentLineId).MaximumLength(150);
        RuleFor(x => x.IdempotencyKey).RequiredInventoryCode(InventoryValidationRules.IdempotencyKeyMaxLength);
        RuleFor(x => x.SkuCode).RequiredInventoryCode(100);
        RuleFor(x => x.UomCode).RequiredInventoryCode(50);
        RuleFor(x => x.SiteCode).RequiredInventoryCode(100);
        RuleFor(x => x.LocationCode).OptionalInventoryCode(100);
        RuleFor(x => x.QualityStatus).RequiredInventoryCode(50);
        RuleFor(x => x.OwnerType).RequiredInventoryCode(50);
        RuleFor(x => x.OwnerId).OptionalInventoryCode(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class ReserveFefoStockCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ReserveFefoStockCommand, ReserveFefoStockResult>
{
    public async Task<ReserveFefoStockResult> Handle(ReserveFefoStockCommand request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.StockReservations
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SourceService == request.SourceService
                && x.SourceDocumentId == request.SourceDocumentId
                && (x.IdempotencyKey == request.IdempotencyKey || x.IdempotencyKey.StartsWith(request.IdempotencyKey + ":part-")))
            .OrderBy(x => x.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.LotNo)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            var existingQuantity = existing.Sum(x => x.ReservedQuantity);
            if (existingQuantity != request.Quantity)
            {
                throw new KnownException("FEFO stock reservation idempotency key conflicts with an existing reservation payload.");
            }

            return new ReserveFefoStockResult(
                existing.Select(x => new ReserveFefoStockAllocationResult(
                    x.Id,
                    x.LocationCode,
                    x.LotNo,
                    x.SerialNo,
                    x.ProductionDate,
                    x.ExpiryDate,
                    x.OpenQuantity,
                    0m)).ToList(),
                existingQuantity);
        }

        var qualityStatus = StockQualityStatus.Normalize(request.QualityStatus);
        var ownerType = StockOwnerType.Normalize(request.OwnerType);
        var asOfDate = request.AsOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var allowExpired = request.AllowExpiredStock && request.ExpiryOverridePermissionGranted;
        var query = dbContext.StockLedgers
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SkuCode == request.SkuCode
                && x.UomCode == request.UomCode
                && x.SiteCode == request.SiteCode
                && x.QualityStatus == qualityStatus
                && x.OwnerType == ownerType
                && x.OwnerId == request.OwnerId
                && x.OnHandQuantity > x.ReservedQuantity);

        if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            query = query.Where(x => x.LocationCode == request.LocationCode);
        }

        if (!allowExpired)
        {
            query = query.Where(x => x.ExpiryDate == null || x.ExpiryDate >= asOfDate);
        }

        var ledgers = await query
            .OrderBy(x => x.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.UpdatedAtUtc)
            .ThenBy(x => x.LocationCode)
            .ThenBy(x => x.LotNo)
            .Take(100)
            .ToListAsync(cancellationToken);

        var remaining = request.Quantity;
        var allocations = new List<ReserveFefoStockAllocationResult>();
        for (var index = 0; index < ledgers.Count && remaining > 0; index++)
        {
            var ledger = ledgers[index];
            var quantity = Math.Min(remaining, ledger.AvailableQuantity);
            var idempotencyKey = allocations.Count == 0 ? request.IdempotencyKey : $"{request.IdempotencyKey}:part-{allocations.Count + 1}";
            var reservation = StockReservation.Reserve(
                ledger,
                request.SourceService,
                request.SourceDocumentId,
                request.SourceDocumentLineId,
                idempotencyKey,
                quantity);
            ledger.Reserve(reservation);
            dbContext.StockReservations.Add(reservation);
            allocations.Add(new ReserveFefoStockAllocationResult(
                reservation.Id,
                reservation.LocationCode,
                reservation.LotNo,
                reservation.SerialNo,
                reservation.ProductionDate,
                reservation.ExpiryDate,
                reservation.ReservedQuantity,
                ledger.AvailableQuantity));
            remaining -= quantity;
        }

        if (remaining > 0)
        {
            throw new KnownException("Available non-expired FEFO stock is insufficient for the requested reservation quantity.");
        }

        return new ReserveFefoStockResult(allocations, request.Quantity);
    }
}
