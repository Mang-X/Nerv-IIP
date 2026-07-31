using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Infrastructure;
using System.Globalization;
using System.Text;

namespace Nerv.IIP.Business.Erp.Web.Application.Seed;

/// <summary>
/// 《工厂世界观设定集》§7 末尾要求的一致性校验器（ERP 侧）。
///
/// 覆盖：订单–发货–应收–凭证–收款的数量与金额链对账、状态分布是否落在设定集比例的容差内、
/// 全链时间戳单调且落在 [上线日, asOfDate] 区间内、废弃单不得留下结果事实。
/// **fail-closed**：任何一条不成立即抛 <see cref="WorldHistoryConsistencyException"/>，seed 随之失败。
///
/// 另外产出 20 单抽样全链引用，供人工逐单核对（设定集 §7「抽样 20 单全链人工可追」）。
/// </summary>
public sealed class WorldHistoryConsistencyValidator(ApplicationDbContext dbContext)
{
    /// <summary>抽样单数（设定集 §7）。</summary>
    public const int SampleSize = 20;

    /// <summary>金额比较容差（分）。所有金额都是 decimal，正常应逐分相等。</summary>
    private const decimal AmountTolerance = 0.005m;

    /// <summary>状态分布容差：确定性伪随机撒点允许 ±3 个百分点的偏离。</summary>
    private const double DistributionTolerance = 0.03;

    public async Task<WorldHistoryValidationReport> ValidateAsync(
        string organizationId,
        string environmentId,
        DateOnly asOfDate,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var plans = WorldHistorySpec.BuildOrderPlans(asOfDate, scale);
        var plansByOrderNo = plans.ToDictionary(x => x.SalesOrderNo, StringComparer.Ordinal);
        var failures = new List<string>();

        var orders = await LoadSalesOrdersAsync(organizationId, environmentId, cancellationToken);
        var deliveries = await LoadDeliveriesAsync(organizationId, environmentId, cancellationToken);
        var receivables = await LoadReceivablesAsync(organizationId, environmentId, cancellationToken);
        var cashReceipts = await LoadCashReceiptsAsync(organizationId, environmentId, cancellationToken);
        var voucherNos = await LoadVoucherNumbersAsync(organizationId, environmentId, cancellationToken);

        CheckOrderPopulation(plans, orders, failures);
        CheckChainPerOrder(plansByOrderNo, orders, deliveries, receivables, cashReceipts, voucherNos, asOfDate, failures);
        CheckDistribution(plans, failures);
        CheckLedgerTotals(orders, deliveries, receivables, cashReceipts, failures);
        await CheckBusinessObjectsAsync(organizationId, environmentId, plans, asOfDate, scale, failures, cancellationToken);

        if (failures.Count > 0)
        {
            throw new WorldHistoryConsistencyException(failures);
        }

        return new WorldHistoryValidationReport(
            OrdersChecked: orders.Count,
            DeliveriesChecked: deliveries.Count,
            ReceivablesChecked: receivables.Count,
            CashReceiptsChecked: cashReceipts.Count,
            VouchersChecked: voucherNos.Count,
            Sample: BuildSample(plans, orders, deliveries, receivables, cashReceipts));
    }

    /// <summary>
    /// 经营对象链（采购申请/询价/供应商报价/销售机会/成本候选，演示走查缺口）：
    /// 每张历史采购单必有已转化申请且转化引用正确；询比价单必收齐品类内全部供应商报价；
    /// 在途申请条数与公式一致；成本候选引用真实收货单；销售机会与订单计划配对。
    /// </summary>
    private async Task CheckBusinessObjectsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryOrderPlan> salesPlans,
        DateOnly asOfDate,
        double scale,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var purchasePlans = WorldHistorySeedService.BuildPurchasePlans(asOfDate, scale);

