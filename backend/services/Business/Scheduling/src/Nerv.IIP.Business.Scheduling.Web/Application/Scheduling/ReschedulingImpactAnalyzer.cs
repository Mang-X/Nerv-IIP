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
    ReschedulingImpactReasonCode Code, ScheduleAssignmentSegmentContract? CompetitionWindow = null, int? CapacityUnits = null);
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
        var predecessors = BuildPredecessorEdges(normalizedProblem, indices);
        var direct = assignments.SelectMany((assignment, index) => operationDeviations[(assignment.OrderId, assignment.OperationId)]
            .Select(x => (Index: index, Reason: new ReschedulingImpactReason(ReschedulingImpactReasonCode.OperationDeviation, x)))
            .Concat(resourceDeviations[assignment.ResourceId].Where(x => Occupies(assignment, x.StartUtc, x.EndUtc))
                .Select(x => (Index: index, Reason: new ReschedulingImpactReason(ReschedulingImpactReasonCode.ResourceUnavailable, x)))))
            .ToArray();
        var rootDemands = direct.Select(x => InitialDemand(x.Index, assignments[x.Index], x.Reason,
            normalizedProblem.HorizonEndUtc)).ToArray();
        var reasonsByOperation = assignments.Select(_ => new List<ReschedulingImpactReason>()).ToArray();
        var pathsByOperation = assignments.Select(_ => new List<ReschedulingImpactPath>()).ToArray();
        var resources = normalizedProblem.Resources.ToDictionary(x => x.ResourceId, StringComparer.Ordinal);
        foreach (var rootDemand in rootDemands)
        {
            // 每个来源/直接命中分别传播。窗口只表示潜在竞争，不是生成或选定的新 assignment。
            var reached = new Dictionary<int, ImpactDemand> { [rootDemand.Index] = rootDemand };
            var pending = new Queue<ImpactDemand>();
            pending.Enqueue(rootDemand);
            while (pending.TryDequeue(out var current))
            {
                foreach (var next in predecessors[current.Index])
                {
                    var assignment = assignments[next];
                    var demand = current.Unquantified
                        ? UnknownDemand(assignment, normalizedProblem.HorizonEndUtc)
                        : ShiftRemaining(assignment, assignment.StartUtc,
                            Max(assignment.StartUtc, Completion(current, assignments[current.Index])));
                    Follow(next, ReschedulingImpactReasonCode.PredecessorDependency, demand, null, null);
                }
                foreach (var competition in ResourceCompetitions(current, rootDemands.Concat(reached.Values), assignments, resources))
                {
                    var assignment = assignments[competition.Index];
                    var demand = current.Unquantified
                        ? UnknownDemand(assignment, normalizedProblem.HorizonEndUtc)
                        : ShiftRemaining(assignment, competition.Window.StartUtc, competition.BlockingEndUtc);
                    Follow(competition.Index, ReschedulingImpactReasonCode.ResourceCapacity, demand, competition.Window, competition.Capacity);
                }

                void Follow(int next, ReschedulingImpactReasonCode code, IReadOnlyList<ScheduleAssignmentSegmentContract> demand,
                    ScheduleAssignmentSegmentContract? competition, int? capacity)
                {
                    var identity = Identity(assignments[next]);
                    if (identity == current.Path.Root || current.Path.Steps.Any(x => x.To == identity)) return;
                    var step = new ReschedulingImpactStep(Identity(assignments[current.Index]), identity, code, competition, capacity);
                    var candidate = new ImpactDemand(next, demand,
                        current.Path with { Steps = [.. current.Path.Steps, step] }, new(code, current.Reason.Source), current.Unquantified);
                    if (reached.TryGetValue(next, out var existing) && Completion(candidate, assignments[next]) <= Completion(existing, assignments[next])) return;
                    reached[next] = candidate;
                    pending.Enqueue(candidate);
                }
            }
            foreach (var current in reached.OrderBy(x => x.Key).Select(x => x.Value))
            {
                reasonsByOperation[current.Index].Add(current.Reason);
                pathsByOperation[current.Index].Add(current.Path);
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

    private sealed record ImpactDemand(int Index, IReadOnlyList<ScheduleAssignmentSegmentContract> Segments,
        ReschedulingImpactPath Path, ReschedulingImpactReason Reason, bool Unquantified);

    private static ImpactDemand InitialDemand(int index, ScheduleAssignmentContract assignment,
        ReschedulingImpactReason reason, DateTimeOffset horizonEnd)
    {
        var segments = reason.Source is SchedulingResourceUnavailableDeviation outage
            ? ShiftRemaining(assignment, outage.StartUtc, outage.EndUtc)
            : UnknownDemand(assignment, horizonEnd);
        return new(index, segments, new(reason.Source, Identity(assignment), []), reason,
            reason.Source is SchedulingOperationDeviation);
    }

    // 未量化偏差的窗口是需校验范围，不能据此读取实际延迟；第一次片段释放后可能影响后续容量。
    private static IReadOnlyList<ScheduleAssignmentSegmentContract> UnknownDemand(ScheduleAssignmentContract assignment, DateTimeOffset horizonEnd)
    {
        var release = Segments(assignment).OrderBy(x => x.StartUtc).First().EndUtc;
        return [new(release, Max(release, horizonEnd))];
    }

    // 保留实际片段之间的空档；剩余加工只取 from 之后的片段部分，不把包络空档计作加工量。
    private static IReadOnlyList<ScheduleAssignmentSegmentContract> ShiftRemaining(ScheduleAssignmentContract assignment,
        DateTimeOffset from, DateTimeOffset resume)
    {
        var remaining = Segments(assignment).Where(x => x.EndUtc > from).OrderBy(x => x.StartUtc)
            .Select(x => new ScheduleAssignmentSegmentContract(Max(x.StartUtc, from), x.EndUtc)).ToArray();
        var shift = Max(resume, remaining[0].StartUtc) - remaining[0].StartUtc;
        return remaining.Select(x => new ScheduleAssignmentSegmentContract(x.StartUtc + shift, x.EndUtc + shift)).ToArray();
    }

    private static DateTimeOffset Completion(ImpactDemand demand, ScheduleAssignmentContract assignment) =>
        Max(assignment.EndUtc, demand.Segments.Max(x => x.EndUtc));

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static IReadOnlyList<int>[] BuildPredecessorEdges(SchedulingProblemContract problem,
        Dictionary<(string OrderId, string OperationId), int> indices)
    {
        var edges = Enumerable.Range(0, indices.Count).Select(_ => new HashSet<int>()).ToArray();
        foreach (var order in problem.Orders)
        foreach (var operation in order.Operations)
        {
            if (!indices.TryGetValue((order.OrderId, operation.OperationId), out var next)) continue;
            foreach (var predecessor in operation.PredecessorOperationIds)
            {
                if (indices.TryGetValue((order.OrderId, predecessor), out var previous)) edges[previous].Add(next);
            }
            // 与 FiniteCapacityScheduler.PredecessorKeys 一致：父单首序工序依赖所有子单工序。
            if (operation.OperationSequence != order.Operations.Min(x => x.OperationSequence)) continue;
            foreach (var dependency in (problem.AssemblyDependencies ?? []).Where(x => x.ParentOrderId == order.OrderId))
            foreach (var child in problem.Orders.Where(x => x.OrderId == dependency.ChildOrderId))
            foreach (var childOperation in child.Operations)
            {
                if (indices.TryGetValue((child.OrderId, childOperation.OperationId), out var previous)) edges[previous].Add(next);
            }
        }
        return edges.Select(x => (IReadOnlyList<int>)x.Order().ToArray()).ToArray();
    }

    private static IReadOnlyList<(int Index, ScheduleAssignmentSegmentContract Window, DateTimeOffset BlockingEndUtc, int Capacity)> ResourceCompetitions(
        ImpactDemand current, IEnumerable<ImpactDemand> demands, ScheduleAssignmentContract[] assignments,
        IReadOnlyDictionary<string, SchedulingResourceContract> resources)
    {
        var resourceId = assignments[current.Index].ResourceId;
        var capacity = Math.Max(1, resources[resourceId].CapacityUnits);
        var baseline = assignments.Select((assignment, index) => (assignment, index)).Where(x => x.assignment.ResourceId == resourceId)
            .SelectMany(x => Segments(x.assignment).Select(segment => (Index: x.index, Segment: segment))).ToArray();
        var potential = demands.Where(x => assignments[x.Index].ResourceId == resourceId)
            .SelectMany(x => x.Segments.Select(segment => (x.Index, Segment: segment))).ToArray();
        var result = new Dictionary<int, (int Index, ScheduleAssignmentSegmentContract Window, DateTimeOffset BlockingEndUtc, int Capacity)>();
        foreach (var window in current.Segments)
        {
            var boundaries = baseline.Concat(potential).SelectMany(x => new[] { x.Segment.StartUtc, x.Segment.EndUtc })
                .Append(window.StartUtc).Append(window.EndUtc).Where(x => x >= window.StartUtc && x <= window.EndUtc).Distinct().Order().ToArray();
            for (var i = 0; i + 1 < boundaries.Length; i++)
            {
                var start = boundaries[i];
                var end = boundaries[i + 1];
                var actual = baseline.Where(x => x.Segment.StartUtc <= start && start < x.Segment.EndUtc).ToArray();
                // 当前需求仍在自己的原占用内时没有新增容量，不能把其它来源的竞争归因给它。
                if (actual.Any(x => x.Index == current.Index)) continue;
                // 同一 assignment 原占用与潜在占用的重合只计一个单位；多个来源的共同竞争仍参与计数。
                var occupied = actual.Select(x => x.Index).Concat(potential
                    .Where(x => x.Segment.StartUtc <= start && start < x.Segment.EndUtc).Select(x => x.Index)).Distinct().Count();
                if (occupied <= capacity) continue;
                foreach (var other in actual.Where(x => x.Index != current.Index).OrderBy(x => x.Index))
                    result.TryAdd(other.Index, (other.Index, new(start, end), window.EndUtc, capacity));
            }
        }
        return result.Values.OrderBy(x => x.Index).ToArray();
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
