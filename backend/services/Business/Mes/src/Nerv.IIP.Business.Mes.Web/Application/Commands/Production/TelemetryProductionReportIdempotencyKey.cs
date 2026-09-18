using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

/// <summary>
/// 遥测报工派生幂等键的唯一构造出处（#3477）。
/// </summary>
/// <remarks>
/// <para><b>缺陷形状</b>：两个调用点各写一份 <c>$"telemetry:{源信封键}"</c> 的纯拼接
/// （<c>TelemetryProductionReportCandidateCommands</c> 的确认晋升、
/// <c>TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport</c> 的直接过账）。
/// 拼接长度单调，而下游有两道 <b>150</b> 的墙：
/// <c>RecordProductionReportCommandValidator</c> 的 <c>RuleFor(x =&gt; x.IdempotencyKey).MaximumLength(150)</c>，
/// 以及这把键最终落进的 <c>code_idempotency_keys.idempotency_key</c>
/// （<see cref="CodeIdempotencyKey.IdempotencyKeyMaxLength"/>，由 <c>CodeAllocator</c> 原样存）。
/// 校验器在命令管道上更早，所以实际失败形态是 <c>KnownException</c> 而不是 22001。</para>
///
/// <para><b>可达性是实测的，不是算式推断</b>（这一点决定了有界派生在这里不是过度防御）：
/// 本仓自己的命名约定就越界 —— <c>org-001</c> / <c>env-dev</c> / <c>DEV-CNC-01</c>（WorldBibleSpec 设备编码段）
/// / <c>parts_count</c> / <c>seed-world-history</c>（<c>WorldHistoryDeviceSpec.SourceSystem</c>）
/// / <c>CONN-OPCUA-01</c>（同类 <c>OpcUaConnectorId</c>）
/// / <c>WorldHistorySeedService</c> 的 summary 序列形状 ⇒ 派生键 <b>182</b> 字符，被校验器拒。
/// 仓库测试夹具**最短**那一组也已经 114，占掉 76% 预算。
/// 对照本仓判例（<c>CodeEntityTypeConfigurations</c> 里 <c>payload_fingerprint</c> 那段注释）：
/// 「重启条件是**真的撞到**，不是『理论上可能』」—— 本处撞到了。</para>
///
/// <para><b>上界从链路哪一侧读</b>：</para>
/// <list type="bullet">
/// <item><b>汇端 150</b> = 上述两道墙取最小（#3281 判据）。本类型引
/// <see cref="CodeIdempotencyKey.IdempotencyKeyMaxLength"/> 这个**跨服务共享常量**，不手抄字面量；
/// 校验器那一道与它相等由 <c>TelemetryProductionReportIdempotencyKeyTests</c> 的行为探针（150 过 / 151 拒）钉住。</item>
/// <item><b>改前派生键最坏 522</b> = <c>"telemetry:"</c>(10) + 源信封键上界 512。
/// 512 来自 <c>IntegrationEventIdempotencyKey.Budget</c> —— 源端 converter 走
/// <c>IntegrationEventIdempotencyKey.Compose</c>，它自己已经在 512 处回落成摘要，
/// 所以源键的真上界是 512，**不是**七个身份段列宽相加得到的 953。
/// （七段列宽在 <c>telemetry_summaries</c> 上是 100/100/150/150/150/100/150 = 900，
/// 加上固定开销 53 得 953；那是**没有 Compose 时**的数，本类型不用它做上界。）</item>
/// <item><b>改后恒 <see cref="Length"/> = 53</b> = 前缀 10 + <see cref="DigestLength"/> 43，
/// 与输入长度**无关**。<see cref="DigestLength"/> 由 SHA-256 输出字节数经 base64url 编码长度派生，不手抄。</item>
/// </list>
///
/// <para><b>为什么是单形态摘要，而不是「装得下用裸拼、装不下退摘要」的两形态回落</b>
/// （本仓 <c>FinishedGoodsReceiptInventoryPostingKey</c> / <c>IntegrationEventIdempotencyKey</c> 是两形态）：
/// 那两处的硬约束是「存量键逐字保持」——下游按**精确键**查幂等行，键变形就查不到已落库的行。
/// 本处 owner 裁定取单形态，代价与收益写清楚：
/// <list type="number">
/// <item><b>收益</b>：同族的行形态一致（不会一半可读一半不可读），且不存在「两条出口产出同一个键」
/// 那类跨分支别名问题 —— 只有一条出口。</item>
/// <item><b>代价（失效方向，必须知道）</b>：今天已落库的 <c>telemetry:{裸源键}</c> 行
/// （即长度 ≤ 150 那一半，例如上面 114 那组）在本改动后**查不到了**。
/// 若同一条来源事实在改动前已记过报工、改动后又被重放，
/// <c>CodeAllocator</c> 会按新键判成首次分配、**再记一条报工**。
/// 这条只在「改动前后跨越同一条来源事实的重放」时成立；由 CAP inbox
/// （<c>MesProcessedIntegrationEventInbox</c>，按 eventId 或信封键精确相等）与候选表
/// <c>confirmed</c> 状态短路两道先挡，命令幂等键是第三道。</item>
/// </list></para>
///
/// <para><b>为什么不是「源键里省掉几段」</b>（更简单的候选，实测不成立）：
/// 七个身份段在源端的列宽合计 900，而固定开销 53 之后总预算只剩 97。
/// 即使砍掉 <c>sourceSystem</c>(100) 与 <c>sourceConnector</c>(150) 这两段，剩下五段上界仍是 650 ≫ 97。
/// 截断也不行：截断会把只在末几位不同的两把键折叠成同一个
/// （<c>InventoryIdempotencyKeyPolicy.Compose</c> 的注释写死禁止这件事），
/// 而这把键正是用来判「同一条来源事实」的。</para>
///
/// <para><b>为什么摘要输入是源信封键、而不是 eventId 或候选行主键</b>：
/// <c>eventId</c> 是**每次投递**的身份不是**每条事实**的身份（源端重发会换新 <c>eventId</c>，
/// 那会让同一条产量事实记两次报工）；候选行主键只在确认晋升那一侧存在，直接过账那一侧根本没有，
/// 拿它做键会让两个调用点对同一条来源事实产出**不同**的键。
/// 源信封键是本链路里唯一「一条事实一个值」的标识（CAP inbox 也正是按它判重投）。</para>
///
/// <para><b>确定性</b>：SHA-256 是纯函数，输入只有源信封键 —— 没有时钟、没有 GUID、没有 salt、没有进程状态。
/// 同一条来源事实跨 CAP 重投求两次键必然逐字节相同。用例里用**外部独立算出**的黄金向量钉住
/// （<c>printf ... | openssl dgst -sha256 -binary | basenc --base64url</c>），
/// 不是「先用本实现求值再用本实现复算」的自指断言。</para>
///
/// <para><b>本类型不证明什么</b>：</para>
/// <list type="number">
/// <item>不证明 <c>telemetry:</c> 这个前缀没有别的产出方。
/// 实跑 <c>git grep -n "telemetry:" -- '*.cs' '*.ts' '*.vue'</c> 后，写进报工幂等键的位点只剩
/// <see cref="Prefix"/> 这一处字面量（另外三类命中与本链路无关：Mes / Maintenance 两份
/// <c>WorldHistoryDeviceSpec</c> 里 <c>WorldHistoryRandom</c> 的随机种子串、以及一个 C# 命名实参
/// <c>telemetry:</c>）；同一组扫描对 <c>StartsWith("telemetry</c> 一族**零命中**，
/// 即今天没有任何下游解析这个前缀。
/// <b>扫描面是字面量</b>，有人把前缀抽成别处常量再拼就扫不到，别把它读成穷举。</item>
/// <item>不证明 HTTP 调用方造不出撞车的键。手工报工的 <c>IdempotencyKey</c> 由调用方给，
/// 调用方本来就能送 <c>telemetry:</c> 开头的任意串 —— 这是改动前就有的性质，本类型未引入也未消除。</item>
/// <item>不管辖那两道 150 本身。它们是受治理的值，本类型只保证自己的产出装得进去。</item>
/// </list>
/// </remarks>
public static class TelemetryProductionReportIdempotencyKey
{
    /// <summary>可读字面前缀，用于排障时按前缀定位这一族键。改动前后逐字相同。</summary>
    public const string Prefix = "telemetry:";