        var requisitionRows = await dbContext.PurchaseRequisitions
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.RequisitionNo.StartsWith("PRQ-2026-"))
            .Select(x => new { x.RequisitionNo, x.Status, x.ConvertedPurchaseOrderNo })
            .ToArrayAsync(cancellationToken);
        foreach (var duplicated in requisitionRows
            .GroupBy(x => x.RequisitionNo, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            failures.Add($"采购申请号 {duplicated.Key} 出现 {duplicated.Count()} 条重复（在途申请转化路径失效？）。");
        }

        var requisitions = requisitionRows
            .GroupBy(x => x.RequisitionNo, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var plan in purchasePlans)
        {
            var requisitionNo = WorldHistoryErpSpec.PurchaseRequisitionNo(plan.Index);
            if (!requisitions.TryGetValue(requisitionNo, out var requisition))
            {
                failures.Add($"{plan.PurchaseOrderNo} 缺少前置采购申请 {requisitionNo}。");
                continue;
            }

            if (requisition.Status != Domain.AggregatesModel.PurchaseRequisitionAggregate.PurchaseRequisitionStatus.Converted ||
                !string.Equals(requisition.ConvertedPurchaseOrderNo, plan.PurchaseOrderNo, StringComparison.Ordinal))
            {
                failures.Add($"{requisitionNo} 未正确转化到 {plan.PurchaseOrderNo}（状态 {requisition.Status}，引用 '{requisition.ConvertedPurchaseOrderNo}'）。");
            }
        }

        var expectedOpen = WorldHistoryErpSpec.OpenRequisitionCount(purchasePlans.Count);
        var openCount = requisitions.Values.Count(x =>
            x.Status == Domain.AggregatesModel.PurchaseRequisitionAggregate.PurchaseRequisitionStatus.Open);
        if (openCount != expectedOpen)
        {
            failures.Add($"在途采购申请 {openCount} 条，与公式期望 {expectedOpen} 条不符。");
        }

        var quotationCountsByRfq = (await dbContext.SupplierQuotations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.QuotationNo.StartsWith("SQ-2026-"))
            .GroupBy(x => x.RfqNo)
            .Select(group => new { RfqNo = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.RfqNo, x => x.Count, StringComparer.Ordinal);
        var rfqNos = (await dbContext.RequestForQuotations
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.RfqNo.StartsWith("RFQ-2026-"))
            .Select(x => x.RfqNo)
            .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var plan in purchasePlans.Where(x => x.Index % WorldHistoryErpSpec.RfqEveryNthPurchase == 1))
        {
            var rfqNo = WorldHistoryErpSpec.RfqNo(plan.Index);
            if (!rfqNos.Contains(rfqNo))
            {
                failures.Add($"{plan.PurchaseOrderNo} 应有询价单 {rfqNo} 却未落库。");
                continue;
            }

            var expectedQuotes = WorldHistoryErpSpec.CategoryOf(plan.SkuCode).SupplierCodes.Count;
            if (!quotationCountsByRfq.TryGetValue(rfqNo, out var quotes) || quotes != expectedQuotes)
            {
                failures.Add($"{rfqNo} 供应商报价 {quotationCountsByRfq.GetValueOrDefault(rfqNo)} 份，应为品类内 {expectedQuotes} 家。");
            }
        }

        var costCandidates = (await dbContext.CostCandidates
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.CandidateNo.StartsWith("COST-2026-"))
            .Select(x => new { x.CandidateNo, x.SourceDocumentNo })
            .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.CandidateNo, x => x.SourceDocumentNo, StringComparer.Ordinal);
        foreach (var plan in purchasePlans.Where(x =>
            x.IsReceived && x.Index % WorldHistoryErpSpec.CostCandidateEveryNthReceipt == 0))
        {
            var candidateNo = WorldHistoryErpSpec.CostCandidateNo(plan.Index);
            if (!costCandidates.TryGetValue(candidateNo, out var sourceDocumentNo) ||
                !string.Equals(sourceDocumentNo, plan.PurchaseReceiptNo, StringComparison.Ordinal))
            {
                failures.Add($"{candidateNo} 缺失或未引用收货单 {plan.PurchaseReceiptNo}。");
            }
        }

        await CheckPayablesAsync(organizationId, environmentId, purchasePlans, asOfDate, failures, cancellationToken);

        var opportunities = (await dbContext.Opportunities
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.OpportunityNo.StartsWith("OPP-2026-"))
            .Select(x => new { x.OpportunityNo, x.CustomerCode })
            .ToArrayAsync(cancellationToken))
            .ToDictionary(x => x.OpportunityNo, x => x.CustomerCode, StringComparer.Ordinal);
        foreach (var plan in salesPlans.Where(x => x.Index % WorldHistoryErpSpec.OpportunityEveryNthSalesOrder == 1))
        {
            var opportunityNo = WorldHistoryErpSpec.OpportunityNo(plan.Index);
            if (!opportunities.TryGetValue(opportunityNo, out var customerCode) ||
                !string.Equals(customerCode, plan.CustomerCode, StringComparison.Ordinal))
            {
                failures.Add($"{opportunityNo} 缺失或客户 '{opportunities.GetValueOrDefault(opportunityNo)}' 与订单计划 {plan.CustomerCode} 不符。");
            }
        }
    }

    /// <summary>
    /// 应付账款对账（<c>AP-2026-####</c>）：
    /// 每张已收货采购单必有且只有一条应付，来源单据号 = 收货单号、供应商与金额逐字对上；
    /// 未收货采购单一条应付都不能有；已付不得超过应付；发票日 / 到期日不得倒挂或越出历史窗。
    ///
    /// 与销售侧同样的口径纪律：**只断言与 asOfDate 无关的量**。
    /// 发票日 / 到期日 / 已付进度都随 asOfDate 前移而变，而库内老行不会被重写，
    /// 逐字复算它们会在「同一个库换更晚日期重跑」时误杀。
    /// </summary>
    private async Task CheckPayablesAsync(
        string organizationId,
        string environmentId,
        IReadOnlyList<WorldHistoryPurchasePlan> purchasePlans,
        DateOnly asOfDate,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        var payables = (await dbContext.AccountPayables
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.PayableNo.StartsWith("AP-2026-"))
            .Select(x => new PayableProjection(
                x.PayableNo,
                x.SourceDocumentNo,
                x.SupplierCode,
                x.Amount,
                x.PaidAmount,
                x.InvoiceDate,
                x.DueDate,
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken))
            .ToArray();
        var payableByNo = payables
            .GroupBy(x => x.PayableNo, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var lowerBound = WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue).AddDays(-1);
        var receivedAmountTotal = 0m;

        foreach (var plan in purchasePlans)
        {
            var payableNo = WorldHistoryErpSpec.PayableNo(plan.Index);
            if (!plan.IsReceived)
            {
                if (payableByNo.ContainsKey(payableNo))
                {
                    failures.Add($"{plan.PurchaseOrderNo} 尚未收货却存在应付 {payableNo}。");
                }

                continue;
            }

            receivedAmountTotal += decimal.Round(plan.TotalAmount, 2);
            if (!payableByNo.TryGetValue(payableNo, out var rows))
            {
                failures.Add($"{plan.PurchaseOrderNo} 已收货却缺少应付 {payableNo}。");
                continue;
            }

            if (rows.Length > 1)
            {
                failures.Add($"应付 {payableNo} 出现 {rows.Length} 条重复。");
            }

            var payable = rows[0];
            if (!string.Equals(payable.SourceDocumentNo, plan.PurchaseReceiptNo, StringComparison.Ordinal))
            {
                failures.Add($"{payableNo} 来源单据 '{payable.SourceDocumentNo}' 与收货单 {plan.PurchaseReceiptNo} 不符。");
            }

            if (!string.Equals(payable.SupplierCode, plan.SupplierCode, StringComparison.Ordinal))
            {
                failures.Add($"{payableNo} 供应商 '{payable.SupplierCode}' 与采购单 {plan.SupplierCode} 不符。");
            }

            if (Math.Abs(payable.Amount - decimal.Round(plan.TotalAmount, 2)) > AmountTolerance)
            {
                failures.Add($"{payableNo} 应付金额 {payable.Amount} 与采购单金额 {plan.TotalAmount} 不符。");
            }

            if (payable.PaidAmount < 0m || payable.PaidAmount - payable.Amount > AmountTolerance)
            {
                failures.Add($"{payableNo} 已付 {payable.PaidAmount} 越出 [0, {payable.Amount}]。");
            }

            if (payable.DueDate < payable.InvoiceDate)
            {
                failures.Add($"{payableNo} 到期日早于发票日。");
            }

            if (payable.InvoiceDate < WorldHistoryCalendar.GoLiveDate || payable.InvoiceDate > asOfDate)
            {
                failures.Add($"{payableNo} 发票日 {payable.InvoiceDate:d} 落在 [{WorldHistoryCalendar.GoLiveDate:d}, {asOfDate:d}] 之外。");
            }

            if (payable.CreatedAtUtc < lowerBound)
            {
                failures.Add($"{payableNo} 创建时间越界：{payable.CreatedAtUtc:O}。");
            }
        }

        // 总账层面：应付合计 = 已收货采购单金额合计（只统计本次计划覆盖到的号，
        // 换更晚日期重跑时留在库里的更大序号不参与本次对账）。
        var plannedPayableNos = purchasePlans
            .Where(plan => plan.IsReceived)
            .Select(plan => WorldHistoryErpSpec.PayableNo(plan.Index))
            .ToHashSet(StringComparer.Ordinal);
        var actualPayableTotal = plannedPayableNos
            .Where(payableByNo.ContainsKey)
            .Sum(payableNo => payableByNo[payableNo][0].Amount);
        if (payables.Length > 0 && Math.Abs(receivedAmountTotal - actualPayableTotal) > AmountTolerance)
        {
            failures.Add($"应付总额不平：已收货采购单合计 {receivedAmountTotal}，应付合计 {actualPayableTotal}。");
        }
    }

    #region 载入紧凑投影

    private async Task<Dictionary<string, OrderProjection>> LoadSalesOrdersAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.SalesOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.SalesOrderNo.StartsWith("SO-2026-"))
            .Select(x => new OrderProjection(
                x.SalesOrderNo,
                x.CustomerCode,
                x.Status,
                x.TotalAmount,
                x.CreatedAtUtc,
                x.Lines.Sum(line => line.OrderedQuantity),
                x.Lines.Sum(line => line.DeliveredQuantity)))
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => x.SalesOrderNo, StringComparer.Ordinal);

    private async Task<Dictionary<string, DeliveryProjection>> LoadDeliveriesAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.DeliveryOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.DeliveryOrderNo.StartsWith("DO-2026-"))
            .Select(x => new DeliveryProjection(
                x.DeliveryOrderNo,
                x.SalesOrderNo,
                x.Status,
                x.ReleasedAtUtc,
                x.ShippedAtUtc,
                x.Lines.Sum(line => line.ShippedQuantity)))
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => x.SalesOrderNo, StringComparer.Ordinal);

    private async Task<Dictionary<string, ReceivableProjection>> LoadReceivablesAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.AccountReceivables
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.ReceivableNo.StartsWith("AR-2026-"))
            .Select(x => new ReceivableProjection(
                x.ReceivableNo,
                x.SourceDocumentNo,
                x.Amount,
                x.CollectedAmount,
                x.InvoiceDate,
                x.DueDate,
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => x.SourceDocumentNo, StringComparer.Ordinal);

    private async Task<Dictionary<string, CashReceiptProjection>> LoadCashReceiptsAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.CashReceipts
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.CashReceiptNo.StartsWith("CR-2026-"))
            .Select(x => new CashReceiptProjection(
                x.CashReceiptNo,
                x.Amount,
                x.ReceiptDate,
                x.RegisteredAtUtc,
                x.Allocations.Select(allocation => allocation.ReceivableNo).First()))
            .ToArrayAsync(cancellationToken))
        .ToDictionary(x => x.ReceivableNo, StringComparer.Ordinal);

    private async Task<HashSet<string>> LoadVoucherNumbersAsync(
        string organizationId, string environmentId, CancellationToken cancellationToken) =>
        (await dbContext.JournalVouchers
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId &&
                x.VoucherNo.StartsWith("JV-2026-"))
            .Select(x => x.VoucherNo)
            .ToArrayAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    #endregion

    #region 校验项

    private static void CheckOrderPopulation(
        IReadOnlyList<WorldHistoryOrderPlan> plans,
        Dictionary<string, OrderProjection> orders,
        List<string> failures)
    {
        if (orders.Count != plans.Count)
        {
            failures.Add(
                $"销售订单数量不符：计划 {plans.Count} 张，库内 {orders.Count} 张（号段 SO-2026-*）。");
        }

        var missing = plans.Where(plan => !orders.ContainsKey(plan.SalesOrderNo)).Take(5).ToArray();
        foreach (var plan in missing)
        {
            failures.Add($"计划中的销售订单 {plan.SalesOrderNo} 未落库。");
        }
    }

    private static void CheckChainPerOrder(
        Dictionary<string, WorldHistoryOrderPlan> plansByOrderNo,
        Dictionary<string, OrderProjection> orders,
        Dictionary<string, DeliveryProjection> deliveries,
        Dictionary<string, ReceivableProjection> receivables,
        Dictionary<string, CashReceiptProjection> cashReceipts,
        HashSet<string> voucherNos,
        DateOnly asOfDate,
        List<string> failures)
    {
        var lowerBound = WorldHistoryCalendar.GoLiveDate.ToDateTime(TimeOnly.MinValue).AddDays(-1);
        var upperBound = asOfDate.ToDateTime(TimeOnly.MaxValue).AddDays(1);

        foreach (var (salesOrderNo, order) in orders)
        {
            if (!plansByOrderNo.TryGetValue(salesOrderNo, out var plan))
            {
                failures.Add($"库内销售订单 {salesOrderNo} 不在本次计划内（号段被外部占用？）。");
                continue;
            }

            if (order.CreatedAtUtc < lowerBound || order.CreatedAtUtc > upperBound)
            {
                failures.Add($"{salesOrderNo} 创建时间 {order.CreatedAtUtc:O} 落在 [{lowerBound:d}, {asOfDate:d}] 之外。");
            }

            if (plan.Stage == WorldHistoryOrderStage.Cancelled)
            {
                CheckCancelled(salesOrderNo, order, deliveries, receivables, failures);
                continue;
            }

            if (Math.Abs(order.OrderedQuantity - plan.Quantity) > AmountTolerance)
            {
                failures.Add($"{salesOrderNo} 订单数量 {order.OrderedQuantity} 与计划 {plan.Quantity} 不符。");
            }

            if (Math.Abs(order.TotalAmount - plan.TotalAmount) > AmountTolerance)
            {
                failures.Add($"{salesOrderNo} 订单金额 {order.TotalAmount} 与计划 {plan.TotalAmount} 不符。");
            }

            // 这里刻意**按库内既有事实**分流，而不是按计划的阶段：同一个库在更晚的日期重跑时，
            // 订单总数变大会把老单的计划阶段往前推（在制 → 已结案），但库里的行早已写定不会重写。
            // 「计划说该发货、库里没有发货单」在那种场景下是正常的，不该让 fail-closed 校验误杀。
            // 真正要守的是链路自洽：有发货就必须有配套的应收与凭证，没发货就一条都不能有。
            if (!deliveries.ContainsKey(salesOrderNo))
            {
                if (receivables.ContainsKey(salesOrderNo))
                {
                    failures.Add($"{salesOrderNo} 没有发货单却存在应收。");
                }

                if (voucherNos.Contains(WorldHistorySpec.RevenueVoucherNo(plan.Index)))
                {
                    failures.Add($"{salesOrderNo} 没有发货单却存在收入凭证。");
                }

                if (order.DeliveredQuantity != 0m)
                {
                    failures.Add($"{salesOrderNo} 没有发货单却记录了已发数量 {order.DeliveredQuantity}。");
                }

                continue;
            }

            CheckDeliveredChain(
                salesOrderNo, plan, order, deliveries, receivables, cashReceipts, voucherNos, lowerBound, upperBound, failures);
        }
    }

    private static void CheckCancelled(
        string salesOrderNo,
        OrderProjection order,
        Dictionary<string, DeliveryProjection> deliveries,
        Dictionary<string, ReceivableProjection> receivables,
        List<string> failures)
    {
        if (!string.Equals(order.Status, "cancelled", StringComparison.Ordinal))
        {
            failures.Add($"{salesOrderNo} 计划为废弃单，库内状态却是 '{order.Status}'。");
        }

        if (deliveries.ContainsKey(salesOrderNo))
        {
            failures.Add($"{salesOrderNo} 是废弃单却存在发货单。");
        }

        if (receivables.ContainsKey(salesOrderNo))
        {
            failures.Add($"{salesOrderNo} 是废弃单却存在应收。");
        }
    }

    /// <summary>
    /// 已完工待发货（#1374）：发货单已开、停在 released，因而**必须**没有已发数量、
    /// 没有应收、没有任何凭证——这些都要等演示当场发运后走真实路径产生。
    ///
    /// 与兄弟分支同一姿势，按**库内既有事实**分流（发货单有没有发运时间），不按计划阶段。
    /// </summary>
    private static void CheckPendingShipmentChain(
        string salesOrderNo,
        WorldHistoryOrderPlan plan,
        OrderProjection order,
        DeliveryProjection delivery,
        Dictionary<string, ReceivableProjection> receivables,
        HashSet<string> voucherNos,
        List<string> failures)
    {
        if (!string.Equals(delivery.Status, "released", StringComparison.Ordinal))
        {
            failures.Add(
                $"{salesOrderNo} 的发货单 {delivery.DeliveryOrderNo} 没有发运时间，状态却是 '{delivery.Status}'，应为 released。");
        }

        if (Math.Abs(delivery.ShippedQuantity) > AmountTolerance)
        {
            failures.Add(
                $"{salesOrderNo} 的发货单尚未发运，却已记录发出数量 {delivery.ShippedQuantity}。");
        }

        // `DeliveryOrder.Release` 会在开单时就把数量登记到订单行上（`SalesOrder.RegisterDelivery`），
        // 因此订单的「已发数量」在开单即等于订单量——这是聚合既有语义，不是待发货态的破绽。
        // 真正区分发没发运的是发货单自己的 ShippedQuantity / ShippedAtUtc。
        if (Math.Abs(order.DeliveredQuantity - plan.Quantity) > AmountTolerance)
        {
            failures.Add(
                $"{salesOrderNo} 已开发货单，订单登记的发货数量 {order.DeliveredQuantity} 与计划 {plan.Quantity} 不符。");
        }

        if (receivables.ContainsKey(salesOrderNo))
        {
            failures.Add($"{salesOrderNo} 尚未发运却存在应收——收入要等发运后才确认。");
        }

        foreach (var voucherNo in new[]
                 {
                     WorldHistorySpec.RevenueVoucherNo(plan.Index),
                     WorldHistorySpec.CollectionVoucherNo(plan.Index),
                 }.Where(voucherNos.Contains))
        {
            failures.Add($"{salesOrderNo} 尚未发运却存在凭证 {voucherNo}。");
        }
    }

    private static void CheckDeliveredChain(
        string salesOrderNo,
        WorldHistoryOrderPlan plan,
        OrderProjection order,
        Dictionary<string, DeliveryProjection> deliveries,
        Dictionary<string, ReceivableProjection> receivables,
        Dictionary<string, CashReceiptProjection> cashReceipts,
        HashSet<string> voucherNos,
        DateTime lowerBound,
        DateTime upperBound,
        List<string> failures)
    {
        if (!deliveries.TryGetValue(salesOrderNo, out var delivery))
        {
            failures.Add($"{salesOrderNo} 应已发货却没有发货单。");
            return;
        }

        // #1374 · 已开未发运的发货单走待发货分支，别拿「必须 completed + 必须有应收」去误杀它。
        if (delivery.ShippedAtUtc is null)
        {
            CheckPendingShipmentChain(
                salesOrderNo, plan, order, delivery, receivables, voucherNos, failures);
            return;
        }

        if (!string.Equals(delivery.Status, "completed", StringComparison.Ordinal))
        {
            failures.Add($"{salesOrderNo} 的发货单 {delivery.DeliveryOrderNo} 状态为 '{delivery.Status}'，应为 completed。");
        }

        // 数量链：订单已发数 = 发货单已发数 = 计划数量。
        if (Math.Abs(delivery.ShippedQuantity - plan.Quantity) > AmountTolerance ||
            Math.Abs(order.DeliveredQuantity - plan.Quantity) > AmountTolerance)
        {
            failures.Add(
                $"{salesOrderNo} 数量链不平：计划 {plan.Quantity}、订单已发 {order.DeliveredQuantity}、发货单已发 {delivery.ShippedQuantity}。");
        }

        if (delivery.ShippedAtUtc is null)
        {
            failures.Add($"{salesOrderNo} 的发货单缺少发运时间。");
        }
        else if (delivery.ShippedAtUtc < order.CreatedAtUtc)
        {
            failures.Add($"{salesOrderNo} 发运时间 {delivery.ShippedAtUtc:O} 早于下单时间 {order.CreatedAtUtc:O}。");
        }

        if (!receivables.TryGetValue(salesOrderNo, out var receivable))
        {
            failures.Add($"{salesOrderNo} 已发货却没有应收。");
            return;
        }

        if (Math.Abs(receivable.Amount - plan.TotalAmount) > AmountTolerance)
        {
            failures.Add($"{salesOrderNo} 应收金额 {receivable.Amount} 与订单金额 {plan.TotalAmount} 不符。");
        }

        if (receivable.CreatedAtUtc < lowerBound || receivable.CreatedAtUtc > upperBound)
        {
            failures.Add($"{receivable.ReceivableNo} 创建时间越界：{receivable.CreatedAtUtc:O}。");
        }

        var revenueVoucherNo = WorldHistorySpec.RevenueVoucherNo(plan.Index);
        if (!voucherNos.Contains(revenueVoucherNo))
        {
            failures.Add($"{salesOrderNo} 已发货却缺少收入凭证 {revenueVoucherNo}。");
        }

        // 同上：按库内是否存在收款单分流，而不是按计划阶段。
        var collectionVoucherNo = WorldHistorySpec.CollectionVoucherNo(plan.Index);
        if (cashReceipts.TryGetValue(receivable.ReceivableNo, out var cashReceipt))
        {
            if (Math.Abs(receivable.CollectedAmount - receivable.Amount) > AmountTolerance)
            {
                failures.Add(
                    $"{salesOrderNo} 有收款单却未结清：应收 {receivable.Amount}，实收 {receivable.CollectedAmount}。");
            }

            if (Math.Abs(cashReceipt.Amount - receivable.Amount) > AmountTolerance)
            {
                failures.Add($"{salesOrderNo} 收款单金额 {cashReceipt.Amount} 与应收 {receivable.Amount} 不符。");
            }

            if (!voucherNos.Contains(collectionVoucherNo))
            {
                failures.Add($"{salesOrderNo} 已收款却缺少收款凭证 {collectionVoucherNo}。");
            }

            if (delivery.ShippedAtUtc is not null && cashReceipt.RegisteredAtUtc < delivery.ShippedAtUtc)
            {
                failures.Add($"{salesOrderNo} 收款时间早于发运时间。");
            }

            return;
        }

        if (receivable.CollectedAmount != 0m)
        {
            failures.Add($"{salesOrderNo} 没有收款单却已收 {receivable.CollectedAmount}。");
        }

        if (voucherNos.Contains(collectionVoucherNo))
        {
            failures.Add($"{salesOrderNo} 没有收款单却存在收款凭证 {collectionVoucherNo}。");
        }
    }

    private static void CheckDistribution(IReadOnlyList<WorldHistoryOrderPlan> plans, List<string> failures)
    {
        if (plans.Count == 0)
        {
            return;
        }

        var expected = new (WorldHistoryOrderStage Stage, double Share, string Label)[]
        {
            (WorldHistoryOrderStage.Settled, 0.78, "已收款结案"),
            (WorldHistoryOrderStage.Shipped, 0.08, "已发货待收款"),
            (WorldHistoryOrderStage.InProgress, 0.09, "在制"),
            (WorldHistoryOrderStage.Released, 0.03, "已下达待开工"),
            (WorldHistoryOrderStage.Cancelled, 0.02, "废弃"),
        };

        foreach (var (stage, share, label) in expected)
        {
            var actual = (double)plans.Count(plan => plan.Stage == stage) / plans.Count;

            // 容差随样本量放宽：小缩放（Scale=0.02 只有几十单）下 2% 的废弃率本来就会有几个百分点的
            // 抽样波动，用固定 ±3% 卡会误报。取「3 倍标准误」与固定 ±3% 的较大者——全量 3200 单时
            // 3σ 只有 0.7%，实际生效的仍是那条更严的固定容差。
            var standardError = Math.Sqrt(share * (1 - share) / plans.Count);
            var tolerance = Math.Max(DistributionTolerance, 3 * standardError);
            if (Math.Abs(actual - share) > tolerance)
            {
                failures.Add(
                    $"状态分布偏离设定集 §7：{label} 实际 {actual:P1}，期望 {share:P0}" +
                    $"（{plans.Count} 单样本下容差 ±{tolerance:P1}）。");
            }
        }

        // #1374 · 已完工待发货是**定额**而不是比例：它的存在只为给演示留几个可操作对象。
        // 用比例卡它没有意义（3/4413 落在任何容差里都成立），因此直接卡条数——
        // 一旦有人把 PromotePendingShipments 改坏，发货链会重新退回「零可操作对象」，这里必须红。
        var pendingShipments = plans.Count(plan => plan.Stage == WorldHistoryOrderStage.PendingShipment);
        var expectedPendingShipments = Math.Min(
            WorldHistorySpec.PendingShipmentOrderCount,
            plans.Count(plan => plan.Stage is WorldHistoryOrderStage.Shipped
                or WorldHistoryOrderStage.PendingShipment));
        if (pendingShipments != expectedPendingShipments)
        {
            failures.Add(
                $"已完工待发货的订单有 {pendingShipments} 张，期望 {expectedPendingShipments} 张" +
                "——发货链要靠它才有可操作对象。");
        }
    }

    /// <summary>
    /// 总账层面的横向对账，全部**从库内事实推导**（不引用计划阶段），
    /// 因此同一个库在更晚的日期重跑也不会误判。
    /// </summary>
    private static void CheckLedgerTotals(
        Dictionary<string, OrderProjection> orders,
        Dictionary<string, DeliveryProjection> deliveries,
        Dictionary<string, ReceivableProjection> receivables,
        Dictionary<string, CashReceiptProjection> cashReceipts,
        List<string> failures)
    {
        // 应收总额 = 所有**已发运**订单的金额之和。
        // #1374：已开未发运的发货单不确认收入，因此按 ShippedAtUtc 而不是「有没有发货单」来算。
        var expectedReceivable = deliveries
            .Where(entry => entry.Value.ShippedAtUtc is not null && orders.ContainsKey(entry.Key))
            .Sum(entry => orders[entry.Key].TotalAmount);
        var actualReceivable = receivables.Values.Sum(x => x.Amount);
        if (Math.Abs(expectedReceivable - actualReceivable) > AmountTolerance)
        {
            failures.Add($"应收总额不平：已发货订单合计 {expectedReceivable}，应收合计 {actualReceivable}。");
        }

        // 收款单合计 = 应收上的已收合计。
        var actualCollected = cashReceipts.Values.Sum(x => x.Amount);
        var actualCollectedOnReceivables = receivables.Values.Sum(x => x.CollectedAmount);
        if (Math.Abs(actualCollected - actualCollectedOnReceivables) > AmountTolerance)
        {
            failures.Add($"收款单合计 {actualCollected} 与应收上的已收合计 {actualCollectedOnReceivables} 不符。");
        }
    }

    #endregion

    private static IReadOnlyList<string> BuildSample(
        IReadOnlyList<WorldHistoryOrderPlan> plans,
        Dictionary<string, OrderProjection> orders,
        Dictionary<string, DeliveryProjection> deliveries,
        Dictionary<string, ReceivableProjection> receivables,
        Dictionary<string, CashReceiptProjection> cashReceipts)
    {
        if (plans.Count == 0)
        {
            return [];
        }

        var stride = Math.Max(1, plans.Count / SampleSize);
        var sample = new List<string>(SampleSize);
        for (var index = 0; index < plans.Count && sample.Count < SampleSize; index += stride)
        {
            var plan = plans[index];
            if (!orders.TryGetValue(plan.SalesOrderNo, out var order))
            {
                continue;
            }

            var builder = new StringBuilder();
            builder.Append(CultureInfo.InvariantCulture, $"{plan.SalesOrderNo} [{plan.Stage}] {plan.CustomerCode} {plan.SkuCode} ×{plan.Quantity:0.##}");
            builder.Append(CultureInfo.InvariantCulture, $" 金额={order.TotalAmount:0.00} 下单={order.CreatedAtUtc:yyyy-MM-dd HH:mm}Z");
            builder.Append(CultureInfo.InvariantCulture, $" 工单={plan.WorkOrderNo}");

            if (deliveries.TryGetValue(plan.SalesOrderNo, out var delivery))
            {
                builder.Append(CultureInfo.InvariantCulture, $" → {delivery.DeliveryOrderNo}({delivery.Status}) 发运={delivery.ShippedAtUtc:yyyy-MM-dd HH:mm}Z 已发={delivery.ShippedQuantity:0.##}");
            }

            if (receivables.TryGetValue(plan.SalesOrderNo, out var receivable))
            {
                builder.Append(CultureInfo.InvariantCulture, $" → {receivable.ReceivableNo} 应收={receivable.Amount:0.00} 已收={receivable.CollectedAmount:0.00} 到期={receivable.DueDate:yyyy-MM-dd}");
                builder.Append(CultureInfo.InvariantCulture, $" 凭证={WorldHistorySpec.RevenueVoucherNo(plan.Index)}");

                if (cashReceipts.TryGetValue(receivable.ReceivableNo, out var cashReceipt))
                {
                    builder.Append(CultureInfo.InvariantCulture, $" → {cashReceipt.CashReceiptNo} 收款={cashReceipt.Amount:0.00}@{cashReceipt.ReceiptDate:yyyy-MM-dd} 凭证={WorldHistorySpec.CollectionVoucherNo(plan.Index)}");
                }
            }

            sample.Add(builder.ToString());
        }

        return sample;
    }

    private sealed record OrderProjection(
        string SalesOrderNo,
        string CustomerCode,
        string Status,
        decimal TotalAmount,
        DateTime CreatedAtUtc,
        decimal OrderedQuantity,
        decimal DeliveredQuantity);

    private sealed record DeliveryProjection(
        string DeliveryOrderNo,
        string SalesOrderNo,
        string Status,
        DateTime ReleasedAtUtc,
        DateTime? ShippedAtUtc,
        decimal ShippedQuantity);

    private sealed record ReceivableProjection(
        string ReceivableNo,
        string SourceDocumentNo,
        decimal Amount,
        decimal CollectedAmount,
        DateOnly InvoiceDate,
        DateOnly DueDate,
        DateTime CreatedAtUtc);

    private sealed record PayableProjection(
        string PayableNo,
        string SourceDocumentNo,
        string SupplierCode,
        decimal Amount,
        decimal PaidAmount,
        DateOnly InvoiceDate,
        DateOnly DueDate,
        DateTime CreatedAtUtc);

    private sealed record CashReceiptProjection(
        string CashReceiptNo,
        decimal Amount,
        DateOnly ReceiptDate,
        DateTime RegisteredAtUtc,
        string ReceivableNo);
}

