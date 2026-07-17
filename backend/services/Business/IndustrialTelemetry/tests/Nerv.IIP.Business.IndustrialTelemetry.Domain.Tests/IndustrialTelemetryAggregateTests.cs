using System.Reflection;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.ConnectorTagManifestAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRawSampleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests;

public sealed class IndustrialTelemetryAggregateTests
{
    private const string ManifestRevisionA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ManifestRevisionB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Connector_manifest_versions_increment_once_for_each_actual_root_or_binding_mutation()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(observedAtUtc, ManifestRevisionA,
        [
            Entry("temperature", observedAtUtc, activationStatus: "pending"),
            Entry("pressure", observedAtUtc),
        ]);
        var temperature = manifest.Bindings.Single(binding => binding.TagKey == "temperature");
        var pressure = manifest.Bindings.Single(binding => binding.TagKey == "pressure");

        Assert.Equal(1, manifest.ConcurrencyVersion);
        Assert.Equal(1, temperature.ConcurrencyVersion);
        Assert.Equal(1, pressure.ConcurrencyVersion);

        manifest.Apply("opcua", ManifestRevisionA, observedAtUtc,
        [
            Entry("temperature", observedAtUtc, activationStatus: "pending"),
            Entry("pressure", observedAtUtc),
        ]);

        Assert.Equal(1, manifest.ConcurrencyVersion);
        Assert.Equal(1, temperature.ConcurrencyVersion);
        Assert.Equal(1, pressure.ConcurrencyVersion);

        manifest.Apply("opcua", ManifestRevisionA, observedAtUtc,
        [
            Entry("temperature", observedAtUtc.AddMinutes(1), activationStatus: "active"),
            Entry("pressure", observedAtUtc),
        ]);

        Assert.Equal(1, manifest.ConcurrencyVersion);
        Assert.Equal(2, temperature.ConcurrencyVersion);
        Assert.Equal(1, pressure.ConcurrencyVersion);

        var changedTemperature = Entry("temperature", observedAtUtc.AddMinutes(1), activationStatus: "active") with
        {
            Enabled = false,
            ProtocolAddress = "ns=3;s=temperature",
        };
        manifest.Apply("opcua", ManifestRevisionB, observedAtUtc.AddMinutes(2), [changedTemperature]);

        Assert.Equal(2, manifest.ConcurrencyVersion);
        Assert.Equal(3, temperature.ConcurrencyVersion);
        Assert.Equal(2, pressure.ConcurrencyVersion);

        manifest.Apply("opcua", ManifestRevisionA, observedAtUtc.AddMinutes(3),
        [
            changedTemperature,
            Entry("pressure", observedAtUtc.AddMinutes(3)),
        ]);

