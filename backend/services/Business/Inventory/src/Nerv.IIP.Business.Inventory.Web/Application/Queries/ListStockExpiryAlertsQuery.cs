using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
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
    bool IncludeZeroAvailable = false,
    int Page = 1,
    int PageSize = 50) : IQuery<StockExpiryAlertsResponse>;

public sealed record StockExpiryAlertsResponse(
    IReadOnlyCollection<StockExpiryAlertLineResponse> Items,
    int TotalCount,
    int ExpiredCount,
    int NearExpiryCount,
    int SkuCount,
    int Page,
    int PageSize);

public sealed record StockCountScope(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId);

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
    int? ShelfLifeDays,
    string? ExpiryDateSource,
    int DaysUntilExpiry,
    bool IsExpired,
    bool IsNearExpiry,
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
    decimal AvailableQuantity);

public sealed class ListStockExpiryAlertsQueryValidator : AbstractValidator<ListStockExpiryAlertsQuery>
{
    public const int MaxPage = 10_000_000;

    public ListStockExpiryAlertsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.SiteCode).RequiredInventoryCode(100);
        RuleFor(x => x.SkuCode).OptionalInventoryCode(100);
        RuleFor(x => x.LocationCode).OptionalInventoryCode(100);
        RuleFor(x => x.NearExpiryThresholdDays).GreaterThanOrEqualTo(0).LessThanOrEqualTo(3660).When(x => x.NearExpiryThresholdDays is not null);
        RuleFor(x => x.Page).InclusiveBetween(1, MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class ListStockExpiryAlertsQueryHandler(
    ApplicationDbContext dbContext,
    IInventorySkuExpiryPolicyProvider? skuExpiryPolicyProvider = null)
    : IQueryHandler<ListStockExpiryAlertsQuery, StockExpiryAlertsResponse>
{
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

        var totalCount = await query.CountAsync(cancellationToken);
        var expiredCount = await query.CountAsync(x => x.ExpiryDate < asOfDate, cancellationToken);
        var nearExpiryCount = await query.CountAsync(x => x.ExpiryDate >= asOfDate, cancellationToken);
        var skuCount = await query.Select(x => x.SkuCode).Distinct().CountAsync(cancellationToken);
        var ledgers = await query
            .OrderBy(x => x.ExpiryDate)
            .ThenBy(x => x.SkuCode)
            .ThenBy(x => x.LocationCode)
            .ThenBy(x => x.LotNo)
            .ThenBy(x => x.SerialNo)
            .ThenBy(x => x.QualityStatus)
            .ThenBy(x => x.OwnerType)
            .ThenBy(x => x.OwnerId)
            .ThenBy(x => x.ProductionDate)
            .ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var pageCountScopes = ledgers.Select(ToCountScope).Distinct().ToList();
        var ambiguousCountScopes = new HashSet<StockCountScope>();
        if (pageCountScopes.Count > 0)
        {
            var ambiguousRows = await RestrictToCountScopes(
                    dbContext.StockLedgers.AsNoTracking(),
                    pageCountScopes)
                .GroupBy(x => new
                {
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.SkuCode,
                    x.UomCode,
                    x.SiteCode,
                    x.LocationCode,
                    x.LotNo,
                    x.SerialNo,
                    x.QualityStatus,
                    x.OwnerType,
                    x.OwnerId,
                })
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToListAsync(cancellationToken);
            ambiguousCountScopes = ambiguousRows
                .Select(x => new StockCountScope(
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.SkuCode,
                    x.UomCode,
                    x.SiteCode,
                    x.LocationCode,
                    x.LotNo,
                    x.SerialNo,
                    x.QualityStatus,
                    x.OwnerType,
                    x.OwnerId))
                .ToHashSet();
        }

        return new StockExpiryAlertsResponse(ledgers.Select(x =>
        {
            var expiryDate = x.ExpiryDate!.Value;
            var daysUntilExpiry = expiryDate.DayNumber - asOfDate.DayNumber;
            var countScopeAmbiguous = ambiguousCountScopes.Contains(ToCountScope(x));
            var operation = StockOperationAvailabilityProjection.From(
                x.QualityStatus,
                x.ExpiryDate,
                x.IsFrozenForCount,
                asOfDate,
                countScopeAmbiguous);
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
                x.ShelfLifeDays,
                x.ExpiryDateSource,
                daysUntilExpiry,
                daysUntilExpiry < 0,
                daysUntilExpiry >= 0 && daysUntilExpiry <= nearExpiryThresholdDays,
                operation.IsBlocked,
                operation.BlockReasonCode,
                operation.BlockReason,
                operation.MovementAllowed,
                operation.MovementBlockReasonCode,
                operation.MovementBlockReason,
                operation.CountAllowed,
                operation.CountBlockReasonCode,
                operation.CountBlockReason,
                x.OnHandQuantity,
                x.ReservedQuantity,
                x.AvailableQuantity);
        }).ToList(), totalCount, expiredCount, nearExpiryCount, skuCount, request.Page, request.PageSize);
    }

    public static IQueryable<StockLedger> RestrictToCountScopes(
        IQueryable<StockLedger> query,
        IReadOnlyCollection<StockCountScope> scopes)
    {
        var ledger = Expression.Parameter(typeof(StockLedger), "ledger");
        Expression body = Expression.Constant(false);
        foreach (var scope in scopes)
        {
            Expression scopeBody = Expression.Constant(true);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.OrganizationId), scope.OrganizationId);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.EnvironmentId), scope.EnvironmentId);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.SkuCode), scope.SkuCode);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.UomCode), scope.UomCode);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.SiteCode), scope.SiteCode);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.LocationCode), scope.LocationCode);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.LotNo), scope.LotNo);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.SerialNo), scope.SerialNo);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.QualityStatus), scope.QualityStatus);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.OwnerType), scope.OwnerType);
            scopeBody = AndEqual(scopeBody, ledger, nameof(StockLedger.OwnerId), scope.OwnerId);
            body = Expression.OrElse(body, scopeBody);
        }

        return query.Where(Expression.Lambda<Func<StockLedger, bool>>(body, ledger));
    }

    private static Expression AndEqual(
        Expression body,
        ParameterExpression ledger,
        string propertyName,
        object? value)
    {
        var property = Expression.Property(ledger, propertyName);
        return Expression.AndAlso(body, Expression.Equal(property, Expression.Constant(value, property.Type)));
    }

    private static StockCountScope ToCountScope(StockLedger ledger) => new(
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
        ledger.OwnerId);

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
