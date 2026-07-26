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
        // 应收总额 = 所有已发货订单的金额之和。
        var expectedReceivable = deliveries.Keys
            .Where(orders.ContainsKey)
            .Sum(salesOrderNo => orders[salesOrderNo].TotalAmount);
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
