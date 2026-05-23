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
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetStockAvailabilityQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetStockAvailabilityQuery, StockAvailabilityResponse>
{
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

        var rows = await query.ToListAsync(cancellationToken);
        var onHand = rows.Sum(x => x.OnHandQuantity);
        var reserved = rows.Sum(x => x.ReservedQuantity);
        var items = rows
            .GroupBy(x => new { x.LocationCode, x.LotNo, x.SerialNo, x.QualityStatus, x.OwnerType, x.OwnerId })
            .Select(group =>
            {
                var groupOnHand = group.Sum(x => x.OnHandQuantity);
                var groupReserved = group.Sum(x => x.ReservedQuantity);
                return new StockAvailabilityLineResponse(
                    group.Key.LocationCode,
                    group.Key.LotNo,
                    group.Key.SerialNo,
                    group.Key.QualityStatus,
                    group.Key.OwnerType,
                    group.Key.OwnerId,
                    groupOnHand,
                    groupReserved,
                    groupOnHand - groupReserved);
            })
            .OrderBy(x => x.LocationCode, StringComparer.Ordinal)
            .ThenBy(x => x.LotNo, StringComparer.Ordinal)
            .ThenBy(x => x.SerialNo, StringComparer.Ordinal)
            .ToArray();
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
