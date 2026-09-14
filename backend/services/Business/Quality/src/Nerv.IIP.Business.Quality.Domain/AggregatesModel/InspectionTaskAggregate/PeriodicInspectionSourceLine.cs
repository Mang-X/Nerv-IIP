namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;

/// <summary>
/// 周期检任务的来源行身份与触发幂等键的**唯一**编码/解码点（#3191）。
///
/// 周期检没有独立的来源单据，来源单据是工单，工序与窗口序号只能编进来源行。该来源行自 #3319 起
/// 原样落在 <c>inspection_records.source_document_line_id</c>（不再被搬进来源单据身份那一列），
/// 下游要还原工序身份仍须解这一串——解码点必须与编码点同住一处，否则两地各写一份等价谓词，
/// 改一处就静默漂移。
/// </summary>
public static class PeriodicInspectionSourceLine
{
    /// <summary>按时间窗口触发的周期检。</summary>
    public const string TimeKind = "periodic-time";

    /// <summary>按累计数量阈值触发的周期检。</summary>
    public const string QuantityKind = "periodic-quantity";

    private const string TriggerKeyPrefix = "quality:";

    /// <summary>来源行身份：<c>{operationId}:{kind}:{runtimeContextId}:{sequence}</c>。</summary>
    public static string LineId(string operationId, string kind, Guid runtimeContextId, long sequence) =>
        $"{operationId}:{kind}:{runtimeContextId:D}:{sequence}";

    /// <summary>触发幂等键：<c>quality:{kind}:{runtimeContextId}:{sequence}</c>。</summary>
    public static string TriggerIdempotencyKey(string kind, Guid runtimeContextId, long sequence) =>
        $"{TriggerKeyPrefix}{kind}:{runtimeContextId:D}:{sequence}";

    /// <summary>
    /// 从来源行身份还原工序 id。不是周期检来源行时返回 false 并把 <paramref name="operationId"/> 置空，
    /// 调用方据此区分「工序检（来源行就是工序任务 id）」与「周期检（来源行是复合窗口身份）」。
    /// </summary>
    public static bool TryParseOperationId(string? value, out string operationId)
    {
        operationId = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split(':');
        if (segments.Length != 4
            || segments[0].Length == 0
            || !(string.Equals(segments[1], TimeKind, StringComparison.Ordinal)
                || string.Equals(segments[1], QuantityKind, StringComparison.Ordinal)))
        {
            return false;
        }

        operationId = segments[0];
        return true;
    }
}