        Assert.Equal(3, manifest.ConcurrencyVersion);
        Assert.Equal(3, temperature.ConcurrencyVersion);
        Assert.Equal(3, pressure.ConcurrencyVersion);
        Assert.True(pressure.IsCurrent);
        Assert.Null(pressure.RetiredAtUtc);
    }

    [Fact]
    public void Connector_manifest_rejected_reports_do_not_increment_concurrency_versions()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(observedAtUtc, ManifestRevisionA, [Entry("temperature", observedAtUtc)]);
        var binding = Assert.Single(manifest.Bindings);

        manifest.Apply("opcua", ManifestRevisionB, observedAtUtc.AddTicks(-1), [Entry("pressure", observedAtUtc)]);
        manifest.Apply("opcua", ManifestRevisionB, observedAtUtc, [Entry("pressure", observedAtUtc)]);

        Assert.Equal(1, manifest.ConcurrencyVersion);
        Assert.Equal(1, binding.ConcurrencyVersion);
    }

    [Fact]
    public void Connector_manifest_same_revision_and_observation_is_idempotent()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(observedAtUtc, ManifestRevisionA, [Entry("temperature", observedAtUtc)]);

        var result = manifest.Apply("opcua", ManifestRevisionA, observedAtUtc, [Entry("temperature", observedAtUtc)]);

        Assert.Equal(ManifestApplyDisposition.Idempotent, result.Disposition);
        Assert.Equal(ManifestRevisionA, result.AcceptedManifestRevision);
        Assert.Equal(observedAtUtc, result.AcceptedManifestObservedAtUtc);
        Assert.Single(manifest.Bindings);
    }

    [Fact]
    public void Connector_manifest_same_revision_later_matching_shape_accepts_newer_activation_fact()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(
            observedAtUtc,
            ManifestRevisionA,
            [Entry("temperature", observedAtUtc, activationStatus: "pending")]);

        var result = manifest.Apply(
            "OPCUA",
            ManifestRevisionA,
            observedAtUtc.AddMinutes(1),
            [Entry("TEMPERATURE", observedAtUtc.AddMinutes(1), activationStatus: "active") with
            {
                ProtocolAddress = "ns=2;s=temperature",
            }]);

        Assert.Equal(ManifestApplyDisposition.Accepted, result.Disposition);
        Assert.Equal(observedAtUtc.AddMinutes(1), manifest.ManifestObservedAtUtc);
        var binding = Assert.Single(manifest.Bindings);
        Assert.Equal("active", binding.ActivationStatus);
        Assert.Equal(observedAtUtc.AddMinutes(1), binding.ActivationObservedAtUtc);
    }

    [Fact]
    public void Connector_manifest_same_revision_later_changed_enabled_is_conflict()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var original = Entry("temperature", observedAtUtc);
        var manifest = CreateManifest(observedAtUtc, ManifestRevisionA, [original]);

        var result = manifest.Apply(
            "opcua",
            ManifestRevisionA,
            observedAtUtc.AddMinutes(1),
            [original with { Enabled = false, ActivationObservedAtUtc = observedAtUtc.AddMinutes(1) }]);

        Assert.Equal(ManifestApplyDisposition.Conflict, result.Disposition);
        Assert.Equal(observedAtUtc, manifest.ManifestObservedAtUtc);
        Assert.True(Assert.Single(manifest.Bindings).Enabled);
    }

    [Fact]
    public void Connector_manifest_same_revision_later_changed_protocol_address_is_conflict()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var original = Entry("temperature", observedAtUtc);
        var manifest = CreateManifest(observedAtUtc, ManifestRevisionA, [original]);

        var result = manifest.Apply(
            "opcua",
            ManifestRevisionA,
            observedAtUtc.AddMinutes(1),
            [original with
            {
                ProtocolAddress = "ns=3;s=temperature",
                ActivationObservedAtUtc = observedAtUtc.AddMinutes(1),
            }]);

        Assert.Equal(ManifestApplyDisposition.Conflict, result.Disposition);
        Assert.Equal(observedAtUtc, manifest.ManifestObservedAtUtc);
        Assert.Equal("ns=2;s=temperature", Assert.Single(manifest.Bindings).ProtocolAddress);
    }

    [Fact]
    public void Connector_manifest_same_revision_later_changed_source_system_is_conflict()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(observedAtUtc, ManifestRevisionA, [Entry("temperature", observedAtUtc)]);

        var result = manifest.Apply(
            "mqtt",
            ManifestRevisionA,
            observedAtUtc.AddMinutes(1),
            [Entry("temperature", observedAtUtc.AddMinutes(1))]);

        Assert.Equal(ManifestApplyDisposition.Conflict, result.Disposition);
        Assert.Equal("opcua", manifest.SourceSystem);
        Assert.Equal(observedAtUtc, manifest.ManifestObservedAtUtc);
    }

    [Fact]
    public void Connector_manifest_same_revision_later_changed_membership_conflict_is_atomic()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(
            observedAtUtc,
            ManifestRevisionA,
            [Entry("temperature", observedAtUtc), Entry("pressure", observedAtUtc)]);
        var acceptedBindings = manifest.Bindings.ToDictionary(binding => binding.TagKey);

        var result = manifest.Apply(
            "opcua",
            ManifestRevisionA,
            observedAtUtc.AddMinutes(1),
            [Entry("temperature", observedAtUtc.AddMinutes(1), activationStatus: "disabled")]);

        Assert.Equal(ManifestApplyDisposition.Conflict, result.Disposition);
        Assert.Equal(ManifestRevisionA, manifest.ManifestRevision);
        Assert.Equal(observedAtUtc, manifest.ManifestObservedAtUtc);
        Assert.Equal(2, manifest.Bindings.Count);
        Assert.All(manifest.Bindings, binding =>
        {
            Assert.True(binding.IsCurrent);
            Assert.Null(binding.RetiredAtUtc);
            Assert.Equal("active", binding.ActivationStatus);
            Assert.Same(acceptedBindings[binding.TagKey], binding);
        });
    }

    [Fact]
    public void Connector_manifest_older_observation_is_stale()
    {
        var acceptedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(acceptedAtUtc, ManifestRevisionA, [Entry("temperature", acceptedAtUtc)]);

        var result = manifest.Apply("opcua", ManifestRevisionB, acceptedAtUtc.AddTicks(-1), [Entry("pressure", acceptedAtUtc)]);

        Assert.Equal(ManifestApplyDisposition.Stale, result.Disposition);
        Assert.Equal(ManifestRevisionA, manifest.ManifestRevision);
        Assert.Equal("temperature", Assert.Single(manifest.Bindings).TagKey);
    }

    [Fact]
    public void Connector_manifest_same_observation_with_different_revision_is_conflict()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(observedAtUtc, ManifestRevisionA, [Entry("temperature", observedAtUtc)]);

        var result = manifest.Apply("opcua", ManifestRevisionB, observedAtUtc, [Entry("pressure", observedAtUtc)]);

        Assert.Equal(ManifestApplyDisposition.Conflict, result.Disposition);
        Assert.Equal(ManifestRevisionA, result.AcceptedManifestRevision);
        Assert.Equal(observedAtUtc, result.AcceptedManifestObservedAtUtc);
    }

    [Fact]
    public void Connector_manifest_later_observation_accepts_rollback_to_earlier_revision()
    {
        var firstObservedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(firstObservedAtUtc, ManifestRevisionA, [Entry("temperature", firstObservedAtUtc)]);
        manifest.Apply("opcua", ManifestRevisionB, firstObservedAtUtc.AddMinutes(1), [Entry("pressure", firstObservedAtUtc.AddMinutes(1))]);

        var result = manifest.Apply("opcua", ManifestRevisionA, firstObservedAtUtc.AddMinutes(2), [Entry("temperature", firstObservedAtUtc.AddMinutes(2))]);

        Assert.Equal(ManifestApplyDisposition.Accepted, result.Disposition);
        Assert.Equal(ManifestRevisionA, manifest.ManifestRevision);
        Assert.Equal(firstObservedAtUtc.AddMinutes(2), manifest.ManifestObservedAtUtc);
    }

    [Fact]
    public void Connector_manifest_omitted_binding_is_retired_and_readding_revives_same_projection()
    {
        var firstObservedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(firstObservedAtUtc, ManifestRevisionA,
        [
            Entry("temperature", firstObservedAtUtc),
            Entry("pressure", firstObservedAtUtc),
        ]);
        var original = manifest.Bindings.Single(x => x.TagKey == "pressure");

        manifest.Apply("opcua", ManifestRevisionB, firstObservedAtUtc.AddMinutes(1), [Entry("temperature", firstObservedAtUtc.AddMinutes(1))]);

        Assert.False(original.IsCurrent);
        Assert.Equal(firstObservedAtUtc.AddMinutes(1), original.RetiredAtUtc);

        manifest.Apply("opcua", ManifestRevisionA, firstObservedAtUtc.AddMinutes(2),
        [
            Entry("temperature", firstObservedAtUtc.AddMinutes(2)),
            Entry("pressure", firstObservedAtUtc.AddMinutes(2)),
        ]);

        var revived = manifest.Bindings.Single(x => x.TagKey == "pressure");
        Assert.Equal(original.Id, revived.Id);
        Assert.True(revived.IsCurrent);
        Assert.Null(revived.RetiredAtUtc);
    }

    [Fact]
    public void Connector_manifest_older_activation_observation_does_not_overwrite_newer_runtime_fact()
    {
        var manifestObservedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var activationObservedAtUtc = manifestObservedAtUtc.AddMinutes(5);
        var manifest = CreateManifest(manifestObservedAtUtc, ManifestRevisionA,
        [
            Entry("temperature", activationObservedAtUtc, activationStatus: "active"),
        ]);

        manifest.Apply("opcua", ManifestRevisionA, manifestObservedAtUtc.AddMinutes(1),
        [
            Entry("temperature", activationObservedAtUtc.AddTicks(-1), activationStatus: "error", activationErrorCode: "late-error"),
        ]);

        var binding = Assert.Single(manifest.Bindings);
        Assert.Equal("active", binding.ActivationStatus);
        Assert.Equal(activationObservedAtUtc, binding.ActivationObservedAtUtc);
        Assert.Null(binding.ActivationErrorCode);
    }

    [Fact]
    public void Connector_manifest_business_keys_isolate_organization_environment_and_connector()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(observedAtUtc, ManifestRevisionA, [Entry("temperature", observedAtUtc)]);

        Assert.True(manifest.HasSameBusinessKey("org-001", "env-dev", "line-a-primary"));
        Assert.False(manifest.HasSameBusinessKey("org-002", "env-dev", "line-a-primary"));
        Assert.False(manifest.HasSameBusinessKey("org-001", "env-prod", "line-a-primary"));
        Assert.False(manifest.HasSameBusinessKey("org-001", "env-dev", "line-b-primary"));

        var binding = Assert.Single(manifest.Bindings);
        Assert.True(binding.HasSameBusinessKey("org-001", "env-dev", "line-a-primary", "DEV-CNC-01", "TEMPERATURE"));
        Assert.False(binding.HasSameBusinessKey("org-001", "env-dev", "line-b-primary", "DEV-CNC-01", "temperature"));
    }

    [Fact]
    public void Connector_manifest_rejects_invalid_revision_and_duplicate_binding_keys()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => CreateManifest(observedAtUtc, ManifestRevisionA.ToUpperInvariant(), [Entry("temperature", observedAtUtc)]));
        Assert.Throws<ArgumentException>(() => CreateManifest(observedAtUtc, ManifestRevisionA,
        [
            Entry("Temperature", observedAtUtc),
            Entry("temperature", observedAtUtc),
        ]));
    }

    [Fact]
    public void Connector_manifest_sanitizes_and_bounds_activation_error_fields()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(observedAtUtc, ManifestRevisionA,
        [
            Entry(
                "temperature",
                observedAtUtc,
                activationStatus: "ERROR",
                activationErrorCode: $" code\r\n{new string('x', 200)} ",
                activationErrorMessage: $" failed\u0000\r\n{new string('y', 600)} "),
        ]);

        var binding = Assert.Single(manifest.Bindings);
        Assert.Equal("error", binding.ActivationStatus);
        Assert.Equal(128, binding.ActivationErrorCode!.Length);
        Assert.Equal(500, binding.ActivationErrorMessage!.Length);
        Assert.DoesNotContain(binding.ActivationErrorCode, char.IsControl);
        Assert.DoesNotContain(binding.ActivationErrorMessage, char.IsControl);
    }

    [Fact]
    public void Connector_manifest_rejected_newer_payload_does_not_partially_mutate_accepted_state()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var manifest = CreateManifest(observedAtUtc, ManifestRevisionA, [Entry("temperature", observedAtUtc)]);
        var acceptedBinding = Assert.Single(manifest.Bindings);

        Assert.Throws<ArgumentException>(() => manifest.Apply(
            "mqtt",
            ManifestRevisionB,
            observedAtUtc.AddMinutes(1),
            [Entry("pressure", observedAtUtc.AddMinutes(1), activationStatus: "unknown")]));

        Assert.Equal("opcua", manifest.SourceSystem);
        Assert.Equal(ManifestRevisionA, manifest.ManifestRevision);
        Assert.Equal(observedAtUtc, manifest.ManifestObservedAtUtc);
        Assert.True(acceptedBinding.IsCurrent);
        Assert.Null(acceptedBinding.RetiredAtUtc);
        Assert.Equal("temperature", acceptedBinding.TagKey);
    }

    [Fact]
    public void Telemetry_tag_creation_normalizes_identity_and_raises_created_event()
    {
        var tag = TelemetryTag.Create("org-001", "env-dev", "DEV-CNC-01", "Spindle.Speed", "number", "rpm", "sample-10s");

        Assert.Equal("org-001", tag.OrganizationId);
        Assert.Equal("env-dev", tag.EnvironmentId);
        Assert.Equal("DEV-CNC-01", tag.DeviceAssetId);
        Assert.Equal("spindle.speed", tag.TagKey);
        Assert.NotEqual(default, tag.Id);
        Assert.IsType<TelemetryTagCreatedDomainEvent>(tag.GetDomainEvents().Single());
    }

    private static ConnectorTagManifest CreateManifest(
        DateTimeOffset observedAtUtc,
        string revision,
        IReadOnlyCollection<ConnectorTagManifestEntry> entries)
    {
        return ConnectorTagManifest.Create(
            "org-001",
            "env-dev",
            "line-a-primary",
            "opcua",
            revision,
            observedAtUtc,
            entries);
    }

    private static ConnectorTagManifestEntry Entry(
        string tagKey,
        DateTimeOffset activationObservedAtUtc,
        string activationStatus = "active",
        string? activationErrorCode = null,
        string? activationErrorMessage = null)
    {
        return new ConnectorTagManifestEntry(
            "DEV-CNC-01",
            tagKey,
            Enabled: true,
            ProtocolAddress: $"ns=2;s={tagKey}",
            activationStatus,
            activationObservedAtUtc,
            activationErrorCode,
            activationErrorMessage);
    }

    [Fact]
    public void Telemetry_tag_business_key_is_unique_per_organization_environment_device_and_tag_key()
    {
        var left = TelemetryTag.Create("org-001", "env-dev", "DEV-CNC-01", "Spindle.Speed", "number", "rpm", "sample-10s");
        var right = TelemetryTag.Create("org-001", "env-dev", "DEV-CNC-01", "spindle.speed", "number", "rpm", "sample-10s");
        var otherDevice = TelemetryTag.Create("org-001", "env-dev", "DEV-CNC-02", "spindle.speed", "number", "rpm", "sample-10s");

        Assert.True(left.HasSameBusinessKey(right));
        Assert.False(left.HasSameBusinessKey(otherDevice));
    }

    [Fact]
    public void Device_state_snapshot_source_sequence_is_idempotent_per_stream()
    {
        var occurredAtUtc = DateTimeOffset.UtcNow;
        var first = DeviceStateSnapshot.Record("org-001", "env-dev", "DEV-CNC-01", "running", occurredAtUtc, "connector-seq-001");
        var duplicate = DeviceStateSnapshot.Record("org-001", "env-dev", "DEV-CNC-01", "running", occurredAtUtc, "connector-seq-001");
        var conflicting = DeviceStateSnapshot.Record("org-001", "env-dev", "DEV-CNC-01", "idle", occurredAtUtc, "connector-seq-001");

        Assert.True(first.IsSameSourceSequence(duplicate));
        Assert.True(first.HasSamePayload(duplicate));
        Assert.False(first.HasSamePayload(conflicting));
        Assert.IsType<DeviceStateChangedDomainEvent>(first.GetDomainEvents().Single());
    }

    [Fact]
    public void Alarm_external_id_duplicate_is_idempotent_for_same_payload_and_rejects_conflicts()
    {
        var raisedAtUtc = DateTimeOffset.UtcNow;
        var alarm = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", raisedAtUtc, "alarm-ext-001");
        var duplicate = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", raisedAtUtc, "alarm-ext-001");
        var conflicting = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "warning", raisedAtUtc, "alarm-ext-001");

        Assert.True(alarm.IsSameExternalAlarm(duplicate));
        Assert.True(alarm.HasSameRaisePayload(duplicate));
        Assert.False(alarm.HasSameRaisePayload(conflicting));
        Assert.Throws<InvalidOperationException>(() => alarm.EnsureCompatibleDuplicate(conflicting));
        Assert.IsType<AlarmRaisedDomainEvent>(alarm.GetDomainEvents().Single());
    }

    [Theory]
    [InlineData(">", 90, 80, 91, true)]
    [InlineData(">=", 90, 80, 90, true)]
    [InlineData("<", 10, 9, 12, true)]
    [InlineData("<=", 10, 10, 12, true)]
    [InlineData("==", 42, 42, 43, true)]
    [InlineData("==", 42, 41, 42, false)]
    [InlineData("!=", 42, 41, 42, true)]
    [InlineData("!=", 42, 42, 43, false)]
    [InlineData(">", 90, 80, 89, false)]
    [InlineData("<", 10, 10, 12, false)]
    public void Alarm_rule_evaluates_average_or_max_value_against_threshold(
        string comparisonOperator,
        decimal thresholdValue,
        decimal averageValue,
        decimal maxValue,
        bool expected)
    {
        var rule = AlarmRule.Configure("org-001", "env-dev", "DEV-CNC-01", "TEMP_RULE", "TEMP_HIGH", "warning", "temperature", comparisonOperator, thresholdValue, "celsius", true);

        Assert.Equal(expected, rule.Evaluate(averageValue, maxValue));
    }

    [Fact]
    public void Alarm_clear_is_idempotent_and_cannot_precede_raise_time()
    {
        var raisedAtUtc = DateTimeOffset.UtcNow;
        var alarm = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", raisedAtUtc, "alarm-ext-001");

        Assert.Throws<ArgumentOutOfRangeException>(() => alarm.Clear(raisedAtUtc.AddSeconds(-1), "operator-001", "temperature normalized"));

        alarm.Clear(raisedAtUtc.AddMinutes(10), "operator-001", "temperature normalized");
        alarm.Clear(raisedAtUtc.AddMinutes(10), "operator-001", "temperature normalized");

        Assert.Equal("cleared", alarm.Status);
        Assert.Equal(2, alarm.GetDomainEvents().Count);
        Assert.IsType<AlarmClearedDomainEvent>(alarm.GetDomainEvents().Last());
    }

    [Fact]
    public void Alarm_acknowledge_is_idempotent_and_preserves_clear_compatibility()
    {
        var raisedAtUtc = new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.Zero);
        var acknowledgedAtUtc = raisedAtUtc.AddMinutes(5);
        var alarm = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", raisedAtUtc, "alarm-ext-001");

        alarm.Acknowledge(acknowledgedAtUtc, "operator-001");
        alarm.Acknowledge(acknowledgedAtUtc, "operator-001");
        alarm.Clear(raisedAtUtc.AddMinutes(10), "operator-001", "temperature normalized");

        Assert.Equal("cleared", alarm.Status);
        Assert.Equal("operator-001", alarm.AcknowledgedBy);
        Assert.Equal(acknowledgedAtUtc, alarm.AcknowledgedAtUtc);
        Assert.Single(alarm.GetDomainEvents().OfType<AlarmAcknowledgedDomainEvent>());
        Assert.Single(alarm.GetDomainEvents().OfType<AlarmClearedDomainEvent>());
    }

    [Fact]
    public void Alarm_shelve_temporarily_suppresses_escalation_and_expires_to_previous_active_state()
    {
        var raisedAtUtc = new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.Zero);
        var alarm = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", raisedAtUtc, "alarm-ext-001");
        alarm.Acknowledge(raisedAtUtc.AddMinutes(2), "operator-001");

        alarm.Shelve(raisedAtUtc.AddMinutes(3), raisedAtUtc.AddMinutes(33), "operator-001", "maintenance window");
        alarm.Shelve(raisedAtUtc.AddMinutes(4), raisedAtUtc.AddMinutes(34), "operator-001", "duplicate request");

        Assert.Equal("shelved", alarm.Status);
        Assert.True(alarm.IsShelvedAt(raisedAtUtc.AddMinutes(20)));
        Assert.False(alarm.ShouldEscalateAt(raisedAtUtc.AddMinutes(20), TimeSpan.FromMinutes(10), ["critical"]));
        Assert.False(alarm.ExpireShelving(raisedAtUtc.AddMinutes(20)));
        Assert.True(alarm.ExpireShelving(raisedAtUtc.AddMinutes(40)));
        Assert.Equal("acknowledged", alarm.Status);
        Assert.False(alarm.IsShelvedAt(raisedAtUtc.AddMinutes(40)));
        Assert.Single(alarm.GetDomainEvents().OfType<AlarmShelvedDomainEvent>());
        Assert.Single(alarm.GetDomainEvents().OfType<AlarmUnshelvedDomainEvent>());
    }

    [Fact]
    public void Alarm_can_be_unshelved_before_expiry()
    {
        var raisedAtUtc = new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.Zero);
        var alarm = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", raisedAtUtc, "alarm-ext-001");
        alarm.Shelve(raisedAtUtc.AddMinutes(3), raisedAtUtc.AddMinutes(33), "operator-001", "maintenance window");

        Assert.True(alarm.Unshelve(raisedAtUtc.AddMinutes(10)));

        Assert.Equal("raised", alarm.Status);
        Assert.False(alarm.IsShelvedAt(raisedAtUtc.AddMinutes(20)));
        Assert.Single(alarm.GetDomainEvents().OfType<AlarmUnshelvedDomainEvent>());
    }

    [Fact]
    public void Alarm_escalates_once_for_high_severity_or_unacknowledged_timeout()
    {
        var raisedAtUtc = new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.Zero);
        var critical = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", raisedAtUtc, "alarm-ext-001");
        var warning = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-02", "PRESSURE_LOW", "warning", raisedAtUtc, "alarm-ext-002");

        Assert.True(critical.ShouldEscalateAt(raisedAtUtc.AddMinutes(1), TimeSpan.FromMinutes(30), ["critical"]));
        Assert.False(warning.ShouldEscalateAt(raisedAtUtc.AddMinutes(10), TimeSpan.FromMinutes(30), ["critical"]));
        Assert.True(warning.ShouldEscalateAt(raisedAtUtc.AddMinutes(31), TimeSpan.FromMinutes(30), ["critical"]));

        critical.Escalate(raisedAtUtc.AddMinutes(1), "critical-severity", ["role:maintenance-manager"]);
        critical.Escalate(raisedAtUtc.AddMinutes(2), "critical-severity", ["role:maintenance-manager"]);

        Assert.Equal(raisedAtUtc.AddMinutes(1), critical.EscalatedAtUtc);
        Assert.Equal("critical-severity", critical.EscalationReason);
        Assert.Equal(["role:maintenance-manager"], critical.EscalationRecipientRefs);
        Assert.Single(critical.GetDomainEvents().OfType<AlarmEscalatedDomainEvent>());
        Assert.False(critical.ShouldEscalateAt(raisedAtUtc.AddMinutes(40), TimeSpan.FromMinutes(30), ["critical"]));
    }

    [Fact]
    public void Alarm_cannot_be_directly_escalated_while_shelved()
    {
        var raisedAtUtc = new DateTimeOffset(2026, 7, 6, 8, 0, 0, TimeSpan.Zero);
        var alarm = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", raisedAtUtc, "alarm-ext-001");
        alarm.Shelve(raisedAtUtc.AddMinutes(3), raisedAtUtc.AddMinutes(33), "operator-001", "maintenance window");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            alarm.Escalate(raisedAtUtc.AddMinutes(10), "critical-severity", ["role:maintenance-manager"]));

        Assert.Equal("shelved alarms cannot be escalated.", ex.Message);
        Assert.Null(alarm.EscalatedAtUtc);
        Assert.Empty(alarm.GetDomainEvents().OfType<AlarmEscalatedDomainEvent>());
    }

    [Fact]
    public void Telemetry_summary_keeps_coarse_facts_not_raw_time_series()
    {
        var summary = TelemetrySummary.Record("org-001", "env-dev", "DEV-CNC-01", "spindle.speed", new DateTimeOffset(2026, 5, 23, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 23, 10, 5, 0, TimeSpan.Zero), 30, 1200m, 1500m, 1350m);

        Assert.Equal(30, summary.SampleCount);
        Assert.Equal(1200m, summary.MinValue);
        Assert.Equal(1500m, summary.MaxValue);
        Assert.Equal(1350m, summary.AverageValue);
    }

    [Fact]
    public void Public_domain_facts_do_not_expose_payload_credential_or_scada_concepts()
    {
        var publicNames = new[]
            {
                typeof(TelemetryTag),
                typeof(DeviceStateSnapshot),
                typeof(AlarmEvent),
                typeof(TelemetryRawSample),
                typeof(TelemetryRollup),
                typeof(TelemetrySummary),
            }
            .SelectMany(type => type.GetMembers(BindingFlags.Instance | BindingFlags.Public).Select(member => $"{type.Name}.{member.Name}"))
            .ToArray();

        var forbidden = new[] { "CommandPayload", "Credential", "Secret", "Password", "Scada" };
        Assert.DoesNotContain(publicNames, name => forbidden.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }
}
