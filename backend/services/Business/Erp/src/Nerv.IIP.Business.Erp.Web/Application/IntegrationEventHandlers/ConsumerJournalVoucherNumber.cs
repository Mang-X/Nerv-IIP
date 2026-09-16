using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Coding;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

/// <summary>
/// 一次凭证号分配的结果。<see cref="Code"/> 为 <see langword="null"/> 表示**没拿到号**，
/// 调用方必须 gate-and-skip（写死信 + 不建凭证），⛔ 不得继续往下走。
/// </summary>
/// <param name="Code">分配到的凭证号；失败时为 <see langword="null"/>。</param>
/// <param name="FailureMessage">失败原因；成功时为空串。</param>
internal readonly record struct JournalVoucherNumberAllocation(string? Code, string FailureMessage);

/// <summary>
/// 集成事件消费侧建凭证时取凭证号的**唯一入口**（GitHub #3278 / S7）。
/// </summary>
/// <remarks>
/// <para>
/// <b>改前</b>：消费侧 5 个建凭证位点的凭证号都由 <c>ErpVoucherNoPolicy.Compose</c> 从上游单号派生
/// （⚠️ 该入口已随 #3278 / S8 一并删除，此处只是叙述改前形状，⛔ 别去源码里找它）
/// （<c>JV-GRIR-{收货单号}</c> / <c>JV-PRTN-{退货单号}</c> / <c>JV-CN-{红字号}</c> /
/// <c>JV-WOC-{工单号}-{移动号}</c> / <c>JV-WOCADJ-{工单号}-{来源号}</c>）。
/// <b>改后</b>：一律走 <c>CodeAllocator</c> 的 <c>journal-voucher</c> 规则，得 <c>JV-yyyyMMdd-NNNNNN</c>（定长 18）。
/// </para>
/// <para>
/// <b>为什么可以这么改</b>：#3278 / S5（merge <c>f14f2cc5d</c>）已把幂等语义从凭证号搬到
/// <c>(organization_id, environment_id, source_type, source_no)</c> 上的 partial unique index
/// （见 <c>JournalVoucherEntityTypeConfiguration</c> 与迁移
/// <c>20260915033607_AddJournalVoucherSourceDocumentUniqueIndex</c>）。
/// 凭证号今天只是账面显示，不再是任何一条查重谓词的键。
/// ⚠️ <c>(organization_id, environment_id, voucher_no)</c> 上那条唯一索引**仍在**，
/// 所以分配器产出的号仍必须互异——这由分配器计数器（scope 含 org/env/ruleKey/resetKey）保证，
/// ⛔ 不由本类型保证。
/// </para>
/// <para>
/// <b>幂等键取 <c>(来源类型, 来源单号)</c>，⛔ 不取事件的 <c>IdempotencyKey</c></b>。
/// 理由不是「<c>ErpReturnIntegrationEventHandlers.cs:247</c> 已经这么写」——那两处分配的是**退货单号 /
/// 借项通知单号**，一个事件产一张单，事件身份与单据身份同粒度；而凭证的身份是 S5 钉死的
/// <c>(source_type, source_no)</c>：同一来源单据可能由**不同事件**（不同 <c>EventId</c> /
/// <c>IdempotencyKey</c>）触达消费者，此时事件键会算成两次分配，来源键才与「至多一张凭证」同粒度。
/// ⭐ 这条选择还顺带解决了 <c>WorkOrderCostIntegrationEventHandlers</c> 两处的难点：
/// <c>CostVariancePosting.PostLateAdjustmentAsync</c> 是静态辅助、被 5 个调用点共用，手里根本没有事件信封，
/// 但 <c>(WOCADJ, sourceId)</c> 就在它的参数里。
/// </para>
/// <para>
/// ⚠️ <b>与 S6（PR #3495）的重叠，已登记</b>：S6 在
/// <c>Application/Commands/JournalVoucherNoAllocation.cs</c> 建了命令侧的同职责取号入口。
/// 两者**不是同一个类型也不在同一个文件**（无 add/add 冲突），但确实是一件事两套实现。
/// ⭐ <b>本入口多出来的那一件事是失败形态</b>：S6 的 <c>AllocateAsync</c> 返回 <c>Task&lt;string&gt;</c>、
/// 不捕获任何异常——命令处理器里那是对的（异常返回给调用方），
/// 但 CAP 消费者里它会逃逸成 poison message（#877 仍 OPEN）。
/// 合并顺序定下来后，本类可以退化成「调 S6 的 <c>AllocateAsync</c> + 一层 try/catch」的薄包装。
/// </para>
/// <para>
/// ⭐ <b>键是定长摘要，不是可读串——承重理由是「越界的失败形态本 gate 接不住」</b>。
///
/// <c>code_idempotency_keys.idempotency_key</c> 列宽
/// <see cref="CodeIdempotencyKey.IdempotencyKeyMaxLength"/> = 150。
/// 如果键写成可读的 <c>"{类型码}:{来源单号}"</c>，越界**不会在取号这一步炸**：
/// <list type="number">
/// <item><c>CodeIdempotencyKey</c> 的构造函数**不校长度**（<c>CodeEntities.cs</c>，逐字段直赋）；</item>
/// <item><c>CodeAllocator.AllocateAsync</c> 只调 <c>_store.AddIdempotencyRecord(...)</c>，
///   而 <c>EfCoreCodeStore.AddIdempotencyRecord</c> **只做 <c>DbSet.Add</c>、不 <c>SaveChanges</c>**
///   （<c>EfCoreCodeStore.cs:67-70</c>）；</item>
/// <item>于是 PostgreSQL <c>22001</c> 要等到**调用方那次 <c>SaveEntitiesAsync</c>** 才抛，
///   那已经在 <see cref="TryAllocateAsync"/> 的 <c>try</c> 块**之外**。</item>
/// </list>
/// ⇒ 裸拼式一旦越界，本类的 gate-and-skip **接不住**，异常会逃逸出消费者成为 poison message——
/// 而那正是本类存在要消灭的那一种失败形态。摘要式让这条路径**结构上不存在**：
/// <c>jv:{类型码}:{SHA-256 十六进制}</c> 的上界是 3+32+1+64 = <b>100</b>（类型码按列宽 32 顶格算），
/// 与来源单号长度无关，没有「合得下 / 合不下」两种形态，也就没有回落分支。
/// 这条上界由 <c>ConsumerJournalVoucherNumberKeyContractTests</c> 从
/// <see cref="JournalVoucherSourceType.All"/> 闭集 + EF 模型读出的两个列宽对撞，不靠人手抄。
/// </para>
/// <para>
/// ⚠️ <b>⛔ 别把上面那条读成「今天的输入会溢出」——今天 5 个位点全部合得下裸拼式</b>。
/// 逐位点的来源单号上界，以及它**是哪一档事实**：
/// <list type="bullet">
/// <item>① <c>GRIR</c> ← <c>PurchaseReceiptNo</c>：Erp 侧 <c>HasMaxLength(100)</c>，
///   **列宽约束**（最硬）⇒ 裸拼 4+1+100 = <b>105</b>；</item>
/// <item>② <c>PRTN</c> ← <c>PurchaseReturnNo</c>：同上 ⇒ <b>105</b>；</item>
/// <item>③ <c>CN</c> ← <c>CreditNoteNo</c>：同上 ⇒ <b>103</b>；</item>
/// <item>④ <c>WOC</c> ← <c>payload.InventoryMovementId</c>：**类型级事实**（次硬）——
///   全仓唯一发布方 <c>InventoryIntegrationEventConverters.cs:15-30</c> 写的是
///   <c>movementId.ToString()</c>，而 <c>StockMovementId</c> 是 <c>IGuidStronglyTypedId</c>
///   ⇒ 恒 36 字符 ⇒ 裸拼 3+1+36 = <b>40</b>。
///   ⚠️ 它**不是列宽约束**：Erp 侧对这个 payload 字段没有任何长度校验，
///   消费侧也没有入站校验器（<c>IntegrationEventEnvelopeValidator</c> 只查信封字段非空，
///   不查 payload 字段长度）。⛔ 也别去引 WMS 的
///   <c>inventory_movement_id HasMaxLength(150)</c> 当上界——那是 WMS **自己消费侧存副本**的列，
///   在本链路的**下游**，对 ERP 收到的 payload 零约束；</item>
/// <item>⑤ <c>WOCADJ</c> ← 7 种表达式，最宽 <c>machine-{OperationTaskId:100}-r{long:19}-void</c> = 134
///   （读数出处：<c>ErpVoucherNoLengthContractTests.WidestAdjustmentSourceIdWidth</c>）
///   ⇒ 裸拼 6+1+134 = <b>141</b>，只剩 9 字符余量；同样先经 payload、无入站校验。</item>
/// </list>
/// ⇒ 摘要式的价值**不在**「修一个今天可达的溢出」（没有这样的溢出），
/// 而在上面那条 A3：⑤ 那 9 字符余量一旦被 <c>OperationTaskId</c> 列宽或 <c>source_no</c> 列宽的变动吃掉，
/// 失败形态就是**本 gate 接不住的那一种**，而不是一条会红的断言。
/// ⭐ 代价登记：排障时读不出那一行对应哪张来源单据（只看得出族）。
/// ⛔ 本票**没有**做「合得下用裸拼 / 合不下退摘要」那种两形态回落——
/// 两形态意味着多一条只在顶格输入下才走的分支，而那条分支今天在消费侧不可达。
/// </para>
/// <para>
/// ⚠️ <b>分段歧义</b>：摘要输入是**带长度前缀、以 U+001F 分隔**的规范串，
/// 所以不同的 <c>(类型, 单号)</c> 划分不可能拼出同一个输入，只剩 SHA-256 碰撞。
/// </para>
/// <para>
/// <b>指纹与键同源</b>：<c>PayloadFingerprint</c> 直接取同一个摘要。
/// <c>CodeAllocator.ToReplay</c> 在「同键不同指纹」时抛 <see cref="KnownException"/>；
/// 指纹是幂等键的函数 ⇒ 同键必同指纹 ⇒ 这条异常路径在本入口**结构上不可达**
/// （只有外部直接篡改 <c>code_idempotency_keys</c> 行才够得到，用例正是这么制造失败的）。
/// ⛔ 别把业务量（金额、差异额、过账日）塞进指纹：迟到调整的 <c>costDelta</c> 依赖累计状态，
/// 重投时算出的值可能与首投不同，塞进去会把一条本不存在的抛出路径造出来。
/// </para>
/// <para>
/// <b>失败形态与失效方向</b>：本入口把 <see cref="CodeConcurrencyException"/>（计数器抢占重试耗尽）
/// 与 <see cref="KnownException"/>（规则停用、以及被篡改的幂等键行造成的指纹冲突）收成 <c>Code == null</c>，
/// 让调用方走 gate-and-skip —— CAP 消费者里抛业务异常会逃逸成 poison message（#877 仍 OPEN）。
/// ⛔ <b>不</b>捕获 <see cref="OperationCanceledException"/>：取消不是业务失败，必须原样冒泡。
/// ⚠️ <b>另一条不在 catch filter 里的异常</b>：<c>StandardCodeRules.Get</c> 对未登记的规则键抛
/// <see cref="KeyNotFoundException"/>，它**不在**上面那两类里 ⇒ <see cref="RuleKey"/> 写错会逃逸成 poison。
/// 没有把它收进来：那是一个 <c>const</c>，写错在 happy path 第一条用例就全红（实测：
/// 把 <see cref="RuleKey"/> 改成 <c>"purchase-order"</c> 后面板红 8），把它当业务失败吸掉反而会把配置错误变成静默死信。
/// ⚠️ 失效方向：调用方拿到 <c>Code == null</c> 后若仍继续建凭证，编译期不会拦——
/// 兜住这个方向的是各调用点的用例，不是本类型。
/// </para>
/// </remarks>
internal static class ConsumerJournalVoucherNumber
{
    /// <summary><c>StandardCodeRules</c> 里记账凭证的规则键，产出 <c>JV-yyyyMMdd-NNNNNN</c>。</summary>
    public const string RuleKey = "journal-voucher";

