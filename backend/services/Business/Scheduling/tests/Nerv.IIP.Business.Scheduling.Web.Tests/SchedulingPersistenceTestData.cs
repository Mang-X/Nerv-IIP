using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

internal static class SchedulingPersistenceTestData
{
    // Direct aggregate fixtures bypass providers, so an empty source set is the matching
    // current trace for their synthetic generated output.
    public static SchedulePlanExecutionTraceSnapshot CurrentAvailableTrace { get; } = new(
        EngineId: "finite-capacity",
        EngineVersion: "aps-lite-v1",
        RuleProviderId: "built-in",
        RuleProfileId: "adr-0014-default",
        RuleProfileVersion: "v1",
        ConstraintSourcesJson: """{"schemaVersion":1,"sources":[]}""",
        TraceSchemaVersion: SchedulingExecutionTraceSchema.CurrentVersion,
        ReplayStatus: SchedulingReplayStatuses.Available);

    // These fixtures model no provider transformation; the supplied normalized JSON is
    // therefore both their explicit base snapshot and exact effective engine input.
    public static string UnchangedEffectiveInputFingerprint(string normalizedProblemJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedProblemJson));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