    /// <summary>摘要形态的字符数，由 SHA-256 输出字节数经 base64url 编码长度**派生**，不手抄。</summary>
    public static readonly int DigestLength = Base64Url.GetEncodedLength(SHA256.HashSizeInBytes);

    /// <summary>
    /// 汇端预算：两道 150 取最小（#3281 判据）。引共享编码实体常量而不是抄字面量。
    /// </summary>
    public static int Budget => CodeIdempotencyKey.IdempotencyKeyMaxLength;

    /// <summary>
    /// 本类型产出的键长，**定长**、与输入无关。
    /// <para><see cref="EnsureFitsBudget"/> 是首次触碰类型才跑的兜底（C# 静态初始化是惰性的，
    /// 不是启动期护栏）；真正更早报红的是 <c>TelemetryProductionReportIdempotencyKeyTests</c> 的上界断言。</para>
    /// </summary>
    public static int Length { get; } = EnsureFitsBudget();

    /// <summary>
    /// 由源信封幂等键派生报工幂等键。
    /// <para><b>总函数，不抛</b>：调用点在 CAP 消费者里，任何新增的抛出路径都会变成毒消息。
    /// 空值在到达这里之前已被上游排除（消费侧 <c>MesProcessedIntegrationEventInbox.TryRecordAsync</c>
    /// 对信封键做 <c>ThrowIfNullOrWhiteSpace</c>；晋升侧 <c>source_idempotency_key</c> 是必填列），
    /// 本方法对空串也只是照常求摘要。</para>
    /// </summary>
    public static string From(string sourceIdempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(sourceIdempotencyKey);
        return Prefix + Base64Url.EncodeToString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sourceIdempotencyKey)));
    }

    private static int EnsureFitsBudget()
    {
        var length = Prefix.Length + DigestLength;
        return length <= Budget
            ? length
            : throw new InvalidOperationException(
                $"遥测报工派生幂等键需要 {length} 个字符，下游预算只放得下 {Budget} 个。");
    }
}
