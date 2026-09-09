namespace Nerv.IIP.Business.Wms.Domain;

/// <summary>
/// 一类 WMS 运营单号：前缀与构造上界绑成**一个值**，上界由该类单号的承载列清单派生（#3228）。
/// </summary>
/// <remarks>
/// 缺陷形状：退供单号按 <c>RTS-{入库单号}-{入库行号}-{检验记录号}</c> 拼接，理论上界 356，
/// 却同时落进 <c>supplier_return_requests.supplier_return_no</c>（300）与
/// <c>outbound_orders.outbound_order_no</c>（100）两列。**一个值被写进多列时，
/// 有效上界是这些列宽的最小值，而不是它「自己那一列」的宽度**——这正是原来看不出问题的地方：
/// 退供聚合只知道自己 300 宽，出库单那 100 宽的承载面不在它的视野里。
///
/// **为什么是一个值而不是两个参数**：把前缀与上界拆成 <c>(string prefix, int maxLength)</c> 两个
/// 互不约束的形参，调用方随手手抄一个字面量就能配错，缺陷类原样存活。绑成本类型后，
/// <c>WmsText.StableOperationalCode(kind, parts)</c> 拿不到裸 <c>int</c>，
/// 「前缀与上界对不上」在类型上不可表达。每类单号只有 <see cref="SupplierReturn"/> 这样一个实例，
/// 承载列清单写在实例上，别处不再重复。
///
/// **上界与回落形态的关系由构造时保证**：回落形态是 <c>{前缀}-{64 位十六进制摘要}</c>，
/// 构造函数要求上界至少放得下它，因此 <c>WmsText.StableOperationalCode</c> 内部**不需要**
/// 再写一条运行期「回落也塞不下」的分支——那条分支在任何已声明的单号种类上都不可达。
///
/// 列宽常量与 EF 模型的一致性由 <c>WmsSchemaConventionTests</c> 断言（口径见那里的注释：
/// 常量集合从类型系统反射枚举，但**「哪个常量对应哪一列」是手写绑定**）。
/// </remarks>
public sealed class WmsOperationalCodeKind
{
    /// <summary>回落形态的摘要位数（SHA-256 十六进制）。</summary>
    private const int HashHexLength = 64;

    /// <summary><c>outbound_orders.outbound_order_no</c> 列宽。</summary>
    public const int OutboundOrderNoColumnMaxLength = 100;

    /// <summary><c>supplier_return_requests.supplier_return_no</c> 列宽。</summary>
    public const int SupplierReturnNoColumnMaxLength = 300;

    /// <summary><c>backorder_orders.backorder_order_no</c> 列宽。</summary>
    public const int BackorderOrderNoColumnMaxLength = 100;

    /// <summary><c>warehouse_tasks.task_no</c> 列宽。</summary>
    public const int WarehouseTaskNoColumnMaxLength = 100;

    private WmsOperationalCodeKind(string prefix, params int[] carryingColumnMaxLengths)
    {
        Prefix = WmsText.Required(prefix, nameof(prefix)).ToUpperInvariant();
        if (carryingColumnMaxLengths.Length == 0)
        {
            throw new ArgumentException("必须至少给出一个承载列宽。", nameof(carryingColumnMaxLengths));
        }

        // 一个单号被同时写进多列时，有效上界是这些列宽的最小值。
        MaxLength = carryingColumnMaxLengths.Min();
        var fallbackLength = Prefix.Length + 1 + HashHexLength;
        if (MaxLength < fallbackLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(carryingColumnMaxLengths),
                MaxLength,
                $"前缀 '{Prefix}' 的回落形态需要 {fallbackLength} 个字符，最窄的承载列只放得下 {MaxLength} 个。");
        }
    }

    /// <summary>单号前缀，已规范化为大写。</summary>
    public string Prefix { get; }

    /// <summary>构造上界 = 承载列宽的最小值。</summary>
    public int MaxLength { get; }

    /// <summary>
    /// 退供单号：它既是 <c>supplier_return_no</c>，又被退供派生出库单原样当作
    /// <c>outbound_order_no</c> 使用，故承载列清单是两列。
    /// </summary>
    public static WmsOperationalCodeKind SupplierReturn { get; } =
        new("RTS", SupplierReturnNoColumnMaxLength, OutboundOrderNoColumnMaxLength);

    /// <summary>缺量单号：只落 <c>backorder_order_no</c> 一列。</summary>
    public static WmsOperationalCodeKind Backorder { get; } =
        new("BO", BackorderOrderNoColumnMaxLength);

    /// <summary>补货建议任务号：只落 <c>warehouse_tasks.task_no</c> 一列。</summary>
    public static WmsOperationalCodeKind ReplenishmentTask { get; } =
        new("RPL", WarehouseTaskNoColumnMaxLength);

    /// <summary>
    /// 仅供契约测试使用的构造探针：让「上界放不下回落形态时就地拒绝」这条不变量可被直接检验，
    /// 而不必为了检验它去声明一个真实的单号种类。
    /// </summary>
    internal static WmsOperationalCodeKind CreateForContractProbe(string prefix, params int[] carryingColumnMaxLengths)
    {
        return new WmsOperationalCodeKind(prefix, carryingColumnMaxLengths);
    }
}
