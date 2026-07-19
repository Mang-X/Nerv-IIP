using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;

namespace Nerv.IIP.Business.Inventory.Web.Application.Queries;

public sealed record GetStockAvailabilityQuery(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId,
    DateOnly? AsOfDate = null) : IQuery<StockAvailabilityResponse>;

public sealed record StockAvailabilityResponse(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string? QualityStatus,
    string? OwnerType,
    string? OwnerId,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal InventoryValue,
    IReadOnlyCollection<StockAvailabilityLineResponse> Items);

public sealed record StockAvailabilityLineResponse(
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate,
    int? ShelfLifeDays,
    string? ExpiryDateSource,
    bool IsExpired,
    bool IsBlocked,
    string? BlockReasonCode,
    string? BlockReason,
    bool MovementAllowed,
    string? MovementBlockReasonCode,
    string? MovementBlockReason,
    bool CountAllowed,
    string? CountBlockReasonCode,
    string? CountBlockReason,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal InventoryValue);

public sealed class GetStockAvailabilityQueryValidator : AbstractValidator<GetStockAvailabilityQuery>
{
    public GetStockAvailabilityQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.SkuCode).RequiredInventoryCode(100);
        RuleFor(x => x.UomCode).RequiredInventoryCode(50);
        RuleFor(x => x.SiteCode).RequiredInventoryCode(100);
        RuleFor(x => x.LocationCode).OptionalInventoryCode(100);
        RuleFor(x => x.LotNo).OptionalInventoryCode(100);
        RuleFor(x => x.SerialNo).OptionalInventoryCode(100);
        RuleFor(x => x.QualityStatus).OptionalInventoryCode(50);
        RuleFor(x => x.OwnerType).OptionalInventoryCode(50);
        RuleFor(x => x.OwnerId).OptionalInventoryCode(100);
    }
}

public sealed class GetStockAvailabilityQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetStockAvailabilityQuery, StockAvailabilityResponse>
{
    public const int MaxResultLines = 1000;

    public async Task<StockAvailabilityResponse> Handle(GetStockAvailabilityQuery request, CancellationToken cancellationToken)
    {
        var qualityStatus = NormalizeQualityStatus(request.QualityStatus);
        var ownerType = NormalizeOwnerType(request.OwnerType);
        var query = dbContext.StockLedgers
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SkuCode == request.SkuCode
                && x.UomCode == request.UomCode
                && x.SiteCode == request.SiteCode);

