using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

internal sealed record ReschedulingCandidateInput(SchedulingProblemContract Problem, SchedulePlanContract Baseline,
    IReadOnlyCollection<SchedulingDeviation> Deviations, IReadOnlyCollection<SchedulingFreezeExecutionFact> Execution,
    IReadOnlyCollection<(string OrderId, string OperationId)> ManualLocks, SchedulingFreezePolicy Policy,
    SchedulingEquipmentAvailabilitySnapshotContract? EquipmentAvailability = null,
    SchedulingMaterialConstraintModeContract MaterialMode = SchedulingMaterialConstraintModeContract.Soft,
    SchedulingQualityConstraintModeContract QualityMode = SchedulingQualityConstraintModeContract.Soft,
    SchedulingEquipmentUnknownModeContract EquipmentUnknownMode = SchedulingEquipmentUnknownModeContract.Soft);
internal sealed record ReschedulingCandidateMovement(ScheduleAssignmentContract Original, ScheduleAssignmentContract Candidate,
    IReadOnlyList<ReschedulingImpactReason> Reasons, IReadOnlyList<ReschedulingImpactPath> Paths);
internal sealed record ReschedulingCandidateExplanation(string OrderId, string OperationId, string Code,
    IReadOnlyList<ReschedulingImpactReason> Reasons, IReadOnlyList<ReschedulingImpactPath> Paths);
internal sealed record ReschedulingCandidate(string InputFingerprint, SchedulePlanContract Plan, ReschedulingImpact Impact,
    IReadOnlyList<ReschedulingCandidateMovement> Movements, IReadOnlyList<ReschedulingCandidateExplanation> Explanations,
    SchedulingEquipmentAvailabilitySnapshotContract? EquipmentAvailability, IReadOnlyList<ReschedulingResourceTransfer> Transfers);
internal sealed record ReschedulingResourceTransfer(string OrderId, string OperationId,
    string OriginalResourceId, string ResourceId, int SetupMinutes, IReadOnlyList<SchedulingSubstituteDeviceFact> DeviceSources);
internal sealed record SchedulingSubstituteDeviceFact(string ResourceId, string SubstituteResourceId,
    SchedulingEquipmentAvailabilityInputContract Source);

