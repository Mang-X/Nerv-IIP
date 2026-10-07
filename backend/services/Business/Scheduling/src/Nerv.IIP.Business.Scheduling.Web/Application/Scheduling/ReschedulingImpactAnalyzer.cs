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

internal sealed record SchedulingInsertedOperationDeviation(string SourceReference, string SourceVersion,
    DateTimeOffset OccurredAtUtc, string OrderId, string OperationId)
    : SchedulingDeviation(SourceReference, SourceVersion, OccurredAtUtc, "rush-insertion");

internal enum ReschedulingImpactReasonCode { OperationDeviation, NewOperation, ResourceUnavailable, PredecessorDependency, ResourceCapacity }
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
    private static readonly JsonSerializerOptions FingerprintJsonOptions = CreateFingerprintJsonOptions();

    private static JsonSerializerOptions CreateFingerprintJsonOptions()
    {
        var options = new JsonSerializerOptions(SchedulingJson.Options);
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        return options;
    }

    // baseline 是方案的实际 assignment 快照，来源读取与显式计算时点由调用方提供。
    public static ReschedulingImpact Analyze(SchedulingProblemContract problem,
        IReadOnlyCollection<ScheduleAssignmentContract> baseline, IReadOnlyCollection<SchedulingDeviation> deviations,
        IReadOnlyCollection<SchedulingFreezeExecutionFact> execution,
        IReadOnlyCollection<(string OrderId, string OperationId)> manualLocks, SchedulingFreezePolicy policy)
        => AnalyzeNormalized(SchedulingProblemNormalizer.Normalize(problem), baseline, deviations, execution, manualLocks, policy);

    internal static ReschedulingImpact AnalyzeNormalized(SchedulingProblemContract normalizedProblem,
        IReadOnlyCollection<ScheduleAssignmentContract> baseline, IReadOnlyCollection<SchedulingDeviation> deviations,
        IReadOnlyCollection<SchedulingFreezeExecutionFact> execution,
        IReadOnlyCollection<(string OrderId, string OperationId)> manualLocks, SchedulingFreezePolicy policy)
    {
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
        var insertedDeviations = normalizedDeviations.OfType<SchedulingInsertedOperationDeviation>()
            .ToLookup(x => (x.OrderId, x.OperationId));
        var insertedIndices = assignments.Select((assignment, index) => (assignment, index))
            .Where(x => insertedDeviations.Contains((x.assignment.OrderId, x.assignment.OperationId)))
            .Select(x => x.index).ToHashSet();
        var frozen = SchedulingFreezeCalculator.Calculate(assignments.Where((_, index) => !insertedIndices.Contains(index)).ToArray(), execution, manualLocks, policy);
        var frozenByOperation = frozen.ToDictionary(x => (x.Assignment.OrderId, x.Assignment.OperationId));
        var operationDeviations = normalizedDeviations.OfType<SchedulingOperationDeviation>().ToLookup(x => (x.OrderId, x.OperationId));
        var resourceDeviations = normalizedDeviations.OfType<SchedulingResourceUnavailableDeviation>().ToLookup(x => x.ResourceId, StringComparer.Ordinal);
        var predecessors = BuildPredecessorEdges(normalizedProblem, indices);
        var direct = assignments.SelectMany((assignment, index) => operationDeviations[(assignment.OrderId, assignment.OperationId)]
            .Select(x => (Index: index, Reason: new ReschedulingImpactReason(ReschedulingImpactReasonCode.OperationDeviation, x)))
            .Concat(insertedDeviations[(assignment.OrderId, assignment.OperationId)]
                .Select(x => (Index: index, Reason: new ReschedulingImpactReason(ReschedulingImpactReasonCode.NewOperation, x))))
            .Concat(resourceDeviations[assignment.ResourceId].Where(x => Occupies(assignment, x.StartUtc, x.EndUtc))
                .Select(x => (Index: index, Reason: new ReschedulingImpactReason(ReschedulingImpactReasonCode.ResourceUnavailable, x)))))
            .ToArray();
        var rootDemands = direct.Select(x => InitialDemand(x.Index, assignments[x.Index], x.Reason,
            normalizedProblem.HorizonEndUtc)).ToArray();
        var reasonsByOperation = assignments.Select(_ => new List<ReschedulingImpactReason>()).ToArray();
        var pathsByOperation = assignments.Select(_ => new List<ReschedulingImpactPath>()).ToArray();
        var resources = normalizedProblem.Resources.ToDictionary(x => x.ResourceId, StringComparer.Ordinal);
        var baselineByResource = assignments.Select((assignment, index) => (assignment, index))
            .Where(x => !insertedIndices.Contains(x.index)).GroupBy(x => x.assignment.ResourceId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.SelectMany(item => Segments(item.assignment)
                .Select(segment => (Index: item.index, Segment: segment))).ToArray(), StringComparer.Ordinal);
        // 所有来源共享潜在需求，容量按工序身份计数；root 标签保留各自的来源和路径。
        var reached = rootDemands.Select((demand, root) => (Key: (Root: root, demand.Index), Demand: demand))
            .ToDictionary(x => x.Key, x => x.Demand);
        var pending = new Queue<(int Root, int Index)>();
        var queued = new HashSet<(int Root, int Index)>();
        foreach (var key in reached.Keys) Enqueue(key);
        while (pending.TryDequeue(out var key))
        {
            queued.Remove(key);
            var current = reached[key];
            foreach (var next in predecessors[current.Index])
            {
                var assignment = assignments[next];
                var demand = current.Unquantified
                    ? UnknownDemand(assignment, normalizedProblem.HorizonEndUtc)
                    : ShiftRemaining(assignment, assignment.StartUtc,
                        Max(assignment.StartUtc, Completion(current, assignments[current.Index])));
                Follow(next, ReschedulingImpactReasonCode.PredecessorDependency, demand, null, null);
            }
            foreach (var competition in ResourceCompetitions(current, reached.Values, assignments, resources, baselineByResource))
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
                if (reached.TryGetValue((key.Root, next), out var existing) && Completion(candidate, assignments[next]) <= Completion(existing, assignments[next])) return;
                reached[(key.Root, next)] = candidate;
                // 新后继需求可能使其它来源的既有需求共同超容量，重新校验同资源上的需求。
                foreach (var changed in reached.Where(x => assignments[x.Key.Index].ResourceId == assignments[next].ResourceId
                        && x.Value.Segments.Any(segment => candidate.Segments.Any(added =>
                            segment.StartUtc < added.EndUtc && added.StartUtc < segment.EndUtc)))
                    .Select(x => x.Key).OrderBy(x => x.Root).ThenBy(x => x.Index)) Enqueue(changed);
            }
        }
        foreach (var current in reached.OrderBy(x => x.Key.Root).ThenBy(x => x.Key.Index).Select(x => x.Value))
        {
            reasonsByOperation[current.Index].Add(current.Reason);
            pathsByOperation[current.Index].Add(current.Path);
        }

        void Enqueue((int Root, int Index) key)
        {
            if (queued.Add(key)) pending.Enqueue(key);
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

        var canonicalInput = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Problem = normalizedProblem with
            {
                Orders = normalizedProblem.Orders.Select(order => order with
                {
                    Operations = order.Operations.Select(operation => operation with
                    {
                        Changeovers = operation.Changeovers?.Select(changeover => changeover with
                        {
                            RequiredToolingIds = changeover.RequiredToolingIds.Order(StringComparer.Ordinal).ToArray(),
                        }).OrderBy(CanonicalJson, StringComparer.Ordinal).ToArray(),
                    }).ToArray(),
                }).ToArray(),
                MaterialReadiness = normalizedProblem.MaterialReadiness.Select(material => material with
                {
                    Shortages = material.Shortages?.OrderBy(CanonicalJson, StringComparer.Ordinal).ToArray(),
                }).OrderBy(CanonicalJson, StringComparer.Ordinal).ToArray(),
                EquipmentDataRisks = normalizedProblem.EquipmentDataRisks?.OrderBy(CanonicalJson, StringComparer.Ordinal).ToArray(),
            },
            Baseline = assignments.Select(assignment => assignment with
            {
                Segments = assignment.Segments?.OrderBy(x => x.StartUtc).ThenBy(x => x.EndUtc).ToArray(),
            }).ToArray(),
            Deviations = normalizedDeviations.Select(x => (object)x).ToArray(),
            Execution = execution.OrderBy(x => x.OrderId, StringComparer.Ordinal).ThenBy(x => x.OperationId, StringComparer.Ordinal).ToArray(),
            ManualLocks = manualLocks.Distinct().OrderBy(x => x.OrderId, StringComparer.Ordinal)
                .ThenBy(x => x.OperationId, StringComparer.Ordinal).Select(x => new { x.OrderId, x.OperationId }).ToArray(),
            Policy = policy with { WorkCenterWindows = policy.WorkCenterWindows.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal) },
        }, FingerprintJsonOptions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(canonicalInput)).ToLowerInvariant();
        return new ReschedulingImpact(fingerprint, affected, frozen,
            affected.Where(x => x.FreezeReasons == SchedulingFreezeReason.None
                && !insertedDeviations.Contains((x.Assignment.OrderId, x.Assignment.OperationId))).Select(x => x.Assignment).ToArray());
    }

    private static ReschedulingImpactOperation Identity(ScheduleAssignmentContract assignment) =>
        new(assignment.OrderId, assignment.OperationId);

    private sealed record ImpactDemand(int Index, IReadOnlyList<ScheduleAssignmentSegmentContract> Segments,
        ReschedulingImpactPath Path, ReschedulingImpactReason Reason, bool Unquantified);

    private static ImpactDemand InitialDemand(int index, ScheduleAssignmentContract assignment,
        ReschedulingImpactReason reason, DateTimeOffset horizonEnd)
    {
        if (reason.Source is SchedulingInsertedOperationDeviation)
            return new(index, Segments(assignment).ToArray(), new(reason.Source, Identity(assignment), []), reason, false);
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
        IReadOnlyDictionary<string, SchedulingResourceContract> resources,
        IReadOnlyDictionary<string, (int Index, ScheduleAssignmentSegmentContract Segment)[]> baselineByResource)
    {
        var resourceId = assignments[current.Index].ResourceId;
        var capacity = Math.Max(1, resources[resourceId].CapacityUnits);
        var baseline = baselineByResource.GetValueOrDefault(resourceId) ?? [];
        var potential = demands.Where(x => assignments[x.Index].ResourceId == resourceId)
            .SelectMany(x => x.Segments.Select(segment => (x.Index, Segment: segment))).ToArray();
        var result = new Dictionary<int, (int Index, ScheduleAssignmentSegmentContract Window, DateTimeOffset BlockingEndUtc, int Capacity)>();
        foreach (var window in current.Segments)
        {
            var events = baseline.SelectMany(x => Events(x.Index, x.Segment, true))
                .Concat(potential.SelectMany(x => Events(x.Index, x.Segment, false)))
                .GroupBy(x => x.At).OrderBy(x => x.Key).ToArray();
            var actual = new Dictionary<int, int>();
            var occupied = new Dictionary<int, int>();
            for (var i = 0; i + 1 < events.Length; i++)
            {
                foreach (var change in events[i])
                {
                    Update(occupied, change.Index, change.Delta);
                    if (change.Actual) Update(actual, change.Index, change.Delta);
                }
                // 多个来源与原占用按同一工序身份合并；只为当前需求带来的新增容量传播。
                if (actual.ContainsKey(current.Index) || occupied.Count <= capacity) continue;
                foreach (var other in actual.Keys.Where(x => x != current.Index).Order())
                    result.TryAdd(other, (other, new(events[i].Key, events[i + 1].Key), window.EndUtc, capacity));
            }

            IEnumerable<(DateTimeOffset At, int Index, bool Actual, int Delta)> Events(int index,
                ScheduleAssignmentSegmentContract segment, bool isActual)
            {
                var start = Max(segment.StartUtc, window.StartUtc);
                var end = segment.EndUtc < window.EndUtc ? segment.EndUtc : window.EndUtc;
                if (start >= end) yield break;
                yield return (start, index, isActual, 1);
                yield return (end, index, isActual, -1);
            }
        }
        return result.Values.OrderBy(x => x.Index).ToArray();
    }

    private static void Update(Dictionary<int, int> active, int index, int delta)
    {
        var count = active.GetValueOrDefault(index) + delta;
        if (count == 0) active.Remove(index);
        else active[index] = count;
    }

    private static IEnumerable<ScheduleAssignmentSegmentContract> Segments(ScheduleAssignmentContract assignment) =>
        assignment.Segments is { Count: > 0 } ? assignment.Segments : [new(assignment.StartUtc, assignment.EndUtc)];

    private static bool Occupies(ScheduleAssignmentContract assignment, DateTimeOffset start, DateTimeOffset end) =>
        assignment.Segments is { Count: > 0 }
            ? assignment.Segments.Any(x => x.StartUtc < end && start < x.EndUtc)
            : assignment.StartUtc < end && start < assignment.EndUtc;

    internal static string CanonicalJson(object value)
    {
        return Canonicalize(JsonSerializer.SerializeToElement(value, FingerprintJsonOptions));
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