        if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            query = query.Where(x => x.LocationCode == request.LocationCode);
        }

        if (!string.IsNullOrWhiteSpace(request.LotNo))
        {
            query = query.Where(x => x.LotNo == request.LotNo);
        }

        if (!string.IsNullOrWhiteSpace(request.SerialNo))
        {
            query = query.Where(x => x.SerialNo == request.SerialNo);
        }

        if (!string.IsNullOrWhiteSpace(qualityStatus))
        {
            query = query.Where(x => x.QualityStatus == qualityStatus);
        }

        if (!string.IsNullOrWhiteSpace(ownerType))
        {
            query = query.Where(x => x.OwnerType == ownerType);
        }

        if (!string.IsNullOrWhiteSpace(request.OwnerId))
        {
            query = query.Where(x => x.OwnerId == request.OwnerId);
        }

        var asOfDate = request.AsOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var projectedItems = await query
            .GroupBy(x => new
            {
                x.LocationCode,
                x.LotNo,
                x.SerialNo,
                x.QualityStatus,
                x.OwnerType,
                x.OwnerId,
                x.ProductionDate,
                x.ExpiryDate,
                x.ShelfLifeDays,
                x.ExpiryDateSource,
                x.IsFrozenForCount,
            })
            .OrderBy(group => group.Key.LocationCode)
            .ThenBy(group => group.Key.LotNo)
            .ThenBy(group => group.Key.SerialNo)
            .Select(group => new
            {
                group.Key.LocationCode,
                group.Key.LotNo,
                group.Key.SerialNo,
                group.Key.QualityStatus,
                group.Key.OwnerType,
                group.Key.OwnerId,
                group.Key.ProductionDate,
                group.Key.ExpiryDate,
                group.Key.ShelfLifeDays,
                group.Key.ExpiryDateSource,
                group.Key.IsFrozenForCount,
                OnHandQuantity = group.Sum(x => x.OnHandQuantity),
                ReservedQuantity = group.Sum(x => x.ReservedQuantity),
                AvailableQuantity = group.Sum(x => x.OnHandQuantity) - group.Sum(x => x.ReservedQuantity),
                InventoryValue = group.Sum(x => x.InventoryValue),
            })
            .Take(MaxResultLines + 1)
            .ToListAsync(cancellationToken);
        if (projectedItems.Count > MaxResultLines)
        {
            throw new KnownException($"Inventory availability query returned more than {MaxResultLines} dimension lines. Add location, lot, serial, quality, or owner filters to narrow the request.");
        }

        var ambiguousCountScopes = projectedItems
            .GroupBy(item => (
                item.LocationCode,
                item.LotNo,
                item.SerialNo,
                item.QualityStatus,
                item.OwnerType,
                item.OwnerId))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        var items = projectedItems.Select(item =>
        {
            var countScopeAmbiguous = ambiguousCountScopes.Contains((
                item.LocationCode,
                item.LotNo,
                item.SerialNo,
                item.QualityStatus,
                item.OwnerType,
                item.OwnerId));
            var operation = StockOperationAvailabilityProjection.From(
                item.QualityStatus,
                item.ExpiryDate,
                item.IsFrozenForCount,
                asOfDate,
                countScopeAmbiguous);
            return new StockAvailabilityLineResponse(
                item.LocationCode,
                item.LotNo,
                item.SerialNo,
                item.QualityStatus,
                item.OwnerType,
                item.OwnerId,
                item.ProductionDate,
                item.ExpiryDate,
                item.ShelfLifeDays,
                item.ExpiryDateSource,
                operation.IsExpired,
                operation.IsBlocked,
                operation.BlockReasonCode,
                operation.BlockReason,
                operation.MovementAllowed,
                operation.MovementBlockReasonCode,
                operation.MovementBlockReason,
                operation.CountAllowed,
                operation.CountBlockReasonCode,
                operation.CountBlockReason,
                item.OnHandQuantity,
                item.ReservedQuantity,
                item.AvailableQuantity,
                item.InventoryValue);
        }).ToList();

        var onHand = items.Sum(x => x.OnHandQuantity);
        var reserved = items.Sum(x => x.ReservedQuantity);
        var inventoryValue = items.Sum(x => x.InventoryValue);
        return new StockAvailabilityResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.LocationCode,
            request.LotNo,
            request.SerialNo,
            qualityStatus,
            ownerType,
            request.OwnerId,
            onHand,
            reserved,
            onHand - reserved,
            inventoryValue,
            items);
    }

    private static string? NormalizeQualityStatus(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : StockQualityStatus.Normalize(value);
    }

    private static string? NormalizeOwnerType(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : StockOwnerType.Normalize(value);
    }
}

public sealed record StockOperationAvailability(
    bool IsExpired,
    bool IsBlocked,
    string? BlockReasonCode,
    string? BlockReason,
    bool MovementAllowed,
    string? MovementBlockReasonCode,
    string? MovementBlockReason,
    bool CountAllowed,
    string? CountBlockReasonCode,
    string? CountBlockReason);

public static class StockOperationAvailabilityProjection
{
    public static StockOperationAvailability From(
        string qualityStatus,
        DateOnly? expiryDate,
        bool isFrozenForCount,
        DateOnly asOfDate,
        bool countScopeAmbiguous = false)
    {
        var isExpired = expiryDate is not null && expiryDate.Value < asOfDate;
        var qualityBlocked = qualityStatus == StockQualityStatus.Blocked;
        var blockReasonCode = isExpired ? "expired-stock" : isFrozenForCount ? "count-frozen" : qualityBlocked ? "quality-blocked" : null;
        var blockReason = isExpired
            ? "已过期，常规移动需授权放行。"
            : isFrozenForCount
                ? "库存已被盘点任务冻结。"
                : qualityBlocked
                    ? "库存处于冻结质量状态。"
                    : null;
        var movementAllowed = !isExpired && !isFrozenForCount;
        var countAllowed = !isFrozenForCount && !countScopeAmbiguous;
        return new StockOperationAvailability(
            isExpired,
            isExpired || isFrozenForCount || qualityBlocked,
            blockReasonCode,
            blockReason,
            movementAllowed,
            movementAllowed ? null : blockReasonCode,
            movementAllowed ? null : blockReason,
            countAllowed,
            countAllowed ? null : isFrozenForCount ? "count-frozen" : "count-scope-ambiguous",
            countAllowed
                ? null
                : isFrozenForCount
                    ? "库存已被盘点任务冻结，不能重复创建盘点。"
                    : "同一盘点定位存在多个生产日期或效期，请先缩小到唯一库存台账。");
    }
}
