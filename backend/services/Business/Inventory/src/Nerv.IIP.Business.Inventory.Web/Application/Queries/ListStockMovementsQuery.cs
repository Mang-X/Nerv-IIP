using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Inventory.Web.Application.Queries;

/// <summary>
/// 库存流水读面（全域分页）。
///
/// 既有的 <see cref="GetStockBySourceQuery"/> 只能按**单张源单据**下钻，页面无法「先看再钻」；
/// 业务前端「库存移动」页因此只能展示本次过账结果的会话内队列，刷新即空。本查询补上按
/// 组织 / 环境收敛的历史流水列表：可按物料、库位、批次、移动类型、源服务与过账日期区间过滤，
/// 服务端分页，并回传入 / 出合计供页面页眉直接显示。
/// </summary>
public sealed record ListStockMovementsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode = null,
    string? SiteCode = null,
    string? LocationCode = null,
    string? LotNo = null,
    string? MovementType = null,
    string? SourceService = null,
    string? SourceDocumentId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int Page = 1,
    int PageSize = 50) : IQuery<StockMovementListResponse>;

public sealed record StockMovementListResponse(
    IReadOnlyCollection<StockMovementLineResponse> Items,
    int TotalCount,
    decimal InboundQuantityTotal,
    decimal OutboundQuantityTotal,
    int Page,
    int PageSize);

public sealed record StockMovementLineResponse(
    string MovementId,
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
    decimal? UnitCost,
    decimal? MovementAmount,
    DateTime PostedAtUtc);

public sealed class ListStockMovementsQueryValidator : AbstractValidator<ListStockMovementsQuery>
{
    public ListStockMovementsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.SkuCode).OptionalInventoryCode(100);
        RuleFor(x => x.SiteCode).OptionalInventoryCode(100);
        RuleFor(x => x.LocationCode).OptionalInventoryCode(100);
        RuleFor(x => x.LotNo).OptionalInventoryCode(100);
        RuleFor(x => x.MovementType).OptionalInventoryCode(50);
        RuleFor(x => x.SourceService).OptionalInventoryCode(100);
        RuleFor(x => x.SourceDocumentId).OptionalInventoryCode(150);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, StockCountQueryLimits.MaxPageSize);
        RuleFor(x => x)
            .Must(x => x.FromDate is null || x.ToDate is null || x.FromDate <= x.ToDate)
            .WithMessage("From date must not be later than to date.");
    }
}

public sealed class ListStockMovementsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListStockMovementsQuery, StockMovementListResponse>
{
    public async Task<StockMovementListResponse> Handle(
        ListStockMovementsQuery request,
        CancellationToken cancellationToken)
    {
        var query = dbContext.StockMovements
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        query = ApplyEquality(query, request.SkuCode, (source, value) => source.Where(x => x.SkuCode == value));
        query = ApplyEquality(query, request.SiteCode, (source, value) => source.Where(x => x.SiteCode == value));
        query = ApplyEquality(query, request.LocationCode, (source, value) => source.Where(x => x.LocationCode == value));
        query = ApplyEquality(query, request.LotNo, (source, value) => source.Where(x => x.LotNo == value));
        query = ApplyEquality(query, request.MovementType, (source, value) => source.Where(x => x.MovementType == value));
        query = ApplyEquality(query, request.SourceService, (source, value) => source.Where(x => x.SourceService == value));
        query = ApplyEquality(query, request.SourceDocumentId, (source, value) => source.Where(x => x.SourceDocumentId == value));

        if (request.FromDate is { } fromDate)
        {
            var lowerBound = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(x => x.PostedAtUtc >= lowerBound);
        }

        if (request.ToDate is { } toDate)
        {
            var upperBound = toDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            query = query.Where(x => x.PostedAtUtc <= upperBound);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var inboundTotal = await query.Where(x => x.Quantity > 0m).SumAsync(x => x.Quantity, cancellationToken);
        var outboundTotal = await query.Where(x => x.Quantity < 0m).SumAsync(x => -x.Quantity, cancellationToken);

        var items = await query
            // 次序键取业务码而不是强类型 id：强类型 id 在 InMemory provider 下不可比较，
            // 而分页必须有稳定序，否则翻页会漏行或重行。
            .OrderByDescending(x => x.PostedAtUtc)
            .ThenBy(x => x.SourceDocumentId)
            .ThenBy(x => x.IdempotencyKey)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new StockMovementLineResponse(
                x.Id.ToString(),
                x.MovementType,
                x.SourceService,
                x.SourceDocumentId,
                x.SourceDocumentLineId,
                x.IdempotencyKey,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.SerialNo,
                x.QualityStatus,
                x.OwnerType,
                x.OwnerId,
                x.Quantity,
                x.UnitCost,
                x.MovementAmount,
                x.PostedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new StockMovementListResponse(
            items,
            totalCount,
            inboundTotal,
            outboundTotal,
            request.Page,
            request.PageSize);
    }

    private static IQueryable<TEntity> ApplyEquality<TEntity>(
        IQueryable<TEntity> source,
        string? value,
        Func<IQueryable<TEntity>, string, IQueryable<TEntity>> apply)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null ? source : apply(source, normalized);
    }
}
