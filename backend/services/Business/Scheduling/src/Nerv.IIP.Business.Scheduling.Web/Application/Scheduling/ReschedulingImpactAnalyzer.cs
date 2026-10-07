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

internal enum ReschedulingImpactReasonCode { OperationDeviation, ResourceUnavailable }
internal sealed record ReschedulingImpactReason(ReschedulingImpactReasonCode Code, SchedulingDeviation Source);
internal sealed record ReschedulingAffectedOperation(ScheduleAssignmentContract Assignment,
    IReadOnlyList<ReschedulingImpactReason> Reasons, SchedulingFreezeReason FreezeReasons);
internal sealed record ReschedulingImpact(string InputFingerprint,
    IReadOnlyList<ReschedulingAffectedOperation> AffectedOperations,
    IReadOnlyList<SchedulingFrozenAssignment> FrozenAssignments,
    IReadOnlyList<ScheduleAssignmentContract> RecalculateAssignments);

internal static class ReschedulingImpactAnalyzer
{
    // #4165：仅计算直接命中；工序依赖与资源传播由 #4168 接续此入口。
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
        // 每个基线工序只允许一条 assignment；冻结与直接命中共享同一身份。
        _ = assignments.ToDictionary(x => (x.OrderId, x.OperationId));
        var normalizedDeviations = deviations.Distinct().OrderBy(x => CanonicalJson(x), StringComparer.Ordinal).ToArray();
        var frozen = SchedulingFreezeCalculator.Calculate(assignments, execution, manualLocks, policy);
        var frozenByOperation = frozen.ToDictionary(x => (x.Assignment.OrderId, x.Assignment.OperationId));
        var operationDeviations = normalizedDeviations.OfType<SchedulingOperationDeviation>().ToLookup(x => (x.OrderId, x.OperationId));
        var resourceDeviations = normalizedDeviations.OfType<SchedulingResourceUnavailableDeviation>().ToLookup(x => x.ResourceId, StringComparer.Ordinal);
        var affected = new List<ReschedulingAffectedOperation>();
        foreach (var assignment in assignments)
        {
            var key = (assignment.OrderId, assignment.OperationId);
            var reasons = operationDeviations[key]
                .Select(x => new ReschedulingImpactReason(ReschedulingImpactReasonCode.OperationDeviation, x))
                .Concat(resourceDeviations[assignment.ResourceId]
                    .Where(x => Occupies(assignment, x.StartUtc, x.EndUtc))
                    .Select(x => new ReschedulingImpactReason(ReschedulingImpactReasonCode.ResourceUnavailable, x)))
                .ToArray();
            if (reasons.Length > 0)
            {
                affected.Add(new ReschedulingAffectedOperation(assignment, reasons,
                    frozenByOperation.TryGetValue(key, out var freeze) ? freeze.Reasons : SchedulingFreezeReason.None));
            }
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
