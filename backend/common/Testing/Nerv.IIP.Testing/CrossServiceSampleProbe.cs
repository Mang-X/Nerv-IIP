using System.Globalization;
using System.Text;

namespace Nerv.IIP.Testing;

/// <summary>
/// 跨服务抽样探针的**证据行格式**（#1826）。
///
/// <para>
/// L1 背景历史由六个服务各写各的库，任何一侧的校验器都只看得见自己的库，
/// 「对方那一行是否真的存在」结构性无人验证。探针把这件事拆成两半：
/// 每个服务在自己的真库里查同一批抽样序号下自己拥有的单据，按本类型的格式
/// 逐行输出「应不应该有 / 实际有没有 / 数量 / 金额 / 时间戳」；
/// <c>scripts/verify-world-history.ps1</c> 再把六份输出按 <c>index + link</c> 对账。
/// </para>
///
/// <para>
/// 本类型只负责**格式**与**抽样序号的算术**——这两件事必须六侧逐字一致，
/// 因此集中在这里声明一次，而不是在六个测试工程里各抄一份。
/// 业务语义（哪张单该存在、数量取哪个字段）留在各服务自己的探针里，
/// 因为那正是各域的 spec 说了算的部分。
/// </para>
/// </summary>
public static class CrossServiceSampleProbe
{
    /// <summary>设定集 §7 承诺的抽样规模：20 单跨域全链。</summary>
    public const int DefaultSampleSize = 20;

    /// <summary>
    /// 跨域链接名：**一个 link 就是一次物理业务事实**，同一 <c>(index, link)</c> 下
    /// 各服务的行必须互相对得上。名字集中在这里声明，六侧引用同一常量，避免拼写漂移。
    /// </summary>
    public static class Links
    {
        /// <summary>销售订单本身（废弃单也有订单，因此恒定存在）。</summary>
        public const string SalesOrder = "sales-order";

        /// <summary>工单（废弃单没有工单）。</summary>
        public const string WorkOrder = "work-order";

        /// <summary>性能终检工序的检验任务。</summary>
        public const string OperationInspection = "operation-inspection";

        /// <summary>完工入库：MES 的入库请求、库存的成品入账、仓储的入库单都是这同一件事。</summary>
        public const string FinishedGoodsReceipt = "finished-goods-receipt";

        /// <summary>发货单本身（含 #1374 已开未发运那一档）。</summary>
        public const string DeliveryOrder = "delivery-order";

        /// <summary>发运：ERP 的发运登记、库存的出库流水、仓储的出库单都是这同一件事。</summary>
        public const string Shipment = "shipment";

        /// <summary>应收。</summary>
        public const string Receivable = "receivable";

        /// <summary>出货检验（完工装箱环节成立，与发运与否无关）。</summary>
        public const string OutboundInspection = "outbound-inspection";

        /// <summary>批次标签打印批次（按 900 张预算抽样，未被抽中的单据合法地没有）。</summary>
        public const string LotPrintBatch = "lot-print-batch";

        /// <summary>成品箱贴打印批次（同上）。</summary>
        public const string CartonPrintBatch = "carton-print-batch";
    }

    /// <summary>证据行的后缀（完整前缀由调用方拼成 <c>erp-world-history-crossdomain</c> 这类）。</summary>
    public const string RowMarker = "-crossdomain: ";

    /// <summary>抽样基准行的后缀。</summary>
    public const string BasisMarker = "-crossdomain-basis: ";

    /// <summary>缺失值的占位符：数量/金额/时间戳三列都用它表示「本行没有这个维度」。</summary>
    public const string AbsentValue = "-";

