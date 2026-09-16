namespace Nerv.IIP.Business.Erp.Web.Application.Validation;

/// <summary>
/// 记账凭证号列宽的唯一数字出处（#3229 → #3278 / S8 退役后的残留面）。
/// </summary>
/// <remarks>
/// <para>
/// <b>本类型今天只剩一件事</b>：给 <c>PostJournalVoucherCommandValidator</c> 的
/// <c>RuleFor(x =&gt; x.VoucherNo).MaximumLength(...)</c> 提供上界，使「校验器放行的长度」与
/// <c>journal_vouchers.voucher_no</c> 的列宽同源，不许两边各自手抄 100。
/// 两侧相等由 <c>ErpVoucherNoLengthContractTests.Voucher_no_column_width_matches_the_policy_constant</c>
/// 从 <b>EF 模型</b>读出后对撞——单边改 <c>HasMaxLength</c> 或单边改本常量都会红。
/// </para>
/// <para>
/// ⭐ <b>#3278 / S8 拆掉了什么，为什么</b>。本类型改前还承担「派生凭证号的拼接入口」
/// （<c>Compose</c> / <c>Digest</c> / <c>CanonicalKey</c> 与 <c>VoucherFamily</c> 闭集），
/// 用来把「固定前缀 + 上游单号」这种必然越界的构造式兜回列宽之内（越界退定长摘要式）。
/// S6（PR #3495，命令侧 10 个位点）与 S7（PR #3496，集成事件消费侧 5 个位点）落地后，
/// <b>凭证号一律取 <c>journal-voucher</c> 规则的分配器短号</b>（<c>JV-yyyyMMdd-NNNNNN</c>，定长 18），
/// 派生构造在生产侧调用点归零 ⇒ 「派生串会越界」这个缺陷形状本身不再存在，
/// 兜底逻辑与它的全部断言一并退役。⛔ 这不是「那些断言碍事」，是它们要证的事已不存在。
/// </para>
/// <para>
/// <b>本常量不能证明什么（⛔ 别读成完备）</b>：
/// <list type="number">
/// <item>它只约束 <c>PostJournalVoucherCommand</c> 这一个<b>调用方给号</b>的入口。
///   其余建凭证位点的号来自分配器（定长 18），不经本常量。</item>
/// <item>它读的是 EF 模型，不是迁移脚本；模型/迁移漂移不由本类负责。</item>
/// <item>它不保证「所有凭证号都合法」——绕开命令直接 <c>JournalVouchers.Add</c>
///   或原生 SQL 写入都够不着校验器；落库那一层的兜底是列宽本身的 <c>22001</c>。</item>
/// <item>seed（<c>WorldHistorySeedService</c>）的两处凭证按 #3278「显式不做」仍直接写
///   <c>JV-2026-S{n}</c> / <c>JV-2026-C{n}</c> ⇒ 库里并存<b>三种</b>凭证号格式：
///   分配器短号、存量派生号、种子号。</item>
/// </list>
/// </para>
/// </remarks>
internal static class ErpVoucherNoPolicy
{
    /// <summary>
    /// 凭证号列宽。EF 侧仍写死 <c>HasMaxLength(100)</c>（迁移的真相在那边），
    /// 由契约测试从 EF 模型读出后断言两侧相等，任一单边改动即红。
    /// </summary>
    public const int ColumnMaxLength = 100;
}
