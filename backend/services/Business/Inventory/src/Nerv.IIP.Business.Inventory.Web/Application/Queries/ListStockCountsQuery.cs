using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;

namespace Nerv.IIP.Business.Inventory.Web.Application.Queries;

/// <summary>
/// 盘点任务读面。
///
/// 此前库存域只有「建任务 / 确认差异 / 取消」三个写端点，没有任何列表查询，业务前端
/// 「库存盘点」页的表格只能挂在会话内本地队列上——刷新即空。本查询补上读面：
/// 按组织 / 环境收敛，可按状态、物料、工厂、库位、任务号过滤，服务端分页，
/// 并回传**不受状态过滤影响**的状态分布计数（页面的状态页签靠它显示总量）。
/// </summary>
public sealed record ListStockCountTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? SkuCode = null,
    string? SiteCode = null,
    string? LocationCode = null,
    string? CountTaskCode = null,
    int Page = 1,
    int PageSize = 50) : IQuery<StockCountTaskListResponse>;

public sealed record StockCountTaskListResponse(
    IReadOnlyCollection<StockCountTaskLineResponse> Items,
    int TotalCount,
    int OpenCount,
    int PendingApprovalCount,
    int ConfirmedCount,
    int RecountRequiredCount,
    int CancelledCount,
    int Page,
    int PageSize);

public sealed record StockCountTaskLineResponse(
    string CountTaskId,
    string CountTaskCode,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    long ExpectedLedgerVersion,
    decimal? CountedQuantity,
    decimal? VarianceQuantity,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class ListStockCountTasksQueryValidator : AbstractValidator<ListStockCountTasksQuery>
{
    public ListStockCountTasksQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.Status).OptionalInventoryCode(50);
        RuleFor(x => x.SkuCode).OptionalInventoryCode(100);
        RuleFor(x => x.SiteCode).OptionalInventoryCode(100);
        RuleFor(x => x.LocationCode).OptionalInventoryCode(100);
        RuleFor(x => x.CountTaskCode).OptionalInventoryCode(150);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, StockCountQueryLimits.MaxPageSize);
    }
}

public static class StockCountQueryLimits
{
    public const int MaxPageSize = 200;
}

public sealed class ListStockCountTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListStockCountTasksQuery, StockCountTaskListResponse>
{
    public async Task<StockCountTaskListResponse> Handle(
        ListStockCountTasksQuery request,
        CancellationToken cancellationToken)
    {
        var status = Normalize(request.Status);
        var skuCode = Normalize(request.SkuCode);
        var siteCode = Normalize(request.SiteCode);
        var locationCode = Normalize(request.LocationCode);
        var countTaskCode = Normalize(request.CountTaskCode);

        // 状态分布必须在「除状态外的同一过滤集」上算，否则点开某个状态页签后计数会自我坍缩。
        var scoped = dbContext.StockCountTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (skuCode is not null)
        {
            scoped = scoped.Where(x => x.SkuCode == skuCode);
        }

        if (siteCode is not null)
        {
            scoped = scoped.Where(x => x.SiteCode == siteCode);
        }

        if (locationCode is not null)
        {
            scoped = scoped.Where(x => x.LocationCode == locationCode);
        }

        if (countTaskCode is not null)
        {
            scoped = scoped.Where(x => x.CountTaskCode == countTaskCode);
        }

        var statusCounts = await scoped
            .GroupBy(x => x.Status)
            .Select(group => new StockCountStatusSummary(group.Key, group.Count()))
            .ToArrayAsync(cancellationToken);

        var filtered = status is null ? scoped : scoped.Where(x => x.Status == status);
        var totalCount = status is null
            ? statusCounts.Sum(x => x.Count)
            : await filtered.CountAsync(cancellationToken);

        var items = await filtered
            // 次序键取业务码而不是强类型 id：强类型 id 在 InMemory provider 下不可比较，
            // 而分页必须有稳定序，否则翻页会漏行或重行。
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.CountTaskCode)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new StockCountTaskLineResponse(
                x.Id.ToString(),
                x.CountTaskCode,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.SerialNo,
                x.QualityStatus,
                x.OwnerType,
                x.OwnerId,
                x.ExpectedLedgerVersion,
                x.CountedQuantity,
                x.VarianceQuantity,
                x.Status,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new StockCountTaskListResponse(
            items,
            totalCount,
            CountOf(statusCounts, StockCountTaskStatuses.Open),
            CountOf(statusCounts, StockCountTaskStatuses.PendingApproval),
            CountOf(statusCounts, StockCountTaskStatuses.Confirmed),
            CountOf(statusCounts, StockCountTaskStatuses.RecountRequired),
            CountOf(statusCounts, StockCountTaskStatuses.Cancelled),
            request.Page,
            request.PageSize);
    }

