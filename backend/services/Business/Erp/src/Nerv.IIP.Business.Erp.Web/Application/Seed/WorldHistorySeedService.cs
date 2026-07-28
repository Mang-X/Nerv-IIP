using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
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
        var payablesWritten = await SeedPayablesAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        // fail-closed：对账不过 seed 就失败，绝不放一份账不平的历史进演示环境。
        var validation = await new WorldHistoryConsistencyValidator(dbContext)
            .ValidateAsync(organizationId, environmentId, asOfDate, scale, cancellationToken);

        return new WorldHistorySeedReport(salesOrdersWritten, purchaseOrdersWritten, payablesWritten, validation);
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
        BackdateUtc(salesOrder, x => x.CreatedAtUtc, orderCreatedAtUtc);

        // 大客户框架/新平台意向：每 40 单前置一个销售机会（销售机会页的历史故事）。
        if (plan.Index % WorldHistoryErpSpec.OpportunityEveryNthSalesOrder == 1)
        {
            var opportunityDay = DayBefore(timeline.OrderDate, 10);
            var opportunity = Opportunity.Open(
                organizationId,
                environmentId,
                WorldHistoryErpSpec.OpportunityNo(plan.Index),
                plan.CustomerCode,
                $"「{plan.SkuCode}」批量供货框架意向");
            dbContext.Opportunities.Add(opportunity);
            BackdateUtc(opportunity, x => x.OpenedAtUtc, MomentOn(opportunityDay, plan.SalesOrderNo, "opportunity"));
        }

        // 订单已开出，现在才把报价单有效期改回历史值（下单当日 +30 天）。
        BackdateValue(quotation, x => x.ExpiresOn, timeline.OrderDate.AddDays(30));

        if (plan.Stage == WorldHistoryOrderStage.Cancelled)
        {
            salesOrder.Cancel("客户取消订单");
            foreach (var change in salesOrder.ChangeHistory)
            {
                BackdateUtc(change, x => x.ChangedAtUtc, MomentOn(timeline.WorkOrderReleaseDate, plan.SalesOrderNo, "cancel"));
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

        BackdateUtc(quotation, x => x.CreatedAtUtc, MomentOn(timeline.OrderDate.AddDays(-1), plan.QuotationNo, "quotation"));

        // ExpiresOn 不在这里改：变更跟踪器会把新值写回实体本身，历史有效期一旦提前生效，
        // 紧接着的 SalesOrder.CreateFromQuotation 就会以「报价单已过期」拒绝开单。
        // 改写延后到订单建好之后（见 WriteOrderChain）。
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
        BackdateUtc(deliveryOrder, x => x.ReleasedAtUtc, MomentOn(timeline.ShipDate, plan.SalesOrderNo, "delivery"));

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
        BackdateUtc(receivable, x => x.CreatedAtUtc, shippedAtUtc);

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
        BackdateUtc(revenueVoucher, x => x.PostedAtUtc, shippedAtUtc);

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
        BackdateUtc(cashReceipt, x => x.RegisteredAtUtc, collectedAtUtc);
        BackdateNullableUtc(cashReceipt, x => x.MatchedAtUtc, collectedAtUtc);

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
        BackdateUtc(collectionVoucher, x => x.PostedAtUtc, collectedAtUtc);
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

            // 后种日期重种时，上一轮的「在途申请」号会进入本轮已转化区间：加载后就地转化，不重号。
            var requisitionNos = batch.Select(x => WorldHistoryErpSpec.PurchaseRequisitionNo(x.Index)).ToArray();
            var existingRequisitions = await dbContext.PurchaseRequisitions
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                    requisitionNos.Contains(x.RequisitionNo))
                .ToDictionaryAsync(x => x.RequisitionNo, StringComparer.Ordinal, cancellationToken);

            var added = 0;
            foreach (var plan in batch.Where(plan => !existingSet.Contains(plan.PurchaseOrderNo)))
            {
                WritePurchaseChain(organizationId, environmentId, plan, existingRequisitions);
                added++;
            }

            if (added > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                written += added;
            }

            dbContext.ChangeTracker.Clear();
        }

        await SeedOpenRequisitionsAsync(organizationId, environmentId, asOfDate, plans.Count, cancellationToken);

        return written;
    }

    /// <summary>
    /// 未转化的在途采购申请（采购申请页的「待处理」故事）：编号接在已转化段之后，
    /// 需求日在 asOfDate 之后的近未来，状态保持 Open。
    /// </summary>
    private async Task SeedOpenRequisitionsAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        int totalPurchaseOrders,
        CancellationToken cancellationToken)
    {
        var openCount = WorldHistoryErpSpec.OpenRequisitionCount(totalPurchaseOrders);
        var requisitionNos = Enumerable.Range(1, openCount)
            .Select(offset => WorldHistoryErpSpec.PurchaseRequisitionNo(totalPurchaseOrders + offset))
            .ToArray();
        var existing = await dbContext.PurchaseRequisitions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                requisitionNos.Contains(x.RequisitionNo))
            .Select(x => x.RequisitionNo)
            .ToArrayAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        var added = false;
        for (var offset = 1; offset <= openCount; offset++)
        {
            var index = totalPurchaseOrders + offset;
            var requisitionNo = WorldHistoryErpSpec.PurchaseRequisitionNo(index);
            if (existingSet.Contains(requisitionNo))
            {
                continue;
            }

            var random = new WorldHistoryRandom($"open-requisition:{requisitionNo}");
            var category = random.PickWeighted(
                WorldHistoryErpSpec.PurchaseCategories, WorldHistoryErpSpec.PurchaseCategoryWeights);
            var requisition = PurchaseRequisition.CreateFromSuggestion(
                organizationId,
                environmentId,
                requisitionNo,
                WorldHistoryErpSpec.MrpSuggestionId(index),
                random.Pick(category.MaterialSkuCodes),
                category.UomCode,
                WorldHistorySpec.SiteCode,
                random.NextQuantity(category.MinQuantity, category.MaxQuantity, category.QuantityStep),
                WorldHistoryCalendar.AddWorkingDays(asOfDate, random.NextInt(3, 11)));
            dbContext.PurchaseRequisitions.Add(requisition);
            BackdateUtc(requisition, x => x.CreatedAtUtc, MomentOn(DayBefore(asOfDate, 1), requisitionNo, "requisition"));
            added = true;
        }

        if (added)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// 应付账款：每张**已收货**的采购单派生一条 <c>AP-2026-####</c>，
    /// 来源单据号 = 该采购单的收货单号，金额 / 供应商与采购单逐字对上——
    /// 应收应付页的两栏因此对称（AR 走销售发货、AP 走采购收货），不再一边有数一边空白。
    ///
    /// 刻意做成**独立一遍**（而不是塞进 <see cref="WritePurchaseChain"/>）：
    /// 早先版本落库的采购单不会被重写，独立遍能把缺失的应付补齐，不受采购单是否新写影响。
    ///
    /// 不写应付凭证：<c>JV-2026-*</c> 号段与销售侧收入 / 收款凭证的对账恒等式绑定，
    /// 往里塞采购侧凭证会打破 <see cref="WorldHistoryConsistencyValidator"/> 的凭证配对口径。
    /// 应付的账在应付表内部自洽（金额 = 已付 + 未付），与收货单可逐单追溯。
    /// </summary>
    private async Task<int> SeedPayablesAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken)
    {
        var payablePlans = BuildPayablePlans(asOfDate, scale);
        var written = 0;

        for (var batchStart = 0; batchStart < payablePlans.Count; batchStart += BatchSize)
        {
            var batch = payablePlans.Skip(batchStart).Take(BatchSize).ToArray();
            var payableNos = batch.Select(x => x.PayableNo).ToArray();
            var existing = (await dbContext.AccountPayables
                    .AsNoTracking()
                    .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                        payableNos.Contains(x.PayableNo))
                    .Select(x => x.PayableNo)
                    .ToArrayAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var added = 0;
            foreach (var plan in batch.Where(plan => !existing.Contains(plan.PayableNo)))
            {
                var payable = AccountPayable.Create(
                    organizationId,
                    environmentId,
                    plan.PayableNo,
                    plan.SourceDocumentNo,
                    plan.SupplierCode,
                    plan.Amount,
                    WorldHistorySpec.CurrencyCode,
                    plan.InvoiceDate,
                    plan.DueDate,
                    plan.PaymentTermCode);
                if (plan.PaidAmount > 0m)
                {
                    payable.RegisterPayment(plan.PaidAmount);
                }

                // 历史事实不驱动下游：AccountPayableCreatedDomainEvent 有跨服务集成事件转换器，
                // 历史应付一旦派发会让下游把 7 个月前的账当成刚发生的事。
                payable.ClearDomainEvents();
                dbContext.AccountPayables.Add(payable);
                BackdateUtc(payable, x => x.CreatedAtUtc, MomentOn(plan.InvoiceDate, plan.PayableNo, "payable"));
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

    /// <summary>确定性应付计划流（校验器与测试按同一公式复算，公开为纯函数）。</summary>
    public static IReadOnlyList<WorldHistoryPayablePlan> BuildPayablePlans(DateOnly asOfDate, double scale) =>
        [.. BuildPurchasePlans(asOfDate, scale)
            .Where(plan => plan.IsReceived)
            .Select(plan => WorldHistoryErpSpec.BuildPayablePlan(plan, asOfDate))];

    /// <summary>确定性采购计划流（校验器与测试按同一公式复算，公开为纯函数）。</summary>
    public static IReadOnlyList<WorldHistoryPurchasePlan> BuildPurchasePlans(DateOnly asOfDate, double scale)
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

    private void WritePurchaseChain(
        string organizationId,
        string environmentId,
        WorldHistoryPurchasePlan plan,
        IReadOnlyDictionary<string, PurchaseRequisition> existingRequisitions)
    {
        // 采购申请先于采购单：MRP 建议 → 申请 → （周期性询比价）→ 下单转化（演示走查缺口：经营五页）。
        var requisitionDay = RequisitionDayBefore(plan.OrderDate);
        var requisitionNo = WorldHistoryErpSpec.PurchaseRequisitionNo(plan.Index);
        if (existingRequisitions.TryGetValue(requisitionNo, out var openRequisition))
        {
            // 上一轮 asOfDate 留下的在途申请，本轮它对应的采购单已发生：就地转化，续写同一故事。
            openRequisition.MarkConverted(plan.PurchaseOrderNo);
            BackdateNullableUtc(openRequisition, x => x.ConvertedAtUtc, MomentOn(plan.OrderDate, plan.PurchaseOrderNo, "purchase"));
        }
        else
        {
            var requisition = PurchaseRequisition.CreateFromSuggestion(
                organizationId,
                environmentId,
                requisitionNo,
                WorldHistoryErpSpec.MrpSuggestionId(plan.Index),
                plan.SkuCode,
                plan.UomCode,
                WorldHistorySpec.SiteCode,
                plan.Quantity,
                plan.PromisedDate);
            requisition.MarkConverted(plan.PurchaseOrderNo);
            dbContext.PurchaseRequisitions.Add(requisition);
            BackdateUtc(requisition, x => x.CreatedAtUtc, MomentOn(requisitionDay, plan.PurchaseOrderNo, "requisition"));
            BackdateNullableUtc(requisition, x => x.ConvertedAtUtc, MomentOn(plan.OrderDate, plan.PurchaseOrderNo, "purchase"));
        }

        if (plan.Index % WorldHistoryErpSpec.RfqEveryNthPurchase == 1)
        {
            WriteSourcingChain(organizationId, environmentId, plan, requisitionDay);
        }

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
        BackdateUtc(purchaseOrder, x => x.CreatedAtUtc, MomentOn(plan.OrderDate, plan.PurchaseOrderNo, "purchase"));

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
        BackdateUtc(receipt, x => x.RecordedAtUtc, MomentOn(plan.ReceiptDate, plan.PurchaseReceiptNo, "receipt"));

        // 部分已收货采购单进入成本归集候选（成本候选页的历史故事）。
        if (plan.Index % WorldHistoryErpSpec.CostCandidateEveryNthReceipt == 0)
        {
            var candidate = CostCandidate.Create(
                organizationId,
                environmentId,
                WorldHistoryErpSpec.CostCandidateNo(plan.Index),
                "purchase-receipt",
                plan.PurchaseReceiptNo,
                plan.TotalAmount,
                WorldHistorySpec.CurrencyCode);
            dbContext.CostCandidates.Add(candidate);
            BackdateUtc(candidate, x => x.CreatedAtUtc, MomentOn(plan.ReceiptDate, plan.PurchaseReceiptNo, "cost-candidate"));
        }
    }

    /// <summary>
    /// 周期性询比价链：申请日发 RFQ 给品类内全部供应商，各家在下单前回报价；
    /// 中标供应商（即采购单供应商）的报价价 == 采购单价，落败方报出确定性更高的价——
    /// 「为什么选这家」在页面上可自洽讲通。
    /// </summary>
    private void WriteSourcingChain(
        string organizationId,
        string environmentId,
        WorldHistoryPurchasePlan plan,
        DateOnly requisitionDay)
    {
        var category = WorldHistoryErpSpec.CategoryOf(plan.SkuCode);
        var rfq = RequestForQuotation.Create(
            organizationId,
            environmentId,
            WorldHistoryErpSpec.RfqNo(plan.Index),
            category.SupplierCodes,
            [new RfqLineDraft("10", plan.SkuCode, plan.UomCode, plan.Quantity, WorldHistorySpec.SiteCode, plan.PromisedDate)]);
        dbContext.RequestForQuotations.Add(rfq);
        BackdateUtc(rfq, x => x.CreatedAtUtc, MomentOn(requisitionDay, plan.PurchaseOrderNo, "rfq"));

        var quoteDay = WorldHistoryCalendar.SnapToWorkingDay(requisitionDay.AddDays(2));
        if (quoteDay > plan.OrderDate)
        {
            quoteDay = plan.OrderDate;
        }

        for (var supplierOrdinal = 0; supplierOrdinal < category.SupplierCodes.Count; supplierOrdinal++)
        {
            var supplierCode = category.SupplierCodes[supplierOrdinal];
            var random = new WorldHistoryRandom($"quote:{plan.PurchaseOrderNo}:{supplierCode}");
            var unitPrice = string.Equals(supplierCode, plan.SupplierCode, StringComparison.Ordinal)
                ? plan.UnitPrice
                : decimal.Round(plan.UnitPrice * (1m + (random.NextInt(3, 13) / 100m)), 2);
            var quotation = SupplierQuotation.Receive(
                organizationId,
                environmentId,
                WorldHistoryErpSpec.SupplierQuotationNo(plan.Index, supplierOrdinal),
                rfq.RfqNo,
                supplierCode,
                [new SupplierQuotationLineDraft("10", plan.SkuCode, plan.UomCode, plan.Quantity, unitPrice, plan.PromisedDate)]);
            dbContext.SupplierQuotations.Add(quotation);
            BackdateUtc(quotation, x => x.ReceivedAtUtc, MomentOn(quoteDay, quotation.QuotationNo, "supplier-quote"));
        }
    }

    /// <summary>申请日 = 下单日往前 3 个自然日吸附到工作日，且不早于上线日。</summary>
    private static DateOnly RequisitionDayBefore(DateOnly orderDate) => DayBefore(orderDate, 3);

    /// <summary>目标日往前 <paramref name="days"/> 个自然日，吸附工作日、夹在 [上线日, 目标日] 内。</summary>
    private static DateOnly DayBefore(DateOnly anchor, int days)
    {
        var candidate = anchor.AddDays(-days);
        if (candidate < WorldHistoryCalendar.GoLiveDate)
        {
            candidate = WorldHistoryCalendar.GoLiveDate;
        }

        var snapped = WorldHistoryCalendar.SnapToWorkingDay(candidate);
        return snapped > anchor ? anchor : snapped;
    }

    /// <summary>
    /// 把聚合构造函数写死的 <c>DateTime.UtcNow</c> 改写为历史时刻。
    ///
    /// ERP 的所有时间戳都是 <c>{ get; private set; }</c> 且构造函数内取 <c>UtcNow</c>，
    /// 领域 API 不提供任何回填入口。这里用 EF Core 变更跟踪器改写待插入行的列值——
    /// 是 EF 的一等公民 API，不是裸 SQL，且只作用于本 seed 新建的实体。
    ///
    /// 故意拆成 <see cref="BackdateUtc{TEntity}"/> / <see cref="BackdateNullableUtc{TEntity}"/> /
    /// <see cref="BackdateValue{TEntity, TProperty}"/> 三个签名，而不是一个全泛型方法：
    /// <c>DateTime</c> 到 <c>DateTimeOffset</c> 存在隐式转换，全泛型版本会让「把 DateTimeOffset
    /// 塞进 DateTime 列」这类错误通过编译，直到运行时才炸成 InvalidCastException。
    /// </summary>
    private void BackdateUtc<TEntity>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, DateTime>> property,
        DateTimeOffset value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value.UtcDateTime;
    }

    private void BackdateNullableUtc<TEntity>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, DateTime?>> property,
        DateTimeOffset value)
        where TEntity : class
    {
        dbContext.Entry(entity).Property(property).CurrentValue = value.UtcDateTime;
    }

    private void BackdateValue<TEntity, TProperty>(
        TEntity entity,
        System.Linq.Expressions.Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
        where TProperty : struct
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
    int PayablesWritten,
    WorldHistoryValidationReport Validation);
