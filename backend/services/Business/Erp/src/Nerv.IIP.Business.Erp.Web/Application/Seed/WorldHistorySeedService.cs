using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》L1 背景历史引擎的 **ERP 侧**：2026-01-05 上线至今约 29 周的销售与采购全链历史。
///
/// 产出（设定集 §7）：约 3200 张销售订单（<c>SO-2026-#####</c>），状态分布
/// 已收款结案 78% / 已发货待收款 8% / 在制 9% / 已下达待开工 3% / 废弃 2%；
/// 结案单带发货单、应收与收入/收款两张平衡凭证；另有约 480 张采购订单与原料收货。
///
/// 设定集 §0 的 MAN-519 基线修订条款允许 L1 号段直写结果事实，前提是
/// ①历史时间戳 ②独立号段 ③一致性校验器 ④讲稿如实定位。本服务四条都满足：
/// - 时间戳：聚合构造函数写死的 <c>DateTime.UtcNow</c> 在入库前经变更跟踪器改写为历史时刻；
/// - 号段：<c>SO/QUO/DO/AR/CR/JV/PO/PR-2026-*</c>，与 <c>*-DEMO-*</c>、<c>*-SCALE-*</c> 完全隔离；
/// - 校验器：<see cref="WorldHistoryConsistencyValidator"/> 在 seed 末尾 fail-closed 运行；
/// - 幂等：按单据号预查跳过，重跑不重复写。
///
/// 批量写入走 <c>SaveChangesAsync</c>（不派发领域事件），避免三千单级 seed 触发下游事件风暴。
/// </summary>
public sealed class WorldHistorySeedService(ApplicationDbContext dbContext)
{
    /// <summary>每批订单数。批内共享一次预查与一次 <c>SaveChanges</c>，批末清变更跟踪器。</summary>
    public const int BatchSize = 100;

    /// <summary>历史订单不做授信拦截：授信额度是 L0 未覆盖的主数据，这里给足以免误判 credit-held。</summary>
    private const decimal CreditLimit = 1_000_000_000m;

    private const string DefaultLocationCode = "FG-01";

