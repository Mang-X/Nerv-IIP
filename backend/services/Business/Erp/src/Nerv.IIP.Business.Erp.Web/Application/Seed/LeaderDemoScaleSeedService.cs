using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>
/// MAN-519 白名单内的领导演示「规模块」ERP 前置事实：批量已审报价单 + 已下达销售订单。
/// 只写销售侧前置事实，不产生发货、应收、成本或任何结果事实；使用独立 <c>SO-SCALE-#####</c>
/// 号段，绝不触碰 <c>SO-DEMO-001</c>/<c>QUO-DEMO-001</c> 等固定演示事实。
/// 批量写入走 <c>SaveChangesAsync</c>（不派发领域事件），避免千单级 seed 触发下游事件风暴；
/// 固定演示事实仍保留原有 <c>SaveEntitiesAsync</c> 事件路径。
/// </summary>
public sealed class LeaderDemoScaleSeedService(ApplicationDbContext dbContext)
{
    public const int BatchSize = 100;
    private const decimal CreditLimit = 100_000_000m;

    /// <summary>
    /// 规模池订单往前压多少天。世界观历史最早的订单大约在 asOfDate 前一年内，压到 720 天
    /// 之前可以保证规模单整体沉到销售订单列表末尾，同时仍能被搜索与下钻找到。
    /// </summary>
    private const int ScaleBackdateDays = 720;

    /// <summary>
    /// 规模单的创建时间：统一压到世界观历史之前，并按序号错开分钟，避免整批同一时刻
    /// 导致倒序分页在边界上抖动（同值排序不稳定，翻页会重复或漏行）。
    /// </summary>
    private static DateTimeOffset ScaleCreatedAtUtc(DateTimeOffset nowUtc, int index) =>
        nowUtc.AddDays(-ScaleBackdateDays).AddMinutes(index);

    private void BackdateUtc<TEntity>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, DateTime>> property,
        DateTimeOffset value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value.UtcDateTime;
    }

    public async Task SeedAsync(
        string organizationId,
        string environmentId,
        int orderCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (orderCount <= 0)
        {
            return;
        }

        var anchor = DateOnly.FromDateTime(nowUtc.UtcDateTime.Date);
        var expiresOn = anchor.AddDays(365);

        for (var batchStart = 1; batchStart <= orderCount; batchStart += BatchSize)
        {
            var batchEnd = Math.Min(batchStart + BatchSize - 1, orderCount);
            var salesOrderNos = Enumerable.Range(batchStart, batchEnd - batchStart + 1)
                .Select(LeaderDemoScaleSpec.SalesOrderNo)
                .ToArray();
            var existing = await dbContext.SalesOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    salesOrderNos.Contains(x.SalesOrderNo))
                .Select(x => x.SalesOrderNo)
                .ToArrayAsync(cancellationToken);
            var existingSet = existing.ToHashSet(StringComparer.Ordinal);

            var added = 0;
            for (var index = batchStart; index <= batchEnd; index++)
            {
                if (existingSet.Contains(LeaderDemoScaleSpec.SalesOrderNo(index)))
                {
                    continue;
                }

                var quotation = Quotation.Create(
                    organizationId,
                    environmentId,
                    LeaderDemoScaleSpec.QuotationNo(index),
                    LeaderDemoScaleSpec.CustomerCode(index),
                    expiresOn,
                    [
                        new QuotationLineDraft(
                            "10",
                            LeaderDemoScaleSpec.SkuCode(index),
                            "pcs",
                            LeaderDemoScaleSpec.Quantity(index),
                            LeaderDemoScaleSpec.UnitPrice(index),
                            anchor.AddDays(LeaderDemoScaleSpec.DueDayOffset(index)))
                    ]);
                quotation.Approve();
                dbContext.Quotations.Add(quotation);
                var scaleSalesOrder = SalesOrder.CreateFromQuotation(
                    LeaderDemoScaleSpec.SalesOrderNo(index),
                    LeaderDemoScaleSpec.SiteCode,
                    quotation,
                    new CustomerCreditSnapshot(LeaderDemoScaleSpec.CustomerCode(index), CreditLimit, 0m, 0m));
                dbContext.SalesOrders.Add(scaleSalesOrder);
                // 规模池只是排产纵深的填充料，不是演示故事的一部分：创建时间必须压到世界观
                // 历史之前，否则销售订单读面按 `CreatedAtUtc` 倒序，**首屏全是 SO-SCALE-***
                // ——领导第一眼看到的就是一批本不该点开的填充单（第五轮走查 owner 亲验点名）。
                // 交期仍锚今天（上面 anchor + DueDayOffset），排产演示要的正是未来交期。
                BackdateUtc(scaleSalesOrder, x => x.CreatedAtUtc, ScaleCreatedAtUtc(nowUtc, index));
                BackdateUtc(quotation, x => x.CreatedAtUtc, ScaleCreatedAtUtc(nowUtc, index));
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            dbContext.ChangeTracker.Clear();
        }
    }
}
