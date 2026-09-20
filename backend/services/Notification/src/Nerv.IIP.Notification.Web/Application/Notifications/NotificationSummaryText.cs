using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;

namespace Nerv.IIP.Notification.Web.Application.Notifications;

/// <summary>
/// 告警摘要的渲染上界（#3305）。
/// </summary>
/// <remarks>
/// <para>⚠️ <b>本类型的生产职责已被 #3346 / PR #3364 接手，以下「要解决的形状」是它被写出来时的历史背景。</b>
/// 当时那条路径上确实没有任何长度闸：消费者走 <c>sender.Send(SubmitNotificationIntentCommand)</c>，
/// **不过** HTTP 端点上那个 <c>Validator&lt;SubmitNotificationIntentRequest&gt;</c>；
/// <c>NotificationIntent</c> 构造器只 <c>Required</c> 不截断；<c>IntegrationEventConsumerGuard</c>
/// 只对**信封**校验失败写死信，handler 抛的异常原样逃逸 ⇒ 超长摘要以 <c>DbUpdateException</c>
/// （Npgsql 22001）逃出消费者、被 CAP 重试到 poison，表现是「告警永远送不到收件人，而上游服务写库是成功的」。</para>
///
/// <para><b>现状（合并 #3364 之后）</b>：那道闸<b>已经有了，但不在这里</b>——
/// <c>SubmitNotificationIntentCommand</c> 不收裸 <c>string</c>，进程内拼装走
/// <c>NotificationSummary.Render</c>（夹紧）、外部提交走 <c>NotificationSummary.FromSubmitted</c>（超界抛），
/// 上界由 <c>NotificationSummaryBudget.FromModel</c> 派生。
/// ⇒ <b><see cref="ResolveSummaryMaxLength"/> 目前没有任何生产调用方</b>；
/// <b><see cref="Fit"/> 在生产路径上只以恒等形态承重</b>（handler 传 <see cref="int.MaxValue"/>，
/// 截断分支生产不可达）。本类型与它的用例仍在测真实行为，但<b>它们守的那条路径生产已经不走了</b>——
/// 是否用 <c>NotificationSummaryBudget</c> 收编掉本类型属 #3305 的设计取舍，不在解冲突范围。</para>
///
/// <para><b>为什么截断责任在本服务这一侧</b>：摘要是**渲染产物**，原文留在产出它的服务里
/// （WCS 诊断报文的原文在 <c>business_wms.wcs_tasks.failure_message</c>，那一列已按 #3305 改为无界
/// <c>text</c>），operator 可以顺着 <c>ResourceId</c> 下钻看全文。
/// 反过来让上游按本服务的列宽产出，等于把 Notification 的 schema 绑进每个上游服务。</para>
///
/// <para><b>不做的事</b>：不改 <c>Summary</c> 的列宽（2000 对一条摘要是合理的），
/// 不在 <c>NotificationIntent</c> 构造器里截断（那会让 HTTP 端点提交的摘要也被静默改写，
/// 而那条路径本就该由端点校验器拒绝，属另一张票）。</para>
/// </remarks>
public static class NotificationSummaryText
{
    /// <summary>
    /// 截断标记。**必须留在结果里**：读的人要能看出这条摘要被截过、全文得去上游服务取。
    /// U+2026 在 C# 里是 1 个 UTF-16 单元、在 Postgres 里算 1 个字符，两边计数一致。
    /// </summary>
    public const string TruncationMarker = "…";

    /// <summary>
    /// 一条摘要会落进的全部列。<b>这不是随手写的白名单</b>：
    /// <c>NotificationSummaryTextTests.Every_summary_column_in_the_model_is_a_declared_landing_column</c>
    /// 从 EF 模型反向枚举「名字叫 Summary 的字符串属性」，与本表双向相等——
    /// 将来谁再加一张带 <c>Summary</c> 的表而不登记，那条会红，而不是静默漏出第三个承载面。
    /// </summary>
    private static readonly (Type EntityType, string PropertyName)[] LandingColumns =
    [
        (typeof(NotificationIntent), nameof(NotificationIntent.Summary)),
        (typeof(NotificationMessage), nameof(NotificationMessage.Summary)),
    ];

    /// <summary>登记在案的承载列，供契约用例反向对撞。</summary>
    public static IReadOnlyList<(Type EntityType, string PropertyName)> DeclaredLandingColumns => LandingColumns;

    /// <summary>
    /// 摘要的有效上界 = 全部承载列宽的**最小值**（#3281 判据「一个值写进多列时有效上界取列宽最小值」）。
    /// 上界从 EF 模型现读，代码里不出现任何列宽字面量。
    /// </summary>
    /// <remarks>
    /// 某一列被改成无界（<c>GetMaxLength()</c> 为 <c>null</c>）时它退出这个最小值的竞争；
    /// 全部无界时返回 <see cref="int.MaxValue"/>，<see cref="Fit"/> 便退化为原样返回。
    /// </remarks>
    public static int ResolveSummaryMaxLength(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var bound = int.MaxValue;
        foreach (var (entityType, propertyName) in LandingColumns)
        {
            var property = model.FindEntityType(entityType)?.FindProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"Notification EF 模型里找不到承载列 {entityType.Name}.{propertyName}。");
            if (property.GetMaxLength() is { } maxLength && maxLength > 0 && maxLength < bound)
            {
                bound = maxLength;
            }
        }

        return bound;
    }

    /// <summary>
    /// 把摘要收进 <paramref name="maxLength"/>，超出部分换成 <see cref="TruncationMarker"/>。
    /// </summary>
    /// <remarks>
    /// <para>按 C# 的 <see cref="string.Length"/>（UTF-16 单元数）度量，而 Postgres 的
    /// <c>varchar(n)</c> 按字符数度量。补充平面字符在前者算 2、后者算 1，
    /// 因此这个口径**只会偏保守**，不会漏过溢出。</para>
    /// <para>切点落在代理对中间会产生孤立高代理项，编码成 UTF-8 时会失败；
    /// 所以切到高代理项就再退一位。</para>
    /// </remarks>
    public static string Fit(string summary, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, TruncationMarker.Length + 1);
        if (summary.Length <= maxLength)
        {
            return summary;
        }

        var keep = maxLength - TruncationMarker.Length;
        if (char.IsHighSurrogate(summary[keep - 1]))
        {
            keep--;
        }

        return string.Concat(summary.AsSpan(0, keep), TruncationMarker);
    }
}