    public async Task<WorldHistorySeedReport> SeedAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        await SeedGlAccountsAsync(organizationId, environmentId, cancellationToken);
        var salesOrdersWritten = await SeedSalesHistoryAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);
        var purchaseOrdersWritten = await SeedPurchaseHistoryAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        // fail-closed：对账不过 seed 就失败，绝不放一份账不平的历史进演示环境。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistorySeedReport(salesOrdersWritten, purchaseOrdersWritten, validation);
    }

    private async Task SeedGlAccountsAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var codes = WorldHistoryErpSpec.GlAccounts.Select(x => x.Code).ToArray();
        var existing = await dbContext.GLAccounts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && codes.Contains(x.Code))
            .Select(x => x.Code)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        var added = false;
        foreach (var account in WorldHistoryErpSpec.GlAccounts.Where(x => !existingSet.Contains(x.Code)))
        {
            dbContext.GLAccounts.Add(GLAccount.Create(organizationId, environmentId, account.Code, account.Name, account.Type));
            added = true;
        }

        if (added)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
    }

    private async Task<int> SeedSalesHistoryAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var plans = WorldHistorySpec.BuildOrderPlans(asOfDate, scale);
        var written = 0;

        for (var batchStart = 0; batchStart < plans.Count; batchStart += BatchSize)
        {
            var batch = plans.Skip(batchStart).Take(BatchSize).ToArray();
            var salesOrderNos = batch.Select(x => x.SalesOrderNo).ToArray();
            var existing = await dbContext.SalesOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    salesOrderNos.Contains(x.SalesOrderNo))
                .Select(x => x.SalesOrderNo)
                .ToArrayAsync(cancellationToken);
            var existingSet = existing.ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var plan in batch.Where(plan => !existingSet.Contains(plan.SalesOrderNo)))
            {
                WriteOrderChain(organizationId, environmentId, plan, asOfDate);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                written += added;
            }

            dbContext.ChangeTracker.Clear();
        }

        return written;
    }

    /// <summary>写一张历史订单的完整链路：报价 → 订单 →（发货 → 应收 → 收入凭证 →（收款 → 收款凭证））。</summary>
    private void WriteOrderChain(
        string organizationId,
        string environmentId,
        WorldHistoryOrderPlan plan,
        DateOnly asOfDate)
    {
        var timeline = WorldHistoryTimeline.For(plan, asOfDate);
        var quotation = CreateBackdatedQuotation(organizationId, environmentId, plan, timeline, asOfDate);
        var salesOrder = SalesOrder.CreateFromQuotation(
            plan.SalesOrderNo,
            WorldHistorySpec.SiteCode,
            quotation,
            new CustomerCreditSnapshot(plan.CustomerCode, CreditLimit, 0m, 0m));
        dbContext.SalesOrders.Add(salesOrder);

        var orderCreatedAtUtc = MomentOn(timeline.OrderDate, plan.SalesOrderNo, "order");
        Backdate(salesOrder, x => x.CreatedAtUtc, orderCreatedAtUtc);

        if (plan.Stage == WorldHistoryOrderStage.Cancelled)
        {
            salesOrder.Cancel("客户取消订单");
            foreach (var change in salesOrder.ChangeHistory)
            {
                Backdate(change, x => x.ChangedAtUtc, MomentOn(timeline.WorkOrderReleaseDate, plan.SalesOrderNo, "cancel"));
            }

            return;
        }

        if (!plan.HasDelivery)
        {
            return;
        }

        WriteDeliveryAndFinance(organizationId, environmentId, plan, timeline);
    }

    private Quotation CreateBackdatedQuotation(
        string organizationId,
        string environmentId,
        WorldHistoryOrderPlan plan,
        WorldHistoryTimeline timeline,
        DateOnly asOfDate)
    {
        // Quotation.EnsureCanCreateSalesOrder 拿**真实今天**比对有效期，历史报价单必须先以未来有效期
        // 通过不变量，再在入库前改写回历史值（下单当日 +30 天），否则一张 1 月的报价单无法开出订单。
        var realToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var provisionalExpiry = (asOfDate > realToday ? asOfDate : realToday).AddDays(1);

        var quotation = Quotation.Create(
            organizationId,
            environmentId,
            plan.QuotationNo,
            plan.CustomerCode,
            provisionalExpiry,
            [
                new QuotationLineDraft(
                    "10",
                    plan.SkuCode,
                    WorldHistorySpec.UomCode,
                    plan.Quantity,
                    plan.UnitPrice,
                    plan.RequiredDate)
            ]);
        quotation.Approve();
        dbContext.Quotations.Add(quotation);

        Backdate(quotation, x => x.CreatedAtUtc, MomentOn(timeline.OrderDate.AddDays(-1), plan.QuotationNo, "quotation"));
        Backdate(quotation, x => x.ExpiresOn, timeline.OrderDate.AddDays(30));
        return quotation;
    }

    private void WriteDeliveryAndFinance(
        string organizationId,
        string environmentId,
        WorldHistoryOrderPlan plan,
        WorldHistoryTimeline timeline)
    {
        var salesOrder = dbContext.SalesOrders.Local.Single(x => x.SalesOrderNo == plan.SalesOrderNo);
        var shippedAtUtc = MomentOn(timeline.ShipDate, plan.SalesOrderNo, "ship");

        var deliveryOrder = DeliveryOrder.Release(
            salesOrder,
            WorldHistorySpec.DeliveryOrderNo(plan.Index),
            [new DeliveryOrderLineDraft("10", plan.Quantity, DefaultLocationCode, $"LOT-{plan.WorkOrderNo}")]);
        deliveryOrder.ApplyShipment([new DeliveryOrderShipmentLine("10", plan.Quantity)], shippedAtUtc.UtcDateTime);
        dbContext.DeliveryOrders.Add(deliveryOrder);
        Backdate(deliveryOrder, x => x.ReleasedAtUtc, MomentOn(timeline.ShipDate, plan.SalesOrderNo, "delivery").UtcDateTime);

        var receivable = AccountReceivable.Create(
            organizationId,
            environmentId,
            WorldHistorySpec.ReceivableNo(plan.Index),
            plan.SalesOrderNo,
            plan.CustomerCode,
            plan.TotalAmount,
            WorldHistorySpec.CurrencyCode,
            invoiceDate: timeline.ShipDate,
            dueDate: timeline.ShipDate.AddDays(30));
        dbContext.AccountReceivables.Add(receivable);
        Backdate(receivable, x => x.CreatedAtUtc, shippedAtUtc.UtcDateTime);

        // 收入确认凭证：借 应收账款 / 贷 主营业务收入，金额与订单总额逐分一致。
        var revenueVoucher = JournalVoucher.Post(
            organizationId,
            environmentId,
            WorldHistorySpec.RevenueVoucherNo(plan.Index),
            timeline.ShipDate,
            [
                new JournalVoucherLineDraft(
                    WorldHistoryErpSpec.ReceivableAccountCode, plan.TotalAmount, 0m, $"{plan.SalesOrderNo} 发货确认收入"),
                new JournalVoucherLineDraft(
                    WorldHistoryErpSpec.RevenueAccountCode, 0m, plan.TotalAmount, $"{plan.SalesOrderNo} 发货确认收入"),
            ]);
        dbContext.JournalVouchers.Add(revenueVoucher);
        Backdate(revenueVoucher, x => x.PostedAtUtc, shippedAtUtc.UtcDateTime);

        if (!plan.IsCollected)
        {
            return;
        }

        var collectedAtUtc = MomentOn(timeline.CollectionDate, plan.SalesOrderNo, "collect");
        receivable.RegisterCollection(plan.TotalAmount);

        var cashReceipt = CashReceipt.Register(
            organizationId,
            environmentId,
            WorldHistorySpec.CashReceiptNo(plan.Index),
            plan.CustomerCode,
            plan.TotalAmount,
            WorldHistorySpec.CurrencyCode,
            timeline.CollectionDate,
            WorldHistoryErpSpec.BankAccountCode,
            [new CashReceiptAllocationDraft(WorldHistorySpec.ReceivableNo(plan.Index), plan.TotalAmount)]);
        cashReceipt.Match();
        dbContext.CashReceipts.Add(cashReceipt);
        Backdate(cashReceipt, x => x.RegisteredAtUtc, collectedAtUtc.UtcDateTime);
        Backdate(cashReceipt, x => x.MatchedAtUtc, collectedAtUtc.UtcDateTime);

        // 收款凭证：借 银行存款 / 贷 应收账款。
        var collectionVoucher = JournalVoucher.Post(
            organizationId,
            environmentId,
            WorldHistorySpec.CollectionVoucherNo(plan.Index),
            timeline.CollectionDate,
            [
                new JournalVoucherLineDraft(
                    WorldHistoryErpSpec.BankAccountCode, plan.TotalAmount, 0m, $"{plan.SalesOrderNo} 货款回收"),
                new JournalVoucherLineDraft(
                    WorldHistoryErpSpec.ReceivableAccountCode, 0m, plan.TotalAmount, $"{plan.SalesOrderNo} 货款回收"),
            ]);
        dbContext.JournalVouchers.Add(collectionVoucher);
        Backdate(collectionVoucher, x => x.PostedAtUtc, collectedAtUtc.UtcDateTime);
    }

    private async Task<int> SeedPurchaseHistoryAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var plans = BuildPurchasePlans(asOfDate, scale);
        var written = 0;

        for (var batchStart = 0; batchStart < plans.Count; batchStart += BatchSize)
        {
            var batch = plans.Skip(batchStart).Take(BatchSize).ToArray();
            var purchaseOrderNos = batch.Select(x => x.PurchaseOrderNo).ToArray();
            var existing = await dbContext.PurchaseOrders
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    purchaseOrderNos.Contains(x.PurchaseOrderNo))
                .Select(x => x.PurchaseOrderNo)
                .ToArrayAsync(cancellationToken);
            var existingSet = existing.ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var plan in batch.Where(plan => !existingSet.Contains(plan.PurchaseOrderNo)))
            {
                WritePurchaseChain(organizationId, environmentId, plan);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                written += added;
            }

            dbContext.ChangeTracker.Clear();
        }

        return written;
    }

    private static IReadOnlyList<WorldHistoryPurchasePlan> BuildPurchasePlans(DateOnly asOfDate, double scale)
    {
        var plans = new List<WorldHistoryPurchasePlan>();
        var weeks = WorldHistoryCalendar.WeekCount(asOfDate);
        var index = 0;
        for (var week = 0; week < weeks; week++)
        {
            var volume = WorldHistoryErpSpec.WeeklyPurchaseOrderVolume(week, scale);
            var weekStart = WorldHistoryCalendar.WeekStart(week);
            for (var slot = 0; slot < volume; slot++)
            {
                index++;
                var candidate = weekStart.AddDays(Math.Min(slot * 6 / Math.Max(volume, 1), 5));
                var orderDate = candidate > asOfDate ? asOfDate : candidate;
                plans.Add(WorldHistoryErpSpec.BuildPurchasePlan(index, orderDate, asOfDate));
            }
        }

        return plans;
    }

    private void WritePurchaseChain(string organizationId, string environmentId, WorldHistoryPurchasePlan plan)
    {
        var approvalChainId = $"seed:world-history:{plan.PurchaseOrderNo}";
        var purchaseOrder = PurchaseOrder.Create(
            organizationId,
            environmentId,
            plan.PurchaseOrderNo,
            plan.SupplierCode,
            WorldHistorySpec.SiteCode,
            WorldHistorySpec.CurrencyCode,
            [
                new PurchaseOrderLineDraft(
                    "10",
                    plan.SkuCode,
                    plan.UomCode,
                    plan.Quantity,
                    plan.UnitPrice,
                    plan.PromisedDate)
            ]);

        // 采购单没有直达 Released 的路径，必须走审批链的两步（设定集只要求「已下达」这一结果态）。
        purchaseOrder.MarkApprovalRequested(approvalChainId);
        purchaseOrder.ReleaseAfterApproval(approvalChainId);
        dbContext.PurchaseOrders.Add(purchaseOrder);
        Backdate(purchaseOrder, x => x.CreatedAtUtc, MomentOn(plan.OrderDate, plan.PurchaseOrderNo, "purchase").UtcDateTime);

        if (!plan.IsReceived)
        {
            return;
        }

        var receipt = PurchaseReceipt.Record(
            purchaseOrder,
            plan.PurchaseReceiptNo,
            [
                new PurchaseReceiptLineDraft(
                    "10",
                    plan.Quantity,
                    "passed",
                    LocationCode: "RM-01",
                    LotNo: $"LOT-{plan.PurchaseOrderNo}",
                    FinalDelivery: true)
            ]);
        dbContext.PurchaseReceipts.Add(receipt);
        Backdate(receipt, x => x.RecordedAtUtc, MomentOn(plan.ReceiptDate, plan.PurchaseReceiptNo, "receipt").UtcDateTime);
    }

    /// <summary>
    /// 把聚合构造函数写死的 <c>DateTime.UtcNow</c> 改写为历史时刻。
    ///
    /// ERP 的所有时间戳都是 <c>{ get; private set; }</c> 且构造函数内取 <c>UtcNow</c>，
    /// 领域 API 不提供任何回填入口。这里用 EF Core 变更跟踪器改写待插入行的列值——
    /// 是 EF 的一等公民 API，不是裸 SQL，且只作用于本 seed 新建的实体。
    /// </summary>
    private void Backdate<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value;
    }

    /// <summary>
    /// 给定工作日与流键，落到该日某个班次内的确定性时刻（UTC）。
    /// 单据时间因此总是落在早班或中班窗口内，不会出现「凌晨 3 点报工」这类穿帮。
    /// </summary>
    private static DateTimeOffset MomentOn(DateOnly date, string streamKey, string purpose)
    {
        var workingDay = WorldHistoryCalendar.SnapToWorkingDay(date);
        var random = new WorldHistoryRandom($"{purpose}:{streamKey}");
        var shiftIndex = random.NextInt(0, 2);
        var minutesIntoShift = random.NextInt(0, WorldHistoryCalendar.ShiftLengthHours * 60);
        return WorldHistoryCalendar.ShiftMoment(workingDay, shiftIndex, minutesIntoShift);
    }
}

/// <summary>一次 L1 ERP 历史生成的产出摘要（写入量 + 校验器结论）。</summary>
public sealed record WorldHistorySeedReport(
    int SalesOrdersWritten,
    int PurchaseOrdersWritten,
    WorldHistoryValidationReport Validation);
