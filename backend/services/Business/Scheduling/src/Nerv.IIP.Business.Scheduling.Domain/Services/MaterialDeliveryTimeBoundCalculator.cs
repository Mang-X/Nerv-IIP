namespace Nerv.IIP.Business.Scheduling.Domain.Services;

/// <summary>
/// 调用方提供真实剩余时长（含尚需发生的准备/加工/收尾时间），不能传入全量工单时长。
/// 保留完整前序关系；已完成工序的剩余时长为零。工序 ID 在同一来源内唯一。
/// </summary>
public sealed record RemainingRoutingOperation(
    string OperationId,
    TimeSpan RemainingDuration,
    IReadOnlyCollection<string> PredecessorOperationIds);

public sealed record MaterialDeliveryTimeSource(
    string SourceReference,
    DateTimeOffset DueUtc,
    IReadOnlyCollection<RemainingRoutingOperation> Operations);

public sealed record MaterialDeliveryTimeBound(
    string SourceReference,
    DateTimeOffset DueUtc,
    TimeSpan RemainingDuration,
    DateTimeOffset LatestStartUtc,
    IReadOnlyList<RemainingRoutingOperation> CriticalPath);

public sealed record MaterialDeliveryTimeBoundResult(
    MaterialDeliveryTimeBound TightestBound,
    IReadOnlyList<MaterialDeliveryTimeBound> SourceBounds);

/// <summary>
/// 交期减最长剩余工艺路径的纯时间界限；不考虑产能、日历或采购提前期。
/// 交期保留来源精度与偏移量，不读取当前时间或推断工序依赖。
/// </summary>
public static class MaterialDeliveryTimeBoundCalculator
{
    public static MaterialDeliveryTimeBoundResult Calculate(IReadOnlyCollection<MaterialDeliveryTimeSource> sources)
    {
        if (sources.Count == 0)
            throw new ArgumentException("At least one delivery source is required.", nameof(sources));

        var bounds = sources.Select(CalculateSource).ToArray();
        return new MaterialDeliveryTimeBoundResult(
            bounds.OrderBy(x => x.LatestStartUtc).ThenBy(x => x.SourceReference, StringComparer.Ordinal).First(),
            bounds);
    }

    private static MaterialDeliveryTimeBound CalculateSource(MaterialDeliveryTimeSource source)
    {
        var operations = source.Operations.ToDictionary(x => x.OperationId, StringComparer.Ordinal);
        var successors = operations.Keys.ToDictionary(x => x, _ => new List<string>(), StringComparer.Ordinal);
        var pending = new Dictionary<string, int>(StringComparer.Ordinal);
        var ready = new PriorityQueue<string, string>(StringComparer.Ordinal);
        foreach (var operation in operations.Values)
        {
            if (operation.RemainingDuration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(source), "Remaining duration cannot be negative.");

            pending[operation.OperationId] = operation.PredecessorOperationIds.Count;
            foreach (var predecessor in operation.PredecessorOperationIds)
                successors[predecessor].Add(operation.OperationId);
            if (pending[operation.OperationId] == 0)
                ready.Enqueue(operation.OperationId, operation.OperationId);
        }

        var pathDurations = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        var pathPredecessors = new Dictionary<string, string?>(StringComparer.Ordinal);
        while (ready.TryDequeue(out var id, out _))
        {
            var operation = operations[id];
            var predecessor = operation.PredecessorOperationIds
                .OrderByDescending(x => pathDurations[x])
                .ThenBy(x => x, StringComparer.Ordinal)
                .FirstOrDefault();
            pathPredecessors[id] = predecessor;
            pathDurations[id] = operation.RemainingDuration +
                (predecessor is null ? TimeSpan.Zero : pathDurations[predecessor]);
            foreach (var successor in successors[id])
            {
                if (--pending[successor] == 0)
                    ready.Enqueue(successor, successor);
            }
        }

        if (pathDurations.Count != operations.Count)
            throw new ArgumentException("The remaining route must not contain a cycle.", nameof(source));

        var last = pathDurations.OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => x.Key).FirstOrDefault();
        var remainingDuration = last is null ? TimeSpan.Zero : pathDurations[last];
        var path = new List<RemainingRoutingOperation>();
        for (var id = last; id is not null; id = pathPredecessors[id])
        {
            if (operations[id].RemainingDuration > TimeSpan.Zero)
                path.Add(operations[id]);
        }
        path.Reverse();

        return new MaterialDeliveryTimeBound(
            source.SourceReference, source.DueUtc, remainingDuration, source.DueUtc - remainingDuration, path);
    }
}