    /// <summary>
    /// 抽样序号：在 <c>[1, totalOrders]</c> 上等距取 <paramref name="sampleSize"/> 个序号。
    ///
    /// <para>
    /// 与本票之前的纯代数版本同算术（<c>1 + floor(slot × total / sampleSize)</c>），
    /// 只是从 PowerShell 挪进这里，好让六个服务与脚本用同一份实现取到同一批序号。
    /// 脚本仍会用同一算术复算一遍并与各侧上报的序号逐个比对，
    /// 任一侧取错序号即报红——这是「六侧确实在看同一批单」的门。
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> SampleIndexes(int totalOrders, int sampleSize = DefaultSampleSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalOrders);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleSize);
        if (totalOrders == 0)
        {
            return [];
        }

        var effective = Math.Min(sampleSize, totalOrders);
        var indexes = new List<int>(effective);
        for (var slot = 0; slot < effective; slot++)
        {
            indexes.Add(1 + (int)((long)slot * totalOrders / effective));
        }

        return indexes;
    }

    /// <summary>抽样基准行：六侧必须逐字段一致，否则它们根本不在对同一批单。</summary>
    public static string FormatBasis(
        string prefix,
        DateOnly asOfDate,
        double scale,
        int totalOrders,
        IReadOnlyList<int> indexes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(indexes);

        var builder = new StringBuilder();
        builder.Append(prefix).Append(BasisMarker);
        builder.Append(CultureInfo.InvariantCulture, $"asOfDate={asOfDate:yyyy-MM-dd}");
        builder.Append(CultureInfo.InvariantCulture, $";scale={FormatDecimal((decimal)scale)}");
        builder.Append(CultureInfo.InvariantCulture, $";totalOrders={totalOrders.ToString(CultureInfo.InvariantCulture)}");
        builder.Append(CultureInfo.InvariantCulture, $";sampleSize={indexes.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.Append(";indexes=");
        builder.Append(string.Join(',', indexes.Select(index => index.ToString(CultureInfo.InvariantCulture))));
        return builder.ToString();
    }

    /// <summary>单张单据的证据行。</summary>
    public static string FormatRow(string prefix, CrossServiceSampleProbeRow row)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(row);

        var builder = new StringBuilder();
        builder.Append(prefix).Append(RowMarker);
        builder.Append(CultureInfo.InvariantCulture, $"index={row.Index.ToString(CultureInfo.InvariantCulture)}");
        builder.Append(CultureInfo.InvariantCulture, $";link={Require(row.Link, nameof(row.Link))}");
        builder.Append(CultureInfo.InvariantCulture, $";kind={Require(row.Kind, nameof(row.Kind))}");
        builder.Append(CultureInfo.InvariantCulture, $";no={Require(row.DocumentNo, nameof(row.DocumentNo))}");
        builder.Append(CultureInfo.InvariantCulture, $";expected={FormatBoolean(row.Expected)}");
        builder.Append(CultureInfo.InvariantCulture, $";exists={FormatOptionalBoolean(row.Exists)}");
        builder.Append(CultureInfo.InvariantCulture, $";quantity={FormatOptionalDecimal(row.Quantity)}");
        builder.Append(CultureInfo.InvariantCulture, $";amount={FormatOptionalDecimal(row.Amount)}");
        builder.Append(CultureInfo.InvariantCulture, $";timestamp={FormatOptionalTimestamp(row.TimestampUtc)}");
        return builder.ToString();
    }

    private static string Require(string value, string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, field);
        if (value.Contains(';', StringComparison.Ordinal) || value.Contains('=', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Cross-service probe field '{field}' must not contain ';' or '=': {value}",
                field);
        }

        return value;
    }

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    // 见证行（witness）不拥有这张单，只按自己的 spec 声明「它应该存在」，
    // 因此 exists 一列对它没有意义，一律输出占位符。
    private static string FormatOptionalBoolean(bool? value) =>
        value.HasValue ? FormatBoolean(value.Value) : AbsentValue;

    private static string FormatOptionalDecimal(decimal? value) =>
        value.HasValue ? FormatDecimal(value.Value) : AbsentValue;

    // 定点 12 位小数：数量/金额在库里最多两位小数，12 位远超需要，
    // 但它保证「同一个值在两侧被格式化成同一个字符串」，不引入指数记法。
    private static string FormatDecimal(decimal value) =>
        value.ToString("0.############", CultureInfo.InvariantCulture);

    // 一律折算成 UTC 再输出 100ns 精度的 round-trip 格式：
    // 两侧的 DateTimeOffset 可能带不同 offset，字符串比较必须先归一化。
    private static string FormatOptionalTimestamp(DateTimeOffset? value) =>
        value.HasValue
            ? value.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture)
            : AbsentValue;
}

/// <summary>
/// 一张单据在某个服务眼中的抽样事实。
/// </summary>
/// <param name="Index">订单序号（跨域对账的连接键）。</param>
/// <param name="Link">跨域链接名：同一 <c>link</c> 下各服务的行互相对账。</param>
/// <param name="Kind">本服务内的单据类别，用于人读与定位。</param>
/// <param name="DocumentNo">按各域 spec 推出的单据号。</param>
/// <param name="Expected">按本服务自己的 spec，这张单**是否应该存在**（废弃单等合法缺失即 false）。</param>
/// <param name="Exists">真库里**是否真的查到**这张单；见证行（不拥有该单的服务）为 null。</param>
/// <param name="Quantity">跨域可比的数量；本行没有数量维度时为 null。</param>
/// <param name="Amount">金额；只有 ERP 侧的单据有钱。</param>
/// <param name="TimestampUtc">该业务事实发生的时刻。</param>
public sealed record CrossServiceSampleProbeRow(
    int Index,
    string Link,
    string Kind,
    string DocumentNo,
    bool Expected,
    bool? Exists,
    decimal? Quantity = null,
    decimal? Amount = null,
    DateTimeOffset? TimestampUtc = null);
