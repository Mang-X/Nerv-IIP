using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Inventory.Web.Application.Queries;

public sealed record ListLineSideInventoryBalancesQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SiteCode = null,
    string? LocationCode = null,
    string? SkuCode = null,
    DateOnly? AsOfDate = null,
    int Page = 1,
    int PageSize = 50) : IQuery<LineSideInventoryBalanceListResponse>;

public sealed record LineSideInventoryBalanceListResponse(
    IReadOnlyCollection<LineSideInventoryBalanceItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    DateOnly AsOfDate);

public sealed record LineSideInventoryBalanceItem(
    string SiteCode,
    string LocationCode,
    string SkuCode,
    string UomCode,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    int LotCount,
    DateOnly? OldestProductionDate,
    int? AgeDays,
    string AgeCompleteness);

public sealed class ListLineSideInventoryBalancesQueryValidator
    : AbstractValidator<ListLineSideInventoryBalancesQuery>
{
    public ListLineSideInventoryBalancesQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.SiteCode).OptionalInventoryCode(100);
        RuleFor(x => x.LocationCode).OptionalInventoryCode(100);
        RuleFor(x => x.SkuCode).OptionalInventoryCode(100);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, StockCountQueryLimits.MaxPageSize);
    }
}

public sealed class ListLineSideInventoryBalancesQueryHandler(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider)
    : IQueryHandler<ListLineSideInventoryBalancesQuery, LineSideInventoryBalanceListResponse>
{
    public async Task<LineSideInventoryBalanceListResponse> Handle(
        ListLineSideInventoryBalancesQuery request,
        CancellationToken cancellationToken)
    {
        var asOfDate = request.AsOfDate ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var ledgers =
            from ledger in dbContext.StockLedgers.AsNoTracking()
            join location in dbContext.StockLocations.AsNoTracking()
                on new
                {
                    ledger.OrganizationId,
                    ledger.EnvironmentId,
                    ledger.SiteCode,
                    ledger.LocationCode,
                }
                equals new
                {
                    location.OrganizationId,
                    location.EnvironmentId,
                    location.SiteCode,
                    location.LocationCode,
                }
            where ledger.OrganizationId == request.OrganizationId
                  && ledger.EnvironmentId == request.EnvironmentId
                  && location.LocationType == "line-side"
                  && ledger.OnHandQuantity > 0m
            select ledger;

        ledgers = ApplyEquality(ledgers, request.SiteCode, (source, value) => source.Where(x => x.SiteCode == value));
        ledgers = ApplyEquality(ledgers, request.LocationCode, (source, value) => source.Where(x => x.LocationCode == value));
        ledgers = ApplyEquality(ledgers, request.SkuCode, (source, value) => source.Where(x => x.SkuCode == value));

        var grouped = ledgers
            .GroupBy(x => new { x.SiteCode, x.LocationCode, x.SkuCode, x.UomCode })
            .Select(group => new LineSideInventoryBalanceProjection
            {
                SiteCode = group.Key.SiteCode,
                LocationCode = group.Key.LocationCode,
                SkuCode = group.Key.SkuCode,
                UomCode = group.Key.UomCode,
                OnHandQuantity = group.Sum(x => x.OnHandQuantity),
                ReservedQuantity = group.Sum(x => x.ReservedQuantity),
                LotCount = group.Where(x => x.LotNo != null).Select(x => x.LotNo).Distinct().Count(),
                OldestProductionDate = group.Min(x => x.ProductionDate),
                DimensionCount = group.Count(),
                DatedDimensionCount = group.Count(x => x.ProductionDate != null),
            });

        var totalCount = await grouped.CountAsync(cancellationToken);
        var projections = await grouped
            .OrderBy(x => x.SiteCode)
            .ThenBy(x => x.LocationCode)
            .ThenBy(x => x.SkuCode)
            .ThenBy(x => x.UomCode)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToArrayAsync(cancellationToken);

        var items = projections.Select(x => ToResponse(x, asOfDate)).ToArray();
        return new LineSideInventoryBalanceListResponse(
            items,
            totalCount,
            request.Page,
            request.PageSize,
            asOfDate);
    }

    private static LineSideInventoryBalanceItem ToResponse(
        LineSideInventoryBalanceProjection projection,
        DateOnly asOfDate)
    {
        var ageCompleteness = projection.DatedDimensionCount switch
        {
            0 => "unavailable",
            var count when count == projection.DimensionCount => "complete",
            _ => "partial",
        };
        int? ageDays = projection.OldestProductionDate is { } productionDate
            ? Math.Max(0, asOfDate.DayNumber - productionDate.DayNumber)
            : null;

        return new LineSideInventoryBalanceItem(
            projection.SiteCode,
            projection.LocationCode,
            projection.SkuCode,
            projection.UomCode,
            projection.OnHandQuantity,
            projection.ReservedQuantity,
            projection.OnHandQuantity - projection.ReservedQuantity,
            projection.LotCount,
            projection.OldestProductionDate,
            ageDays,
            ageCompleteness);
    }

    private static IQueryable<TEntity> ApplyEquality<TEntity>(
        IQueryable<TEntity> source,
        string? value,
        Func<IQueryable<TEntity>, string, IQueryable<TEntity>> apply)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null ? source : apply(source, normalized);
    }

    private sealed class LineSideInventoryBalanceProjection
    {
        public required string SiteCode { get; init; }

        public required string LocationCode { get; init; }

        public required string SkuCode { get; init; }

        public required string UomCode { get; init; }

        public decimal OnHandQuantity { get; init; }

        public decimal ReservedQuantity { get; init; }

        public int LotCount { get; init; }

        public DateOnly? OldestProductionDate { get; init; }

        public int DimensionCount { get; init; }

        public int DatedDimensionCount { get; init; }
    }
}