/// <summary>两策略共用影响、冻结、完整输入指纹及局部结果解释。</summary>
internal sealed record ReschedulingCandidateContext(ReschedulingCandidateInput Input, SchedulingProblemContract Problem,
    ReschedulingImpact Impact, IReadOnlyCollection<ScheduleAssignmentContract> Movable,
    IReadOnlyCollection<ScheduleAssignmentContract> Preserved, string Fingerprint)
{
    public static ReschedulingCandidateContext Create(ReschedulingCandidateInput input)
    {
        var deviations = input.Deviations.Distinct().ToArray();
        var locks = input.ManualLocks.Concat(input.Baseline.Assignments.Where(x => x.IsLocked)
            .Select(x => (x.OrderId, x.OperationId))).Concat(input.Problem.LockedAssignments.Select(x => (x.OrderId, x.OperationId)))
            .Distinct().ToArray();
        var equipment = input.EquipmentAvailability;
        var equipmentProblem = equipment is null ? input.Problem : EquipmentAvailabilitySchedulingAdapter.Apply(input.Problem,
            new(equipment.ContractVersion, input.Problem.OrganizationId, input.Problem.EnvironmentId,
                input.Problem.HorizonStartUtc, input.Problem.HorizonEndUtc, equipment.Windows.Select(x => x.Window).ToArray()), input.EquipmentUnknownMode);
        var problem = SchedulingProblemNormalizer.Normalize(equipmentProblem with
        {
            UnavailabilityWindows = equipmentProblem.UnavailabilityWindows.Concat(deviations.OfType<SchedulingResourceUnavailableDeviation>()
                .Select(x => new SchedulingUnavailabilityWindowContract(x.ResourceId, null, x.StartUtc, x.EndUtc, x.ReasonCode))).Distinct().ToArray()
        });
        var impact = ReschedulingImpactAnalyzer.Analyze(problem, input.Baseline.Assignments, deviations,
            input.Execution, locks, input.Policy);
        var movable = impact.AffectedOperations.Where(x => x.FreezeReasons == SchedulingFreezeReason.None
                && x.Reasons.Any(reason => reason.Source is SchedulingResourceUnavailableDeviation))
            .Select(x => x.Assignment).ToArray();
        var movableKeys = movable.Select(Key).ToHashSet();
        var preserved = input.Baseline.Assignments.Where(x => !movableKeys.Contains(Key(x)))
            .OrderBy(x => x.OrderId, StringComparer.Ordinal).ThenBy(x => x.OperationId, StringComparer.Ordinal).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ReschedulingImpactAnalyzer.CanonicalJson(new
        {
            Impact = impact.InputFingerprint,
            // assignment 已由 Impact 指纹覆盖；KPI/Gantt 是派生投影，不再次序列化整份方案。
            Baseline = new { input.Baseline.PlanId, input.Baseline.ProblemId, input.Baseline.ProblemFingerprint,
                input.Baseline.AlgorithmVersion, input.Baseline.Status, input.Baseline.GeneratedAtUtc, input.Baseline.UnscheduledOperations },
            input.EquipmentAvailability,
            input.MaterialMode,
            input.QualityMode,
            input.EquipmentUnknownMode,
        })))).ToLowerInvariant();
        return new(input, problem, impact, movable, preserved, fingerprint);
    }

    public SchedulingProblemContract WithMovableOperations(Func<SchedulingOperationContract, ScheduleAssignmentContract, SchedulingOperationContract> select)
    {
        var movable = Movable.ToDictionary(Key);
        return Problem with
        {
            Orders = Problem.Orders.Select(order => order with
            {
                Operations = order.Operations.Select(operation => movable.TryGetValue((order.OrderId, operation.OperationId), out var original)
                    ? select(operation, original) : operation).ToArray()
            }).ToArray(),
            LockedAssignments = Preserved.Select(x => new SchedulingLockedAssignmentContract(x.AssignmentId, x.OrderId, x.OperationId,
                x.OperationSequence, x.ResourceId, x.WorkCenterId, x.StartUtc, x.EndUtc, "baseline-preserved",
                x.Segments?.OrderBy(segment => segment.StartUtc).ThenBy(segment => segment.EndUtc).ToArray())).ToArray()
        };
    }

    public ReschedulingCandidate Complete(SchedulePlanContract plan, IReadOnlyList<ReschedulingResourceTransfer> transfers)
    {
        // 基线未排工序不在局部可移动集合中，也不借候选补排；保留既有未排说明。
        plan = plan with
        {
            ProblemFingerprint = Fingerprint,
            UnscheduledOperations = plan.UnscheduledOperations.Concat(Input.Baseline.UnscheduledOperations)
                .DistinctBy(x => (x.OrderId, x.OperationId)).OrderBy(x => x.OrderId, StringComparer.Ordinal)
                .ThenBy(x => x.OperationId, StringComparer.Ordinal).ToArray(),
            FreezeContext = SchedulingFrozenOccupancy.ToContract(SchedulingFreezeSnapshot.From(Input.Policy, Impact.FrozenAssignments))
        };
        plan = plan with { Metrics = plan.Metrics with { UnscheduledOperationCount = plan.UnscheduledOperations.Count } };
        var candidateByKey = plan.Assignments.ToDictionary(Key);
        var movements = Impact.AffectedOperations.Where(x => candidateByKey.TryGetValue(Key(x.Assignment), out var candidate)
                && Changed(x.Assignment, candidate)).Select(x => new ReschedulingCandidateMovement(x.Assignment,
                    candidateByKey[Key(x.Assignment)], x.Reasons, x.Paths)).ToArray();
        var explanations = new List<ReschedulingCandidateExplanation>();
        foreach (var affected in Impact.AffectedOperations)
        {
            var assignment = affected.Assignment;
            if (Input.EquipmentAvailability?.Windows.Any(x => x.RestorePredictionExpired && x.Window.DeviceAssetId == assignment.ResourceId) == true)
                explanations.Add(new(assignment.OrderId, assignment.OperationId, "restore-prediction-expired", affected.Reasons, affected.Paths));
            if (affected.Reasons.Any(x => x.Source is SchedulingOperationDeviation))
                explanations.Add(new(assignment.OrderId, assignment.OperationId, "unquantified-delay", affected.Reasons, affected.Paths));
            if (!candidateByKey.ContainsKey(Key(assignment)))
                explanations.Add(new(assignment.OrderId, assignment.OperationId,
                    plan.UnscheduledOperations.Single(x => (x.OrderId, x.OperationId) == Key(assignment)).ReasonCode.ToString(), affected.Reasons, affected.Paths));
            if (affected.FreezeReasons != SchedulingFreezeReason.None && plan.Conflicts.Any(x =>
                    (x.OrderId, x.OperationId) == Key(assignment) && x.Severity == ScheduleConflictSeverityContract.Error))
                explanations.Add(new(assignment.OrderId, assignment.OperationId, "frozen-conflict", affected.Reasons, affected.Paths));
        }
        return new(Fingerprint, plan, Impact, movements, explanations, Input.EquipmentAvailability, transfers);
    }

    internal static (string OrderId, string OperationId) Key(ScheduleAssignmentContract assignment) => (assignment.OrderId, assignment.OperationId);
    internal static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    private static bool Changed(ScheduleAssignmentContract a, ScheduleAssignmentContract b) =>
        a.ResourceId != b.ResourceId || a.StartUtc != b.StartUtc || a.EndUtc != b.EndUtc
        || !(a.Segments ?? []).OrderBy(x => x.StartUtc).SequenceEqual((b.Segments ?? []).OrderBy(x => x.StartUtc));
}