    /// <summary>死信失败码：凭证号没分配下来，本次不建凭证。</summary>
    public const string AllocationFailureCode = "voucher-number-allocation-failed";

    /// <summary>幂等键的固定前缀。</summary>
    public const string KeyPrefix = "jv:";

    /// <summary>SHA-256 转大写十六进制后的固定长度。</summary>
    public const int DigestLength = 64;

    /// <summary>规范串里的段分隔符，取 ASCII 单元分隔符（US, U+001F）。</summary>
    private const char CanonicalSeparator = '';

    /// <summary>
    /// 分配器幂等键：<c>jv:{来源类型码}:{SHA-256}</c>。
    /// 与 S5 的 partial unique index 同粒度 <c>(source_type, source_no)</c>——
    /// org/env 不必写进串里，分配器自己按 org/env/ruleKey 分桶。
    /// </summary>
    public static string IdempotencyKeyOf(JournalVoucherSourceType sourceType, string sourceNo)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNo);
        return string.Concat(KeyPrefix, sourceType.Code, ":", Digest(sourceType, sourceNo));
    }

    /// <summary>
    /// 摘要输入的规范串：两段各自前置十进制长度、以 U+001F 分隔，
    /// 故不同的段划分不可能拼出同一个输入。
    /// </summary>
    public static string CanonicalKey(JournalVoucherSourceType sourceType, string sourceNo)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(sourceNo);
        return new StringBuilder()
            .Append(sourceType.Code.Length.ToString(CultureInfo.InvariantCulture))
            .Append(CanonicalSeparator)
            .Append(sourceType.Code)
            .Append(CanonicalSeparator)
            .Append(sourceNo.Length.ToString(CultureInfo.InvariantCulture))
            .Append(CanonicalSeparator)
            .Append(sourceNo)
            .ToString();
    }

    public static string Digest(JournalVoucherSourceType sourceType, string sourceNo)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalKey(sourceType, sourceNo))));
    }

    public static async Task<JournalVoucherNumberAllocation> TryAllocateAsync(
        ErpCodingService codingService,
        string organizationId,
        string environmentId,
        JournalVoucherSourceType sourceType,
        string sourceNo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codingService);
        ArgumentNullException.ThrowIfNull(sourceType);
        try
        {
            var allocation = await codingService.AllocateAsync(
                organizationId,
                environmentId,
                RuleKey,
                requestedCode: null,
                IdempotencyKeyOf(sourceType, sourceNo),
                // 指纹取同一个摘要：同键必同指纹，指纹冲突分支在本入口不可达。
                Digest(sourceType, sourceNo),
                cancellationToken);
            return new JournalVoucherNumberAllocation(allocation.Code, string.Empty);
        }
        catch (Exception exception) when (exception is CodeConcurrencyException or KnownException)
        {
            return new JournalVoucherNumberAllocation(
                null,
                $"Journal voucher number could not be allocated for source '{sourceType.Code}'/'{sourceNo}': {exception.Message}");
        }
    }
}
