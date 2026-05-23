using Microsoft.EntityFrameworkCore;

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
    string? OwnerId) : IQuery<StockAvailabilityResponse>;

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
    IReadOnlyCollection<StockAvailabilityLineResponse> Items);

public sealed record StockAvailabilityLineResponse(
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity);

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
        var qualityStatus = Normalize(request.QualityStatus);
        var ownerType = Normalize(request.OwnerType);
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

        var items = await query
            .GroupBy(x => new { x.LocationCode, x.LotNo, x.SerialNo, x.QualityStatus, x.OwnerType, x.OwnerId })
            .OrderBy(group => group.Key.LocationCode)
            .ThenBy(group => group.Key.LotNo)
            .ThenBy(group => group.Key.SerialNo)
            .Select(group => new StockAvailabilityLineResponse(
                group.Key.LocationCode,
                group.Key.LotNo,
                group.Key.SerialNo,
                group.Key.QualityStatus,
                group.Key.OwnerType,
                group.Key.OwnerId,
                group.Sum(x => x.OnHandQuantity),
                group.Sum(x => x.ReservedQuantity),
                group.Sum(x => x.OnHandQuantity) - group.Sum(x => x.ReservedQuantity)))
            .Take(MaxResultLines + 1)
            .ToListAsync(cancellationToken);
        if (items.Count > MaxResultLines)
        {
            throw new KnownException($"Inventory availability query returned more than {MaxResultLines} dimension lines. Add location, lot, serial, quality, or owner filters to narrow the request.");
        }

        var onHand = items.Sum(x => x.OnHandQuantity);
        var reserved = items.Sum(x => x.ReservedQuantity);
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
            items);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }
}
