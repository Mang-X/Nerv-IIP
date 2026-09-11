using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Application.Notifications;

/// <summary>
/// 摘要值会被原样写进的一列。新增承载列必须登记在
/// <see cref="NotificationSummaryBudget.CarryingColumns"/>；测试会用「从 EF 模型反向枚举」与该名单对撞。
/// </summary>
public sealed record NotificationSummaryCarryingColumn(Type ClrType, string PropertyName)
{
    public string Key => $"{ClrType.Name}.{PropertyName}";
}

/// <summary>
/// 通知摘要的承载上界。
/// <para>
/// 上界从 EF 模型现读，并取全部承载列的最小值（一个值写进多列时，有效上界是列宽最小值）。
/// 本类型与它的调用方都不出现列宽字面量：改列宽只改 EntityConfiguration，夹紧点自动跟随。
/// </para>
/// </summary>
public sealed class NotificationSummaryBudget
{
    /// <summary>
    /// 摘要的承载列名单。<see cref="NotificationIntent.Summary"/> 会被逐字复制进每条
    /// <see cref="NotificationMessage.Summary"/>（见 NotificationIntent 构造器），两列同在一次
    /// SaveChanges 里落库，所以任意一列放不下都会让整条告警变成 poison message。
    /// </summary>
    public static readonly IReadOnlyList<NotificationSummaryCarryingColumn> CarryingColumns =
    [
        new(typeof(NotificationIntent), nameof(NotificationIntent.Summary)),
        new(typeof(NotificationMessage), nameof(NotificationMessage.Summary)),
    ];

    private NotificationSummaryBudget(int maxLength)
    {
        MaxLength = maxLength;
    }

    /// <summary>摘要可用的最大字符数，等于全部承载列声明宽度的最小值。</summary>
    public int MaxLength { get; }

    public static NotificationSummaryBudget FromModel(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var maxLength = int.MaxValue;
        foreach (var column in CarryingColumns)
        {
            var entityType = model.FindEntityType(column.ClrType)
                ?? throw new InvalidOperationException(
                    $"Notification summary carrying entity '{column.ClrType.FullName}' is not part of the EF model.");
            var property = entityType.FindProperty(column.PropertyName)
                ?? throw new InvalidOperationException(
                    $"Notification summary carrying column '{column.Key}' is not part of the EF model.");
            var declaredMaxLength = property.GetMaxLength()
                ?? throw new InvalidOperationException(
                    $"Notification summary carrying column '{column.Key}' declares no max length, so the summary bound cannot be derived from the model.");
            maxLength = Math.Min(maxLength, declaredMaxLength);
        }

        return new NotificationSummaryBudget(maxLength);
    }
}

/// <summary>
/// 通知摘要的取值。只有两个具名工厂能产出，调用方在编译期必须显式选一个：
/// <see cref="Render"/>（进程内拼装，夹紧）或 <see cref="FromSubmitted"/>（外部提交，超界抛）。
/// <para>
/// 夹紧放在应用层而不是 <see cref="NotificationIntent"/> 域构造器里：
/// 「摘要装得下承载列」是渲染语义不是领域不变量，域层改写会让「守卫量的」与「落库的」分叉。
/// </para>
/// </summary>
public sealed record NotificationSummary
{
    /// <summary>夹紧后追加的省略标记，让收件人看得出摘要被截过。</summary>
    public const string TruncationMarker = "…";

    private NotificationSummary(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// 拼装路径：进程内集成事件消费者与监控器插值拼出的摘要，超界按承载上界夹紧。
    /// <para>
    /// 摘要少几个字仍然能送达；让它溢出则 22001 逃出消费者 → CAP 重试 → poison，
    /// 收件人一个字都收不到，而上游服务写库是成功的（症状不在上游那侧显现）。
    /// </para>
    /// </summary>
    public static NotificationSummary Render(string? text, NotificationSummaryBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);

        var value = text ?? string.Empty;
        return value.Length <= budget.MaxLength
            ? new NotificationSummary(value)
            : new NotificationSummary(Clamp(value, budget.MaxLength));
    }

    /// <summary>
    /// 提交路径：外部调用方经 HTTP 提交的摘要，超界抛而不夹紧 ——
    /// 悄悄改写调用方提交的文本，会让它以为自己发出去的就是落库的那一份。
    /// <para>
    /// 正常情况下 SubmitNotificationIntentRequestValidator 会排在 handler 之前先拒掉；
    /// 这里是校验器万一没被容器解析到时的响亮失败（失效方向必须是拒绝，不能是静默截断）。
    /// </para>
    /// </summary>
    public static NotificationSummary FromSubmitted(string? text, NotificationSummaryBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);

        var value = text ?? string.Empty;
        if (value.Length > budget.MaxLength)
        {
            throw new KnownException($"通知摘要长度不能超过 {budget.MaxLength} 个字符。");
        }

        return new NotificationSummary(value);
    }

    private static string Clamp(string value, int maxLength)
    {
        var keep = maxLength - TruncationMarker.Length;
        // 不要把代理对劈成两半：劈开后得到的是一个无法渲染的孤立码元。
        if (keep > 0 && char.IsHighSurrogate(value[keep - 1]))
        {
            keep--;
        }

        return keep <= 0
            ? value[..maxLength]
            : string.Concat(value.AsSpan(0, keep), TruncationMarker);
    }
}

/// <summary>
/// 同质枚举集合（一组标识符）在摘要里的呈现。
/// <para>
/// <b>只截项，不截字符。</b> 元素都是标识符：截字符会产出不存在的标识符 ——
/// 收件人拿 <c>WC-PRESS-0</c> 去搜会搜不到，或者搜到另一台设备。
/// 截字符是把「少了几项」换成「有几项是假的」，而截项保住了
/// 「摘要里出现的每个码都是真码」这条可断言性质，缺失则由计数提示显式化。
/// </para>
/// <para>
/// ⚠️ <b>适用条件：只适用于同质枚举集合。</b>
/// 异构语义段（例如告警摘要里的 alarm / tag / observed / threshold / raised-at 五段）
/// 丢任何一段都是丢语义、不是丢枚举项，那种位点只做整体夹紧，<b>不得</b>套用本方法。
/// </para>
/// </summary>
public static class NotificationSummaryList
{
    /// <summary>
    /// 摘要里最多列出的条数。
    /// <para>
    /// 这是产品数，不是算术数：列到第 5 个码，读的人已经能判断「这是一批而不是一个」，
    /// 再往下列只是把摘要撑长。<b>不由列宽反算</b> —— 反算会让条数随列宽漂移，
    /// 并且把一个产品判断伪装成算术。
    /// </para>
    /// <para>
    /// ⚠️ 工业告警摘要那处的语义段数上界也恰好是 5，那是<b>数值巧合，与本常量没有任何关联</b>：
    /// 那处根本不走本类型。调整本产品数时不必、也不应该去看那边的段数。
    /// </para>
    /// </summary>
    public const int MaxListedItems = 5;

    /// <summary>
    /// 先截项、后由 <see cref="NotificationSummary.Render"/> 整体夹紧：
    /// 单个元素的长度同样没有上界，所以截项之后仍然需要夹紧兜底。
    /// </summary>
    public static string Describe(IReadOnlyCollection<string>? items, string emptyFallback)
    {
        if (items is null || items.Count == 0)
        {
            return emptyFallback;
        }

        if (items.Count <= MaxListedItems)
        {
            return string.Join(", ", items);
        }

        var listed = string.Join(", ", items.Take(MaxListedItems));
        return $"{listed} and {items.Count - MaxListedItems} more ({items.Count} total)";
    }
}
