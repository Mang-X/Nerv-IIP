using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public enum SchedulePlanReplayVerificationStatus
{
    Verified,
    UnknownEngineId,
    UnknownEngineVersion,
    EffectiveInputUnavailable,
    TraceUnavailable,
    UnsupportedTraceSchema,
    InvalidEffectiveInput,
    DigestMismatch
}

public sealed record SchedulePlanReplayVerificationResult(
    SchedulePlanReplayVerificationStatus Status,
    string? ExpectedDigest,
    string? ActualDigest,
    string Detail);

public sealed class SchedulePlanReplayService
{
    private readonly ApplicationDbContext dbContext;
    private readonly IReadOnlyDictionary<(string EngineId, string Version), ISchedulingEngine> engines;
    private readonly IReadOnlySet<string> engineIds;

    public SchedulePlanReplayService(
        ApplicationDbContext dbContext,
        IEnumerable<ISchedulingEngine> engines)
    {
        this.dbContext = dbContext;
        var registrations = engines.ToArray();
        var duplicate = registrations
            .GroupBy(x => (x.EngineId, x.Version))
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate scheduling engine registration '{duplicate.Key.EngineId}/{duplicate.Key.Version}'.");
        }

        this.engines = registrations.ToDictionary(x => (x.EngineId, x.Version));
        engineIds = registrations
            .Select(x => x.EngineId)
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<SchedulePlanReplayVerificationResult> VerifyAsync(
        string planId,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.SchedulePlans.AsNoTracking()
            .Include(x => x.Assignments)
            .Include(x => x.ResourceLoads)
            .Include(x => x.Conflicts)
            .Include(x => x.UnscheduledOperations)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                x => x.PlanId == planId &&
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId,
                cancellationToken)
            ?? throw new KnownException($"Schedule plan was not found, PlanId = {planId}");
        var problem = await dbContext.ScheduleProblems.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ProblemId == plan.ProblemId &&
                    x.OrganizationId == organizationId &&
                    x.EnvironmentId == environmentId,
                cancellationToken)
            ?? throw new KnownException(
                $"Schedule problem snapshot was not found, ProblemId = {plan.ProblemId}");

        if (!string.Equals(
                plan.ReplayStatus,
                SchedulingReplayStatuses.Available,
                StringComparison.Ordinal))
        {
            return Unavailable(
                SchedulePlanReplayVerificationStatus.TraceUnavailable,
                $"Replay trace status is '{plan.ReplayStatus}'.");
        }

        if (plan.TraceSchemaVersion != SchedulingExecutionTraceSchema.CurrentVersion)
        {
            return Unavailable(
                SchedulePlanReplayVerificationStatus.UnsupportedTraceSchema,
                $"Trace schema version '{plan.TraceSchemaVersion}' is not supported.");
        }

        if (!engineIds.Contains(plan.EngineId))
        {
            return Unavailable(
                SchedulePlanReplayVerificationStatus.UnknownEngineId,
                $"Scheduling engine id '{plan.EngineId}' is not registered.");
        }

        if (!engines.TryGetValue((plan.EngineId, plan.AlgorithmVersion), out var engine))
        {
            return Unavailable(
                SchedulePlanReplayVerificationStatus.UnknownEngineVersion,
                $"Scheduling engine '{plan.EngineId}' version '{plan.AlgorithmVersion}' is not registered.");
        }

        if (string.IsNullOrWhiteSpace(problem.EngineInputJson) ||
            string.IsNullOrWhiteSpace(problem.EngineInputFingerprint))
        {
            return Unavailable(
                SchedulePlanReplayVerificationStatus.EffectiveInputUnavailable,
                "The exact effective scheduling engine input is unavailable.");
        }

        SchedulingProblemContract effectiveProblem;
        try
        {
            effectiveProblem = JsonSerializer.Deserialize<SchedulingProblemContract>(
                problem.EngineInputJson,
                SchedulingJson.Options)
                ?? throw new JsonException("The exact effective input deserialized to null.");
        }
        catch (JsonException exception)
        {
            return Unavailable(
                SchedulePlanReplayVerificationStatus.InvalidEffectiveInput,
                $"The exact effective scheduling engine input is invalid: {exception.Message}");
        }

        var persisted = SchedulePlanContractMapper.ToContract(
            plan,
            problem.EngineInputFingerprint);
        var replayed = engine.Schedule(
            effectiveProblem,
            plan.PlanId,
            plan.GeneratedAtUtc);
        var expectedDigest = CalculateCanonicalDigest(persisted);
        var actualDigest = CalculateCanonicalDigest(replayed);
        return string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal)
            ? new SchedulePlanReplayVerificationResult(
                SchedulePlanReplayVerificationStatus.Verified,
                expectedDigest,
                actualDigest,
                "Exact replay matched the persisted canonical plan digest.")
            : new SchedulePlanReplayVerificationResult(
                SchedulePlanReplayVerificationStatus.DigestMismatch,
                expectedDigest,
                actualDigest,
                "Exact replay did not match the persisted canonical plan digest.");
    }

    private static SchedulePlanReplayVerificationResult Unavailable(
        SchedulePlanReplayVerificationStatus status,
        string detail)
    {
        return new SchedulePlanReplayVerificationResult(status, null, null, detail);
    }

    private static string CalculateCanonicalDigest(SchedulePlanContract plan)
    {
        var canonical = new ReplayDigestDocument(
            plan.ProblemFingerprint,
            plan.Metrics,
            plan.Assignments
                .OrderBy(x => x.OrderId, StringComparer.Ordinal)
                .ThenBy(x => x.OperationSequence)
                .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                .ThenBy(x => x.AssignmentId, StringComparer.Ordinal)
                .ToArray(),
            plan.ResourceLoads
                .OrderBy(x => x.ResourceId, StringComparer.Ordinal)
                .ThenBy(x => x.WindowStartUtc)
                .ThenBy(x => x.WindowEndUtc)
                .ToArray(),
            plan.Conflicts
                .OrderBy(x => x.ConflictId, StringComparer.Ordinal)
                .ToArray(),
            plan.UnscheduledOperations
                .OrderBy(x => x.OrderId, StringComparer.Ordinal)
                .ThenBy(x => x.OperationId, StringComparer.Ordinal)
                .ThenBy(x => x.ReasonCode)
                .ToArray());
        var json = JsonSerializer.Serialize(canonical, SchedulingJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private sealed record ReplayDigestDocument(
        string ProblemFingerprint,
        SchedulePlanMetricsContract Metrics,
        IReadOnlyCollection<ScheduleAssignmentContract> Assignments,
        IReadOnlyCollection<ScheduleResourceLoadContract> ResourceLoads,
        IReadOnlyCollection<ScheduleConflictContract> Conflicts,
        IReadOnlyCollection<UnscheduledOperationContract> UnscheduledOperations);
}