    private static int CountOf(IReadOnlyList<StockCountStatusSummary> summaries, string status) =>
        summaries.SingleOrDefault(x => string.Equals(x.Status, status, StringComparison.Ordinal))?.Count ?? 0;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>盘点任务状态分布的中间投影。</summary>
public sealed record StockCountStatusSummary(string Status, int Count);

/// <summary>
/// 盘点调整读面：与盘点任务成对，页面据此展示「差异 → 审批 → 过账」的落地凭据
/// （<c>MovementId</c> / <c>ApprovalChainId</c>）。同样按组织 / 环境收敛并服务端分页。
/// </summary>
public sealed record ListStockCountAdjustmentsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status = null,
    string? CountTaskCode = null,
    string? SkuCode = null,
    int Page = 1,
    int PageSize = 50) : IQuery<StockCountAdjustmentListResponse>;

public sealed record StockCountAdjustmentListResponse(
    IReadOnlyCollection<StockCountAdjustmentLineResponse> Items,
    int TotalCount,
    int PendingApprovalCount,
    int PostedCount,
    int VoidedCount,
    decimal VarianceAmountTotal,
    int Page,
    int PageSize);

public sealed record StockCountAdjustmentLineResponse(
    string AdjustmentId,
    string CountTaskCode,
    string IdempotencyKey,
    string? MovementId,
    string? ApprovalChainId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal CountedQuantity,
    decimal VarianceQuantity,
    decimal VarianceAmount,
    string Status,
    DateTime? ConfirmedAtUtc);

public sealed class ListStockCountAdjustmentsQueryValidator : AbstractValidator<ListStockCountAdjustmentsQuery>
{
    public ListStockCountAdjustmentsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).RequiredInventoryCode(100);
        RuleFor(x => x.EnvironmentId).RequiredInventoryCode(100);
        RuleFor(x => x.Status).OptionalInventoryCode(50);
        RuleFor(x => x.CountTaskCode).OptionalInventoryCode(150);
        RuleFor(x => x.SkuCode).OptionalInventoryCode(100);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, StockCountQueryLimits.MaxPageSize);
    }
}

public sealed class ListStockCountAdjustmentsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListStockCountAdjustmentsQuery, StockCountAdjustmentListResponse>
{
    public async Task<StockCountAdjustmentListResponse> Handle(
        ListStockCountAdjustmentsQuery request,
        CancellationToken cancellationToken)
    {
        var status = Normalize(request.Status);
        var countTaskCode = Normalize(request.CountTaskCode);
        var skuCode = Normalize(request.SkuCode);

        var scoped = dbContext.StockCountAdjustments
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (countTaskCode is not null)
        {
            scoped = scoped.Where(x => x.CountTaskCode == countTaskCode);
        }

        if (skuCode is not null)
        {
            scoped = scoped.Where(x => x.SkuCode == skuCode);
        }

        var summaries = await scoped
            .GroupBy(x => x.Status)
            .Select(group => new StockCountAdjustmentStatusSummary(
                group.Key,
                group.Count(),
                group.Sum(x => x.VarianceAmount)))
            .ToArrayAsync(cancellationToken);

        var filtered = status is null ? scoped : scoped.Where(x => x.Status == status);
        var totalCount = status is null
            ? summaries.Sum(x => x.Count)
            : await filtered.CountAsync(cancellationToken);

        var items = await filtered
            .OrderByDescending(x => x.CountTaskCode)
            .ThenBy(x => x.IdempotencyKey)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new StockCountAdjustmentLineResponse(
                x.Id.ToString(),
                x.CountTaskCode,
                x.IdempotencyKey,
                x.MovementId,
                x.ApprovalChainId,
                x.SkuCode,
                x.UomCode,
                x.SiteCode,
                x.LocationCode,
                x.LotNo,
                x.SerialNo,
                x.QualityStatus,
                x.OwnerType,
                x.OwnerId,
                x.CountedQuantity,
                x.VarianceQuantity,
                x.VarianceAmount,
                x.Status,
                x.ConfirmedAtUtc))
            .ToArrayAsync(cancellationToken);

        return new StockCountAdjustmentListResponse(
            items,
            totalCount,
            CountOf(summaries, StockCountAdjustmentStatuses.PendingApproval),
            CountOf(summaries, StockCountAdjustmentStatuses.Posted),
            CountOf(summaries, StockCountAdjustmentStatuses.Voided),
            summaries.Sum(x => x.VarianceAmountTotal),
            request.Page,
            request.PageSize);
    }

    private static int CountOf(IReadOnlyList<StockCountAdjustmentStatusSummary> summaries, string status) =>
        summaries.SingleOrDefault(x => string.Equals(x.Status, status, StringComparison.Ordinal))?.Count ?? 0;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>状态分布的中间投影（EF 的 <c>GroupBy</c> 投影不能直接用匿名类型跨方法传递）。</summary>
public sealed record StockCountAdjustmentStatusSummary(string Status, int Count, decimal VarianceAmountTotal);
