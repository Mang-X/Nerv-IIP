using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Web.Application.MasterData;

namespace Nerv.IIP.Business.Inventory.Web.Application.Queries;

public sealed record ListStockExpiryAlertsQuery(
    string OrganizationId,
    string EnvironmentId,
    string SiteCode,
    string? SkuCode = null,
    string? LocationCode = null,
    DateOnly? AsOfDate = null,
    int? NearExpiryThresholdDays = null,
    bool IncludeZeroAvailable = false) : IQuery<StockExpiryAlertsResponse>;

public sealed record StockExpiryAlertsResponse(IReadOnlyCollection<StockExpiryAlertLineResponse> Items);

public sealed record StockExpiryAlertLineResponse(
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    DateOnly? ProductionDate,
    DateOnly ExpiryDate,
    int DaysUntilExpiry,
    bool IsExpired,
    bool IsNearExpiry,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity);

public sealed class ListStockExpiryAlertsQueryValidator : AbstractValidator<ListStockExpiryAlertsQuery>
{
    public ListStockExpiryAlertsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.SiteCode).RequiredInventoryCode(100);
        RuleFor(x => x.SkuCode).OptionalInventoryCode(100);
        RuleFor(x => x.LocationCode).OptionalInventoryCode(100);
        RuleFor(x => x.NearExpiryThresholdDays).GreaterThanOrEqualTo(0).LessThanOrEqualTo(3660).When(x => x.NearExpiryThresholdDays is not null);
    }
}

public sealed class ListStockExpiryAlertsQueryHandler(
    ApplicationDbContext dbContext,
    IInventorySkuExpiryPolicyProvider? skuExpiryPolicyProvider = null)
    : IQueryHandler<ListStockExpiryAlertsQuery, StockExpiryAlertsResponse>
{
    public const int MaxResultLines = 1000;

    public async Task<StockExpiryAlertsResponse> Handle(ListStockExpiryAlertsQuery request, CancellationToken cancellationToken)
    {
        var asOfDate = request.AsOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var nearExpiryThresholdDays = await ResolveNearExpiryThresholdDaysAsync(request, cancellationToken);
        var nearExpiryDate = asOfDate.AddDays(nearExpiryThresholdDays);
        var query = dbContext.StockLedgers
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SiteCode == request.SiteCode
                && x.ExpiryDate != null
                && x.ExpiryDate <= nearExpiryDate);

        if (!request.IncludeZeroAvailable)
        {
            query = query.Where(x => x.OnHandQuantity > x.ReservedQuantity);
        }

        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            query = query.Where(x => x.SkuCode == request.SkuCode);
        }

        if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            query = query.Where(x => x.LocationCode == request.LocationCode);
        }

        var ledgers = await query
            .OrderBy(x => x.ExpiryDate)
            .ThenBy(x => x.SkuCode)
            .ThenBy(x => x.LocationCode)
            .ThenBy(x => x.LotNo)
            .Take(MaxResultLines + 1)
            .ToListAsync(cancellationToken);
        if (ledgers.Count > MaxResultLines)
        {
            throw new KnownException($"Inventory expiry alert query returned more than {MaxResultLines} ledger lines. Add SKU or location filters to narrow the request.");
        }

        return new StockExpiryAlertsResponse(ledgers.Select(x =>
        {
            var expiryDate = x.ExpiryDate!.Value;
            var daysUntilExpiry = expiryDate.DayNumber - asOfDate.DayNumber;
            return new StockExpiryAlertLineResponse(
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.SerialNo,
                x.QualityStatus,
                x.OwnerType,
                x.OwnerId,
                x.ProductionDate,
                expiryDate,
                daysUntilExpiry,
                daysUntilExpiry < 0,
                daysUntilExpiry >= 0 && daysUntilExpiry <= nearExpiryThresholdDays,
                x.OnHandQuantity,
                x.ReservedQuantity,
                x.AvailableQuantity);
        }).ToList());
    }

    private async Task<int> ResolveNearExpiryThresholdDaysAsync(ListStockExpiryAlertsQuery request, CancellationToken cancellationToken)
    {
        if (request.NearExpiryThresholdDays is not null)
        {
            return request.NearExpiryThresholdDays.Value;
        }

        if (skuExpiryPolicyProvider is not null && !string.IsNullOrWhiteSpace(request.SkuCode))
        {
            var policy = await skuExpiryPolicyProvider.GetAsync(
                request.OrganizationId,
                request.EnvironmentId,
                request.SkuCode,
                cancellationToken);
            if (policy?.NearExpiryThresholdDays is >= 0)
            {
                return policy.NearExpiryThresholdDays.Value;
            }
        }

        return 30;
    }
}
