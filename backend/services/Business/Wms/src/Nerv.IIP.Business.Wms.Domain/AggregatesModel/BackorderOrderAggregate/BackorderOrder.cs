using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;

namespace Nerv.IIP.Business.Wms.Domain.AggregatesModel.BackorderOrderAggregate;

public partial record BackorderOrderId : IGuidStronglyTypedId;

public enum BackorderOrderStatus
{
    Open = 0,
    Closed = 1,
}

public sealed class BackorderOrder : Entity<BackorderOrderId>, IAggregateRoot
{
    private BackorderOrder()
    {
    }

    /// <summary>
    /// 缺量单号的**唯一构造入口**（#3228）：种类与上界由 <see cref="WmsOperationalCodeKind.Backorder"/>
    /// 承载，应用层不再自己拼前缀。
    /// </summary>
    public static string ComposeBackorderOrderNo(string outboundOrderNo, string outboundOrderLineNo)
    {
        return WmsText.StableOperationalCode(WmsOperationalCodeKind.Backorder, outboundOrderNo, outboundOrderLineNo);
    }

    /// <summary>
    /// 补货建议任务号的构造入口（#3228）：种类与上界由
    /// <see cref="WmsOperationalCodeKind.ReplenishmentTask"/> 承载。
    /// </summary>
    /// <remarks>
    /// **不要读成「<c>warehouse_tasks.task_no</c> 只有这一个构造入口」**：世界史种子侧的
    /// <c>WorldHistoryPhase2Spec.WarehouseTaskNo</c> 按 <c>WT-{单号}-{序号}</c> 另拼一路，
    /// 前缀 <c>WT</c> 不在任何已声明种类里、不走 <c>StableOperationalCode</c>、也没有上界。
    /// 本类型只约束补货建议这一条路径。
    /// </remarks>
    public static string ComposeReplenishmentTaskNo(string outboundOrderNo, string outboundOrderLineNo)
    {
        return WmsText.StableOperationalCode(WmsOperationalCodeKind.ReplenishmentTask, outboundOrderNo, outboundOrderLineNo);
    }

    private BackorderOrder(
        string organizationId,
        string environmentId,
        string backorderOrderNo,
        string outboundOrderNo,
        string outboundOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string pickLocationCode,
        decimal backorderQuantity)
    {
        OrganizationId = WmsText.Required(organizationId, nameof(organizationId));
        EnvironmentId = WmsText.Required(environmentId, nameof(environmentId));
        BackorderOrderNo = WmsText.Required(backorderOrderNo, nameof(backorderOrderNo));
        OutboundOrderNo = WmsText.Required(outboundOrderNo, nameof(outboundOrderNo));
        OutboundOrderLineNo = WmsText.Required(outboundOrderLineNo, nameof(outboundOrderLineNo));
        SkuCode = WmsText.Required(skuCode, nameof(skuCode));
        UomCode = WmsText.Required(uomCode, nameof(uomCode));
        SiteCode = WmsText.Required(siteCode, nameof(siteCode));
        PickLocationCode = WmsText.Required(pickLocationCode, nameof(pickLocationCode));
        BackorderQuantity = WmsText.Positive(backorderQuantity, nameof(backorderQuantity));
        Status = BackorderOrderStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string BackorderOrderNo { get; private set; } = string.Empty;
    public string OutboundOrderNo { get; private set; } = string.Empty;
    public string OutboundOrderLineNo { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string PickLocationCode { get; private set; } = string.Empty;
    public decimal BackorderQuantity { get; private set; }
    public BackorderOrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ClosureReason { get; private set; }

    public static BackorderOrder Create(
        string organizationId,
        string environmentId,
        string backorderOrderNo,
        string outboundOrderNo,
        string outboundOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string pickLocationCode,
        decimal backorderQuantity) =>
        new(organizationId, environmentId, backorderOrderNo, outboundOrderNo, outboundOrderLineNo, skuCode, uomCode, siteCode, pickLocationCode, backorderQuantity);

    /// <summary>
    /// 短拣产生缺量单的**生产入口**（#3228）：缺量单号由聚合按 <see cref="WmsOperationalCodeKind.Backorder"/>
    /// 自算，调用方拿不到「传错种类的号」的机会。
    /// </summary>
    /// <remarks>
    /// 为什么不是把 <see cref="Create"/> 的 <c>backorderOrderNo</c> 形参收掉：那个重载还被三处测试
    /// 夹具用来构造**互不相同且被租户隔离/关键字查询断言承重**的号（`BO-HTTP-001` 等），收参会破坏
    /// 那些用例的设计。生产侧只有短拣这一条路会创建缺量单，因此给它一个专用入口即可关掉缺陷类，
    /// <see cref="Create"/> 留给夹具与将来可能出现的、号由外部给定的路径。
    /// </remarks>
    public static BackorderOrder CreateForShortPick(
        string organizationId,
        string environmentId,
        string outboundOrderNo,
        string outboundOrderLineNo,
        string skuCode,
        string uomCode,
        string siteCode,
        string pickLocationCode,
        decimal backorderQuantity) =>
        Create(
            organizationId,
            environmentId,
            ComposeBackorderOrderNo(outboundOrderNo, outboundOrderLineNo),
            outboundOrderNo,
            outboundOrderLineNo,
            skuCode,
            uomCode,
            siteCode,
            pickLocationCode,
            backorderQuantity);

    /// <summary>
    /// 补货建议任务号由聚合自己算（#3228）：它需要的两个输入 <see cref="OutboundOrderNo"/> 与
    /// <see cref="OutboundOrderLineNo"/> 本来就在聚合上，收一个 <c>string taskNo</c> 只会让调用方
    /// 有机会传错种类的号（实测：把 RPL 换成 BO 种类，全仓无一条断言会红）。不收参数即不可传错。
    /// 缺量单号那一侧的同形缺陷由 <see cref="CreateForShortPick"/> 关掉。
    /// </summary>
    public WarehouseTask CreateReplenishmentRecommendation() =>
        WarehouseTask.CreateReplenishment(
            OrganizationId,
            EnvironmentId,
            ComposeReplenishmentTaskNo(OutboundOrderNo, OutboundOrderLineNo),
            BackorderOrderNo,
            OutboundOrderLineNo,
            SkuCode,
            UomCode,
            SiteCode,
            PickLocationCode,
            BackorderQuantity);

    public void Close(string reason)
    {
        var normalizedReason = WmsText.Required(reason, nameof(reason));
        if (Status == BackorderOrderStatus.Closed)
        {
            if (!string.Equals(ClosureReason, normalizedReason, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Backorder order is already closed with a different reason.");
            }

            return;
        }

        Status = BackorderOrderStatus.Closed;
        ClosureReason = normalizedReason;
        ClosedAtUtc = DateTime.UtcNow;
    }
}
