using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

internal abstract record SchedulingDeviation(string SourceReference, string SourceVersion, DateTimeOffset OccurredAtUtc, string ReasonCode);

internal sealed record SchedulingResourceUnavailableDeviation(string SourceReference, string SourceVersion,
    DateTimeOffset OccurredAtUtc, string ReasonCode, string ResourceId, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
    : SchedulingDeviation(SourceReference, SourceVersion, OccurredAtUtc, ReasonCode);

internal sealed record SchedulingOperationDeviation(string SourceReference, string SourceVersion,
    DateTimeOffset OccurredAtUtc, string ReasonCode, string OrderId, string OperationId)
    : SchedulingDeviation(SourceReference, SourceVersion, OccurredAtUtc, ReasonCode);

internal enum ReschedulingImpactReasonCode { OperationDeviation, ResourceUnavailable, PredecessorDependency, ResourceCapacity }
internal sealed record ReschedulingImpactOperation(string OrderId, string OperationId);
internal sealed record ReschedulingImpactStep(ReschedulingImpactOperation From, ReschedulingImpactOperation To,
    ReschedulingImpactReasonCode Code);
internal sealed record ReschedulingImpactPath(SchedulingDeviation Source, ReschedulingImpactOperation Root,
    IReadOnlyList<ReschedulingImpactStep> Steps);
internal sealed record ReschedulingImpactReason(ReschedulingImpactReasonCode Code, SchedulingDeviation Source);
internal sealed record ReschedulingAffectedOperation(ScheduleAssignmentContract Assignment,
    IReadOnlyList<ReschedulingImpactReason> Reasons, SchedulingFreezeReason FreezeReasons, IReadOnlyList<ReschedulingImpactPath> Paths);
internal sealed record ReschedulingImpact(string InputFingerprint,
    IReadOnlyList<ReschedulingAffectedOperation> AffectedOperations,
    IReadOnlyList<SchedulingFrozenAssignment> FrozenAssignments,
    IReadOnlyList<ScheduleAssignmentContract> RecalculateAssignments);

internal static class ReschedulingImpactAnalyzer
{
    // baseline 是方案的实际 assignment 快照，来源读取与显式计算时点由调用方提供。
    public static ReschedulingImpact Analyze(SchedulingProblemContract problem,
        IReadOnlyCollection<ScheduleAssignmentContract> baseline, IReadOnlyCollection<SchedulingDeviation> deviations,
        IReadOnlyCollection<SchedulingFreezeExecutionFact> execution,
        IReadOnlyCollection<(string OrderId, string OperationId)> manualLocks, SchedulingFreezePolicy policy)
    {
        var normalizedProblem = SchedulingProblemNormalizer.Normalize(problem);
        foreach (var deviation in deviations)
        {
            if (string.IsNullOrWhiteSpace(deviation.SourceReference) || string.IsNullOrWhiteSpace(deviation.SourceVersion)
                || string.IsNullOrWhiteSpace(deviation.ReasonCode))
            {
                throw new ArgumentException("Deviation source reference, version and reason are required.", nameof(deviations));
            }
            switch (deviation)
            {
                case SchedulingResourceUnavailableDeviation unavailable:
                    if (string.IsNullOrWhiteSpace(unavailable.ResourceId) || unavailable.EndUtc <= unavailable.StartUtc)
                    {
                        throw new ArgumentException("Resource downtime requires a resource and a positive time window.", nameof(deviations));
                    }
                    break;
                case SchedulingOperationDeviation operation:
                    if (string.IsNullOrWhiteSpace(operation.OrderId) || string.IsNullOrWhiteSpace(operation.OperationId))
                    {
                        throw new ArgumentException("Operation deviation requires order and operation identifiers.", nameof(deviations));
                    }
                    break;
            }
        }

        var assignments = baseline.OrderBy(x => x.OrderId, StringComparer.Ordinal)
            .ThenBy(x => x.OperationId, StringComparer.Ordinal).ToArray();
        // 每个基线工序只允许一条 assignment；冻结、依赖与资源传播共享同一身份。
        var indices = assignments.Select((assignment, index) => (assignment, index))
            .ToDictionary(x => (x.assignment.OrderId, x.assignment.OperationId), x => x.index);
        var normalizedDeviations = deviations.Distinct().OrderBy(x => CanonicalJson(x), StringComparer.Ordinal).ToArray();
        var frozen = SchedulingFreezeCalculator.Calculate(assignments, execution, manualLocks, policy);
        var frozenByOperation = frozen.ToDictionary(x => (x.Assignment.OrderId, x.Assignment.OperationId));
        var operationDeviations = normalizedDeviations.OfType<SchedulingOperationDeviation>().ToLookup(x => (x.OrderId, x.OperationId));
        var resourceDeviations = normalizedDeviations.OfType<SchedulingResourceUnavailableDeviation>().ToLookup(x => x.ResourceId, StringComparer.Ordinal);
        var edges = BuildEdges(normalizedProblem, assignments, indices);
        var reasonsByOperation = assignments.Select(_ => new List<ReschedulingImpactReason>()).ToArray();
        var pathsByOperation = assignments.Select(_ => new List<ReschedulingImpactPath>()).ToArray();
        for (var root = 0; root < assignments.Length; root++)
        {
            var assignment = assignments[root];
            var directReasons = operationDeviations[(assignment.OrderId, assignment.OperationId)]
                .Select(x => new ReschedulingImpactReason(ReschedulingImpactReasonCode.OperationDeviation, x))
                .Concat(resourceDeviations[assignment.ResourceId]
                    .Where(x => Occupies(assignment, x.StartUtc, x.EndUtc))
                    .Select(x => new ReschedulingImpactReason(ReschedulingImpactReasonCode.ResourceUnavailable, x)));
            foreach (var direct in directReasons)
            {
                // 每个直接命中/来源保留一条确定性的最短路径；已访问集同时处理汇合与片段形成的环。
                var visited = new HashSet<int> { root };
                var pending = new Queue<(int Index, ReschedulingImpactPath Path, ReschedulingImpactReason Reason)>();
                pending.Enqueue((root, new(direct.Source, Identity(assignment), []), direct));
                while (pending.TryDequeue(out var current))
                {
                    reasonsByOperation[current.Index].Add(current.Reason);
                    pathsByOperation[current.Index].Add(current.Path);
                    foreach (var edge in edges[current.Index])
                    {
                        if (visited.Add(edge.To))
                        {
                            var step = new ReschedulingImpactStep(Identity(assignments[current.Index]),
                                Identity(assignments[edge.To]), edge.Code);
                            pending.Enqueue((edge.To, current.Path with { Steps = [.. current.Path.Steps, step] },
                                new(edge.Code, direct.Source)));
                        }
                    }
                }
            }
        }

        var affected = new List<ReschedulingAffectedOperation>();
        for (var index = 0; index < assignments.Length; index++)
        {
            if (reasonsByOperation[index].Count == 0) continue;
            var assignment = assignments[index];
            var key = (assignment.OrderId, assignment.OperationId);
            affected.Add(new(assignment, reasonsByOperation[index].Distinct()
                .OrderBy(x => x.Code).ThenBy(x => CanonicalJson(x.Source), StringComparer.Ordinal).ToArray(),
                frozenByOperation.TryGetValue(key, out var freeze) ? freeze.Reasons : SchedulingFreezeReason.None,
                pathsByOperation[index].ToArray()));
        }

        var canonicalInput = CanonicalJson(new
        {
            Problem = normalizedProblem,
            Baseline = assignments,
            Deviations = normalizedDeviations.Select(x => (object)x).ToArray(),
            Execution = execution,
            ManualLocks = manualLocks.Distinct().Select(x => new { x.OrderId, x.OperationId }).ToArray(),
            Policy = policy,
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalInput))).ToLowerInvariant();
        return new ReschedulingImpact(fingerprint, affected, frozen,
            affected.Where(x => x.FreezeReasons == SchedulingFreezeReason.None).Select(x => x.Assignment).ToArray());
    }

    private static ReschedulingImpactOperation Identity(ScheduleAssignmentContract assignment) =>
        new(assignment.OrderId, assignment.OperationId);

    private static IReadOnlyList<(int To, ReschedulingImpactReasonCode Code)>[] BuildEdges(
        SchedulingProblemContract problem, ScheduleAssignmentContract[] assignments,
        Dictionary<(string OrderId, string OperationId), int> indices)
    {
        var edges = assignments.Select(_ => new HashSet<(int To, ReschedulingImpactReasonCode Code)>()).ToArray();
        foreach (var order in problem.Orders)
        foreach (var operation in order.Operations)
        {
            if (!indices.TryGetValue((order.OrderId, operation.OperationId), out var next)) continue;
            foreach (var predecessor in operation.PredecessorOperationIds)
            {
                if (indices.TryGetValue((order.OrderId, predecessor), out var previous))
                    edges[previous].Add((next, ReschedulingImpactReasonCode.PredecessorDependency));
            }
        }

        var byResource = assignments.Select((assignment, index) => (assignment, index))
            .ToLookup(x => x.assignment.ResourceId, StringComparer.Ordinal);
        foreach (var resource in problem.Resources)
        {
            var occupancies = byResource[resource.ResourceId]
                .SelectMany(x => Segments(x.assignment).Select(segment => (x.index, Segment: segment))).ToArray();
            foreach (var starting in occupancies.GroupBy(x => x.Segment.StartUtc))
            {
                var boundary = starting.Key;
                // 基线在该边界已占满容量时，接续片段依赖刚结束片段释放容量。
                // 半开区间与有限产能内核一致；只看实际 Segments，不把包络空档或归属当占用。
                var occupied = occupancies.Where(x => x.Segment.StartUtc <= boundary && boundary < x.Segment.EndUtc)
                    .Select(x => x.index).Distinct().Count();
                if (occupied < Math.Max(1, resource.CapacityUnits)) continue;
                var releasing = occupancies.Where(x => x.Segment.EndUtc == boundary);
                foreach (var previous in releasing)
                foreach (var next in starting)
                {
                    if (previous.index != next.index)
                        edges[previous.index].Add((next.index, ReschedulingImpactReasonCode.ResourceCapacity));
                }
            }
        }
        return edges.Select(x => (IReadOnlyList<(int To, ReschedulingImpactReasonCode Code)>)x
            .OrderBy(edge => edge.To).ThenBy(edge => edge.Code).ToArray()).ToArray();
    }

    private static IEnumerable<ScheduleAssignmentSegmentContract> Segments(ScheduleAssignmentContract assignment) =>
        assignment.Segments is { Count: > 0 } ? assignment.Segments : [new(assignment.StartUtc, assignment.EndUtc)];

    private static bool Occupies(ScheduleAssignmentContract assignment, DateTimeOffset start, DateTimeOffset end) =>
        assignment.Segments is { Count: > 0 }
            ? assignment.Segments.Any(x => x.StartUtc < end && start < x.EndUtc)
            : assignment.StartUtc < end && start < assignment.EndUtc;

    private static string CanonicalJson(object value)
    {
        var options = new JsonSerializerOptions(SchedulingJson.Options);
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        return Canonicalize(JsonSerializer.SerializeToElement(value, options));
    }

    // 输入中的数组均为集合；排序对象字段与集合元素，覆盖嵌套集合和策略字典。
    // 只规范化指纹表示，不改动返回的基线 assignment 或 Segments。
    private static string Canonicalize(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject()
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => JsonSerializer.Serialize(x.Name) + ":" + Canonicalize(x.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray()
            .Select(Canonicalize).OrderBy(x => x, StringComparer.Ordinal)) + "]",
        _ => element.GetRawText(),
    };

    private sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDateTimeOffset();
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUniversalTime());
    }
}
