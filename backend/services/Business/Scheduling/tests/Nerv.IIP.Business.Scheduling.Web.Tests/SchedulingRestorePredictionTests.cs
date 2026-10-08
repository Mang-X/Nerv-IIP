using System.Text.Json;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// #4130 / ADR 0032 §2: prediction provenance is input, never actual restoration.
public sealed class SchedulingRestorePredictionTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("explicit-etr")]
    [InlineData("device-mttr")]
    [InlineData("configuration-default")]
    public async Task Preview_fingerprint_identifies_prediction_basis_and_version(string source)
    {
        var first = await Preview(Window(source, "v1"));
        var repeated = await Preview(Window(source, "v1"));
        var changedVersion = await Preview(Window(source, "v2"));
        var changedBasis = await Preview(Window(source + "-changed", "v1"));
        var changedEstimate = await Preview(Window(source, "v1") with { ExpectedRestoreAtUtc = AsOf.AddHours(3) });
        Assert.Equal(first.ProblemFingerprint, repeated.ProblemFingerprint);
        Assert.NotEqual(first.ProblemFingerprint, changedVersion.ProblemFingerprint);
        Assert.NotEqual(first.ProblemFingerprint, changedBasis.ProblemFingerprint);
        Assert.NotEqual(first.ProblemFingerprint, changedEstimate.ProblemFingerprint);
        Assert.Equal(first.Assignments, repeated.Assignments);
    }

    [Fact]
    public async Task Future_and_expired_predictions_do_not_clear_either_actual_source()
    {
        var maintenance = Window("explicit-etr", "v1");
        var telemetry = maintenance with { SourceType = EquipmentRuntimeSourceType.DeviceState,
            SourceReferenceId = "telemetry", ExpectedRestoreAtUtc = null, RestorePredictionSource = null,
            RestorePredictionSourceVersion = null };
        var future = await Preview(maintenance, telemetry);
        var expired = await Preview(maintenance with { ExpectedRestoreAtUtc = AsOf.AddMinutes(-1) }, telemetry);
        var maintenanceRestored = await Preview(maintenance with { AvailabilityStatus = EquipmentRuntimeAvailabilityStatus.Available }, telemetry);
        foreach (var plan in new[] { future, expired, maintenanceRestored })
            Assert.Contains(plan.UnscheduledOperations, x => x.OperationId == "WO-RUSH-REAR-001-WELD"
                && x.ReasonCode == ScheduleConflictReasonCodeContract.Equipment);
        var restored = await Preview(maintenance with { AvailabilityStatus = EquipmentRuntimeAvailabilityStatus.Available },
            telemetry with { AvailabilityStatus = EquipmentRuntimeAvailabilityStatus.Available });
        Assert.DoesNotContain(restored.UnscheduledOperations, x => x.OperationId == "WO-RUSH-REAR-001-WELD");
    }

    [Fact]
    public async Task Explicit_as_of_changes_fingerprint_without_releasing_actual_downtime()
    {
        var estimate = Window("explicit-etr", "v1");
        var before = await PreviewAt(AsOf, estimate);
        var expired = await PreviewAt(AsOf.AddHours(1), estimate);
        Assert.NotEqual(before.ProblemFingerprint, expired.ProblemFingerprint);
        Assert.Equal(before.UnscheduledOperations, expired.UnscheduledOperations);
    }

    [Fact]
    public void Internal_snapshot_replays_expiration_provenance_and_source_order()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var estimate = Window("device-mttr", "history-v1");
        var telemetry = estimate with { SourceReferenceId = "telemetry", SourceType = EquipmentRuntimeSourceType.DeviceState,
            ExpectedRestoreAtUtc = null, RestorePredictionSource = null, RestorePredictionSourceVersion = null };
        EquipmentRuntimeAvailabilityResponse Response(params EquipmentRuntimeAvailabilityWindowContract[] windows)
            => new(1, problem.OrganizationId, problem.EnvironmentId, problem.HorizonStartUtc, problem.HorizonEndUtc, windows);
        var input = SchedulingEquipmentAvailabilitySnapshot.Create(Response(estimate, telemetry), AsOf);
        Assert.False(Assert.Single(input.Windows, x => x.Window.SourceReferenceId == "maintenance").RestorePredictionExpired);
        var reordered = SchedulingEquipmentAvailabilitySnapshot.Create(Response(telemetry, estimate), AsOf);
        var fingerprint = CreateSchedulePlanCommandHandler.CalculateProblemFingerprint(problem, [], equipmentAvailability: input);
        Assert.Equal(fingerprint, CreateSchedulePlanCommandHandler.CalculateProblemFingerprint(problem, [], equipmentAvailability: reordered));
        var expired = SchedulingEquipmentAvailabilitySnapshot.Create(Response(estimate, telemetry), AsOf.AddHours(1));
        Assert.NotEqual(fingerprint, CreateSchedulePlanCommandHandler.CalculateProblemFingerprint(problem, [], equipmentAvailability: expired));
        var json = SchedulingFrozenOccupancy.SerializeSnapshot(problem, [], equipmentAvailability: expired);
        using var snapshot = JsonDocument.Parse(json);
        var restored = snapshot.RootElement.GetProperty("equipmentAvailability")
            .Deserialize<SchedulingEquipmentAvailabilitySnapshotContract>(SchedulingJson.Options)!;
        Assert.Equal(AsOf.AddHours(1), restored.AsOfUtc);
        var prediction = Assert.Single(restored.Windows, x => x.Window.SourceReferenceId == "maintenance");
        Assert.True(prediction.RestorePredictionExpired);
        Assert.Equal("device-mttr", prediction.Window.RestorePredictionSource);
        Assert.Equal("history-v1", prediction.Window.RestorePredictionSourceVersion);
        Assert.Equal(estimate.ExpectedRestoreAtUtc, prediction.Window.ExpectedRestoreAtUtc);
        Assert.Equal(problem.ProblemId, JsonSerializer.Deserialize<SchedulingProblemContract>(json, SchedulingJson.Options)!.ProblemId);
        var actualRestored = SchedulingEquipmentAvailabilitySnapshot.Create(Response(estimate with
            { AvailabilityStatus = EquipmentRuntimeAvailabilityStatus.Available }), AsOf);
        Assert.Null(Assert.Single(actualRestored.Windows).Window.ExpectedRestoreAtUtc);
        Assert.False(Assert.Single(actualRestored.Windows).RestorePredictionExpired);
    }

    private static EquipmentRuntimeAvailabilityWindowContract Window(string source, string version) => new(
        "DEV-WELD-01", "WC-TUBE-WELD", EquipmentRuntimeAvailabilityStatus.Unavailable,
        EquipmentRuntimeReasonCodes.Downtime, EquipmentRuntimeSeverity.Blocked, AsOf, AsOf.AddHours(32),
        EquipmentRuntimeSourceType.Downtime, "maintenance", "equipment.downtime", [],
        ExpectedRestoreAtUtc: AsOf.AddHours(1), RestorePredictionSource: source, RestorePredictionSourceVersion: version);

    private static Task<SchedulePlanContract> Preview(params EquipmentRuntimeAvailabilityWindowContract[] windows)
        => PreviewAt(AsOf, windows);

    private static Task<SchedulePlanContract> PreviewAt(DateTimeOffset asOfUtc, params EquipmentRuntimeAvailabilityWindowContract[] windows)
    {
        var handler = new PreviewSchedulePlanCommandHandler(new FiniteCapacityScheduler(), new Clock(),
            new Availability(windows), new NoopSchedulingMaterialReadinessProvider(), new Overlay(),
            SchedulingEquipmentUnknownModeOption.Default);
        return handler.Handle(new PreviewSchedulePlanCommand(ShockAbsorberSchedulingFixture.CreateProblem(), AsOfUtc: asOfUtc), CancellationToken.None);
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => AsOf; }
    private sealed class Overlay : ISchedulingOperationOverrideOverlay
    {
        public Task<SchedulingProblemContract> ApplyAsync(SchedulingProblemContract problem, CancellationToken cancellationToken)
            => Task.FromResult(problem);
    }
    private sealed class Availability(EquipmentRuntimeAvailabilityWindowContract[] windows) : ISchedulingEquipmentAvailabilityProvider
    {
        public Task<EquipmentRuntimeAvailabilityResponse> QueryAsync(SchedulingProblemContract problem, CancellationToken cancellationToken)
            => Task.FromResult(new EquipmentRuntimeAvailabilityResponse(1, problem.OrganizationId, problem.EnvironmentId,
                problem.HorizonStartUtc, problem.HorizonEndUtc, windows));
    }
}
