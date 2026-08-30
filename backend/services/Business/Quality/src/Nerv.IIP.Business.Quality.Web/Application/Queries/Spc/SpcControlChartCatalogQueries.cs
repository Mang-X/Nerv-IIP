using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.SpcControlChartAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Queries;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.Spc;

/// <summary>SPC 控制图台账一行（一组已锁定的 X-bar/R 控制限）。</summary>
public sealed record SpcControlChartCatalogItemResponse(
    SpcControlChartId SpcControlChartId,
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string CharacteristicCode,
    string WorkCenterId,
    int SubgroupSize,
    decimal CenterLine,
    decimal AverageRange,
    decimal XbarUpperControlLimit,
    decimal XbarLowerControlLimit,
    decimal RangeUpperControlLimit,
    decimal RangeLowerControlLimit,
    bool Locked,
    DateTime? LimitsCalculatedAtUtc,
    DateTime? LockedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ListSpcControlChartsResponse(
    IReadOnlyCollection<SpcControlChartCatalogItemResponse> Items,
    int Total,
    int LockedCount);

/// <summary>
/// SPC 控制图台账读面。
///
/// 与 <see cref="QuerySpcControlChartQuery"/> 的分工：后者按 (SKU, 特性, 工作中心) 现场重算一张图的
/// 数据点与判异，**前提是调用方已经知道要看哪一张**；本查询回答的是「系统里到底立了哪些控制图、
/// 控制限是什么时候锁的」——没有它，控制图管理这件事在界面上完全不可见。
/// </summary>
public sealed record ListSpcControlChartsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? SkuCode = null,
    string? CharacteristicCode = null,
    string? WorkCenterId = null,
    bool? Locked = null,
    string? Keyword = null,
    int Skip = 0,
    int Take = OffsetPage.DefaultTake) : IQuery<ListSpcControlChartsResponse>;

public sealed class ListSpcControlChartsQueryValidator : AbstractValidator<ListSpcControlChartsQuery>
{
    public ListSpcControlChartsQueryValidator()
    {
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        RuleFor(x => x.SkuCode).MaximumLength(100);
        RuleFor(x => x.CharacteristicCode).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).MaximumLength(100);
        this.AddSearchTermRule(x => x.Keyword);
        this.AddOffsetPageRules(x => x.Skip, x => x.Take);
    }
}

public sealed class ListSpcControlChartsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListSpcControlChartsQuery, ListSpcControlChartsResponse>
{
    public async Task<ListSpcControlChartsResponse> Handle(
        ListSpcControlChartsQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var page = OffsetPage.From(request.Skip, request.Take);
        var keyword = SearchTerm.From(request.Keyword);
        var baseQuery = dbContext.SpcControlCharts
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            var skuCode = request.SkuCode.Trim();
            baseQuery = baseQuery.Where(x => x.SkuCode == skuCode);
        }

        if (!string.IsNullOrWhiteSpace(request.CharacteristicCode))
        {
            var characteristicCode = request.CharacteristicCode.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(x => x.CharacteristicCode == characteristicCode);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId))
        {
            var workCenterId = request.WorkCenterId.Trim();
            baseQuery = baseQuery.Where(x => x.WorkCenterId == workCenterId);
        }

        if (keyword.Value is not null)
        {
            baseQuery = baseQuery.Where(x =>
                x.SkuCode.ToLower().Contains(keyword.Value)
                || x.CharacteristicCode.ToLower().Contains(keyword.Value)
                || x.WorkCenterId.ToLower().Contains(keyword.Value));
        }

        var lockedCount = await baseQuery.CountAsync(x => x.Locked, cancellationToken);

        var filtered = request.Locked is { } locked ? baseQuery.Where(x => x.Locked == locked) : baseQuery;
        var total = await filtered.CountAsync(cancellationToken);
        var items = await filtered
            .OrderBy(x => x.SkuCode)
            .ThenBy(x => x.CharacteristicCode)
            .ThenBy(x => x.WorkCenterId)
            .Skip(page.Skip)
            .Take(page.Take)
            .Select(x => new SpcControlChartCatalogItemResponse(
                x.Id,
                x.OrganizationId,
                x.EnvironmentId,
                x.SkuCode,
                x.CharacteristicCode,
                x.WorkCenterId,
                x.SubgroupSize,
                x.CenterLine,
                x.AverageRange,
                x.XbarUpperControlLimit,
                x.XbarLowerControlLimit,
                x.RangeUpperControlLimit,
                x.RangeLowerControlLimit,
                x.Locked,
                x.LimitsCalculatedAtUtc,
                x.LockedAtUtc,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return new ListSpcControlChartsResponse(items, total, lockedCount);
    }
}
