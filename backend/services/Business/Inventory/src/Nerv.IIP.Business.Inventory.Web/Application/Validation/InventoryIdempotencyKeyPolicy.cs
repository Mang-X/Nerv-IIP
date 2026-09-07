namespace Nerv.IIP.Business.Inventory.Web.Application.Validation;

/// <summary>
/// 幂等键「列宽 / 校验器上界 / handler 落库前追加量」三者关系的唯一出处（#3176）。
/// </summary>
/// <remarks>
/// 缺陷形状：<c>idempotency_key</c> 列宽 128，校验器也按 128 放行，但 handler 在落库前给键追加
/// <c>:out</c> / <c>:in</c> / <c>:part-N</c>。于是**有效上界不是列宽本身，而是「列宽 − 该写面最长后缀」**；
/// 125–128 字符的合法键通过校验、落库时炸 <c>22001 value too long</c>，且该异常是 <c>DbUpdateException</c>
/// 而非 <see cref="KnownException"/>，不被 <c>AddKnownExceptionErrorModelInterceptor</c> 覆盖，会逃逸出 CAP 消费者。
///
/// 因此本类型同时承担两件事：
/// 1. <see cref="BaseMaxLengthFor"/>——校验器上界由「列宽 − 后缀字面量长度」算出，不许再手抄数字；
/// 2. <see cref="Compose"/>——落库前拼接的唯一入口，超宽时以 <see cref="KnownException"/> 就地拒绝（400），
///    而不是把越界值送进数据库换一个逃逸出消费者的 <c>DbUpdateException</c>。
///
/// 三者关系由 <c>InventoryIdempotencyKeyLengthContractTests</c> 钉住：改列宽 / 改校验器上界 /
/// 改后缀（或绕过 <see cref="Compose"/>）任一，都会红。
/// </remarks>
public static class InventoryIdempotencyKeyPolicy
{
    /// <summary>
    /// 幂等键列宽。EF 侧仍各自写死 <c>HasMaxLength(128)</c>（迁移的真相在那边），
    /// 由契约测试断言两侧相等，任一单边改动即红。
    /// </summary>
    public const int ColumnMaxLength = 128;

    /// <summary>
    /// 该写面的基础幂等键上界 = 列宽 − 本写面 handler 可能追加的最长后缀。
    /// <paramref name="suffixes"/> 必须是 handler 真正拿去拼接的那些字面量本身。
    /// </summary>
    public static int BaseMaxLengthFor(params string[] suffixes)
    {
        ArgumentNullException.ThrowIfNull(suffixes);
        if (suffixes.Length == 0)
        {
            return ColumnMaxLength;
        }

        return ColumnMaxLength - suffixes.Max(suffix => suffix.Length);
    }

    /// <summary>
    /// 落库前给幂等键追加后缀的唯一入口：绝不截断（截断会把仅末几位不同的两个键折叠成同一个），
    /// 超出列宽就地抛 <see cref="KnownException"/>。
    /// </summary>
    public static string Compose(string idempotencyKey, string suffix)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        ArgumentNullException.ThrowIfNull(suffix);
        if (idempotencyKey.Length + suffix.Length > ColumnMaxLength)
        {
            throw new KnownException("幂等键追加腿后缀后超出长度上限，请缩短幂等键后重试。");
        }

        return idempotencyKey + suffix;
    }
}
