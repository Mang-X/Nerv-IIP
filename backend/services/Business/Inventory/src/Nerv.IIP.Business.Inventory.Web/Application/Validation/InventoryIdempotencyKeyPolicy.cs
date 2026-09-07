namespace Nerv.IIP.Business.Inventory.Web.Application.Validation;

/// <summary>
/// 幂等键「列宽 / 校验器上界 / handler 落库前追加量」三者关系的唯一出处（#3176）。
/// </summary>
/// <remarks>
/// 缺陷形状：<c>idempotency_key</c> 列宽 128，校验器也按 128 放行，但 handler 在落库前给键追加
/// <c>:out</c> / <c>:in</c> / <c>:part-N</c>。于是**有效上界不是列宽本身，而是「列宽 − 该写面最长后缀」**；
/// 125–128 字符的合法键通过校验、落库时炸 <c>22001 value too long</c>。
///
/// 因此本类型承担两件事：
/// 1. <see cref="BaseMaxLengthFor"/>——校验器上界由「列宽 − 后缀字面量长度」算出，不许再手抄数字；
/// 2. <see cref="Compose"/>——落库前拼接的入口，超宽时就地拒绝。
///
/// **失败形态的变化（实测口径，不要读成「不再逃逸」）**：改前是 <c>DbUpdateException</c>（Npgsql 22001），
/// 改后是 <c>KnownException</c>。本仓 <c>backend/common/Messaging</c> 下**没有任何** <c>catch (KnownException)</c>，
/// 生产侧也**没有** <c>ISubscribeFilter</c>——所以 <c>KnownException</c> 在 CAP 消费者内**同样会逃逸**（#877 同族）。
/// 本类型改的是**失败形态**（500 崩溃 → 400 可归因拒绝），**不是**「不再逃逸」。
///
/// **由哪条读数看守（实测强度，不是完备性主张）**：
/// 互不重叠的方向按「生产侧 / 测试侧」分列——**测试侧只证明「护栏不会静默缴械」，
/// 不给生产代码增加鉴别力，两者不混计**。
///
/// 生产侧 12 个：① 列宽 = <see cref="ColumnMaxLength"/>；② 状态调拨校验器上界由派生得来；
/// ③ FEFO 校验器上界由派生得来；④ 状态调拨「上界 + 最长后缀 == 列宽」；⑤ FEFO 同上（反手抄）；
/// ⑥ <see cref="Compose"/> 越界就地拒绝；⑦ <see cref="Compose"/> 调用点闭集；⑧ 绕过面闭集
/// （⑦⑧ 实测红在**不同**断言上，不是一条）；⑨ FEFO 腿序号上限真被执行；
/// ⑩ <c>count-code:</c> 前缀键的**真实构造入口**（不是两个常量之间的算术）；
/// ⑪ 过期封锁的重入探针与状态调拨写面常量同源（**此格需两变量发散才红**：两个常量今天同值，
/// 只改绑定或只改后缀都不红，故不算单变量防线）；⑫ 幂等键列集合非空且计数封闭。
///
/// 测试侧 2 个：登记集计数封闭；检测分支不可被单边删除（半失配曾经全绿）。
///
/// **单纯把某个后缀字面量改长（上界仍由 <see cref="BaseMaxLengthFor"/> 派生）不会红，也不应该红**
/// ——那种改法上界会自动跟着变，行为仍然正确。
/// </remarks>
internal static class InventoryIdempotencyKeyPolicy
{
    /// <summary>
    /// 幂等键列宽。EF 侧仍各自写死 <c>HasMaxLength(128)</c>（迁移的真相在那边），
    /// 由契约测试从 EF 模型闭集枚举后断言两侧相等，任一单边改动即红。
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
    /// 落库前给幂等键追加后缀的入口：绝不截断（截断会把仅末几位不同的两个键折叠成同一个），
    /// 超出列宽就地抛 <c>KnownException</c>，而不是把越界值送进数据库换一个 22001。
    /// **不要把这里读成「<c>Application/</c> 下所有拼接都走这里」**——那是完备性主张，已知为假。
    /// 源码闭集扫描实际断言的是**两类形状**：`+` 拼接（**单行与跨行都算、左右操作数都算**）
    /// 与插值串续写（前缀后缀都算）。**以下在构造上不在扫描视野内，是已知缺口**：
    /// <c>string.Concat</c> / <c>string.Format</c> / <c>StringBuilder.Append</c> / <c>Span</c> 拼接；
    /// 经**局部别名**转手后再跟**非字面量**后缀（`var k = x.IdempotencyKey; k + SomeSuffix;`）；
    /// 以及别名转手后再插值（语句里已不含 <c>IdempotencyKey</c> 一词，纯文本扫描追不到）。
    /// 覆盖后两类需要数据流分析，本仓不做——**加新写面时别指望扫描替你兜底，请显式走本方法。**
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