/// <summary>一致性校验器的产出摘要。</summary>
public sealed record WorldHistoryValidationReport(
    int OrdersChecked,
    int DeliveriesChecked,
    int ReceivablesChecked,
    int CashReceiptsChecked,
    int VouchersChecked,
    IReadOnlyList<string> Sample);

/// <summary>一致性校验失败。抛出即代表 seed 失败（fail-closed）。</summary>
public sealed class WorldHistoryConsistencyException : InvalidOperationException
{
    public WorldHistoryConsistencyException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public WorldHistoryConsistencyException()
        : base("World-history consistency validation failed.")
    {
        Failures = [];
    }

    public WorldHistoryConsistencyException(string message)
        : base(message)
    {
        Failures = [message];
    }

    public WorldHistoryConsistencyException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [message];
    }

    public IReadOnlyList<string> Failures { get; }

    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var builder = new StringBuilder("L1 背景历史一致性校验失败，共 ");
        builder.Append(failures.Count).AppendLine(" 条：");
        foreach (var failure in failures.Take(25))
        {
            builder.Append("  - ").AppendLine(failure);
        }

        if (failures.Count > 25)
        {
            builder.Append("  … 另有 ").Append(failures.Count - 25).AppendLine(" 条未列出。");
        }

        return builder.ToString();
    }
}
