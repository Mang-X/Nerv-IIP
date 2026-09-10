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
/// 生产侧 9 个：① 列宽 = <see cref="ColumnMaxLength"/>；② 状态调拨校验器上界由派生得来
/// （**口径**：把该上界手抄成 <see cref="ColumnMaxLength"/> 时，红的是 ④ 与「顶格键仍塞得进列」
/// 那两条，②的具名承担者本身不红——它在 128/129 边界上照样自洽。②与④在那条变异下**不是两条
/// 独立读数**）；③ FEFO 校验器上界由派生得来；④ 状态调拨「上界 + 最长后缀 == 列宽」；
/// ⑤ FEFO 同上（反手抄）；⑥ <see cref="Compose"/> 越界就地拒绝；⑦ FEFO 腿序号上限真被执行；
/// ⑧ <c>count-code:</c> 前缀键的**真实构造入口**（不是两个常量之间的算术）；
/// ⑨ 过期封锁的重入探针与状态调拨写面常量同源（**此格需两变量发散才红**：两个常量今天同值，
/// 只改绑定或只改后缀都不红，故不算单变量防线）。
///
/// 测试侧 1 个：幂等键列集合非空且计数封闭。
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
    /// 本方法是幂等键落库前拼接的**唯一入口**。**这是约定，不是护栏**——
    /// 当前没有任何机制能在编译期阻止绕过（`key + ":out"` 照样编译得过）。
    ///
    /// **「换成不可拼接的包装类型」不是出路（#3231 实测读数，不是猜测，也不是待办）**：
    /// 包装类型加 <c>[Obsolete(error)] operator +</c> 只关得掉 <c>key + ":out"</c> 这一种写法；
    /// <c>$"{key}:out"</c>、<c>string.Concat(key, ":out")</c>、<c>string.Format("{0}:out", key)</c>、
    /// <c>key.ToString() + ":out"</c> 全部照样编译通过，且产出与本方法**完全相同**的键。
    /// 机制：netcorepal 的 <c>IStringStronglyTypedId</c> 生成器会生成
    /// <c>public override string ToString()</c>，而 <c>[Obsolete]</c> 打在 override 上只在**声明处**
    /// 报 CS0809，调用处不报错；实测唯一能全关的是 <c>readonly ref struct</c>，
    /// 而它不能作 EF 属性、不能作 record 成员、不能跨 <c>await</c>、不能作泛型实参、
    /// 不能被 JSON 序列化——对这个值不可用。
    ///
    /// 唯一可能覆盖全部写法的方向是把判据从文本挪到 **Roslyn 语义模型**上：「对该键类型的值做任何
    /// 字符串化或拼接」是 symbol 级判定，对换行、逐字插值、局部别名天然免疫。
    /// 这类判定在本仓**已有先例**——多个服务的边界 / 来源测试就是
    /// <c>CSharpCompilation.Create</c> + <c>MetadataReference</c> + <c>GetSemanticModel</c> 这一套
    /// （例如 <c>MesMaterialRequirementSnapshotBoundaryTests</c>，它连局部别名都跟踪），
    /// 可以照这条路做。但注意它们**跑在测试期，不是编译期**：把同一判定变成编译期 / IDE 里就报错
    /// 的形态是另一件事，在那之前本方法的「唯一入口」始终只是约定。
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
