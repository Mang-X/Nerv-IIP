namespace Nerv.IIP.Business.Wms.Domain;

/// <summary>
/// WMS 运营单号「承载列宽 → 构造上界」的唯一出处（#3228）。
/// </summary>
/// <remarks>
/// 缺陷形状：退供单号按 <c>RTS-{入库单号}-{入库行号}-{检验记录号}</c> 拼接，理论上界 356，
/// 却同时落进 <c>supplier_return_requests.supplier_return_no</c>（300）与
/// <c>outbound_orders.outbound_order_no</c>（100）两列。**一个值被写进多列时，
/// 有效上界是这些列宽的最小值，而不是它「自己那一列」的宽度**——这正是原来看不出问题的地方：
/// 退供聚合只知道自己 300 宽，出库单那 100 宽的承载面不在它的视野里。
///
/// 因此本类型只承担一件事：把「哪些列承载这个单号」写成显式清单，由
/// <see cref="MaxLengthFor"/> 取最小值算出构造上界，**不许再手抄数字**。
/// 真正的有界构造在 <c>WmsText.StableOperationalCode</c>。
///
/// 列宽常量与 EF 模型的一致性由 <c>WmsSchemaConventionTests</c> 的契约测试按闭集枚举断言，
/// 任一单边改动即红。
///
/// **没有修什么**：本类型不改变 <c>SaveChangesAsync</c> 在 try/catch 之外导致
/// <c>DbUpdateException</c> 逃逸 CAP 消费者的问题（系统性缺口 #877）。它只消除溢出本身；
/// 越界时改由 <c>ArgumentException</c> 在 try 块内抛出，从而落进既有的死信分支。
/// </remarks>
public static class WmsOperationalCodePolicy
{
    /// <summary><c>outbound_orders.outbound_order_no</c> 列宽。</summary>
    public const int OutboundOrderNoColumnMaxLength = 100;

    /// <summary><c>supplier_return_requests.supplier_return_no</c> 列宽。</summary>
    public const int SupplierReturnNoColumnMaxLength = 300;

    /// <summary><c>backorder_orders.backorder_order_no</c> 列宽。</summary>
    public const int BackorderOrderNoColumnMaxLength = 100;

    /// <summary><c>warehouse_tasks.task_no</c> 列宽。</summary>
    public const int WarehouseTaskNoColumnMaxLength = 100;

    /// <summary>
    /// 一个单号被同时写进多列时的有效上界 = 这些列宽的最小值。
    /// <paramref name="columnMaxLengths"/> 必须是该单号真正落库的那些列的宽度本身。
    /// </summary>
    public static int MaxLengthFor(params int[] columnMaxLengths)
    {
        ArgumentNullException.ThrowIfNull(columnMaxLengths);
        if (columnMaxLengths.Length == 0)
        {
            throw new ArgumentException("必须至少给出一个承载列宽。", nameof(columnMaxLengths));
        }

        return columnMaxLengths.Min();
    }

    /// <summary>
    /// 退供单号上界：它既是 <c>supplier_return_no</c>，又被退供派生出库单原样当作
    /// <c>outbound_order_no</c> 使用，故取两列宽的最小值。
    /// </summary>
    public static int SupplierReturnNoMaxLength =>
        MaxLengthFor(SupplierReturnNoColumnMaxLength, OutboundOrderNoColumnMaxLength);

    /// <summary>缺量单号上界：只落 <c>backorder_order_no</c> 一列。</summary>
    public static int BackorderOrderNoMaxLength => MaxLengthFor(BackorderOrderNoColumnMaxLength);

    /// <summary>补货建议任务号上界：只落 <c>warehouse_tasks.task_no</c> 一列。</summary>
    public static int ReplenishmentTaskNoMaxLength => MaxLengthFor(WarehouseTaskNoColumnMaxLength);
}
