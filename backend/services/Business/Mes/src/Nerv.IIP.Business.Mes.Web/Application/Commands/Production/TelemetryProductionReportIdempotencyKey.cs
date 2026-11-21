using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

/// <summary>
/// 使用源信封幂等键的完整 SHA-256 base64url 摘要生成定长遥测报工键。
/// </summary>
/// <remarks>
/// 消费与晋升必须对同一来源事实生成相同字节。信封键标识事实，事件 ID 标识投递。
/// 旧的裸源键与摘要键不相等。消费端由同事务 inbox 挡重投，晋升端若内层报工已提交、
/// 候选确认中断，则依赖稳定派生键找回已提交报工。保留独立黄金向量及下游长度验证。
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
    /// <para><b>⚠️ 本方法对 <c>null</c> 会抛</b>，这是本改动**新增**的一条抛出路径
    /// （改前 <c>$"telemetry:{null}"</c> 得到 <c>"telemetry:"</c>，不抛）。**别写成「总函数」**。
    /// 穷举实跑过的其余输入一律不抛：空串 / 全空白 / 孤立代理对 / <c>NUL</c> / 1MB 长串
    /// 都照常求摘要（<c>Encoding.UTF8</c> 对非法代理对走替换回退）。</para>
    /// <para><b>它仍然不会在 CAP 消费者里变成毒消息，理由是 <c>null</c> 在两个调用点都不可达</b>
    /// —— 理由点名到位点，别只写「上游会挡」：
    /// <list type="bullet">
    /// <item><b>消费侧</b>：<c>IntegrationEventConsumerGuard.HandleAsync</c> 在进
    /// <c>HandleValidEventAsync</c> **之前**跑 <c>IntegrationEventEnvelopeValidator</c>
    /// （<c>IntegrationEventReliability.cs:193</c>），其 <c>GetRequiredStringFields</c> 含
    /// <c>IdempotencyKey</c>（同文件 <c>:171</c>），null/空白一律 dead-letter 后 <c>return</c>
    /// （<c>:196-203</c>），根本到不了调用点。
    /// ⚠️ <b>失效方向</b>：谁要是把这个消费者从 Guard 里摘出去直接订阅，这道闸就没了，必须同步补。</item>
    /// <item><b>晋升侧</b>：<c>TelemetryProductionReportCandidate.SourceIdempotencyKey</c> 是非空
    /// <c>string</c>，构造时过 <c>DomainGuard.Required</c>（该聚合 <c>:46</c>），且列是
    /// <c>IsRequired()</c>。</item>
    /// </list></para>
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
