using System.Reflection;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceStateSnapshotAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.DomainEvents;

namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests;

public sealed class IndustrialTelemetryAggregateTests
{
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
    public void Public_domain_facts_do_not_expose_control_payload_credential_or_scada_concepts()
    {
        var publicNames = new[]
            {
                typeof(TelemetryTag),
                typeof(DeviceStateSnapshot),
                typeof(AlarmEvent),
                typeof(TelemetrySummary),
            }
            .SelectMany(type => type.GetMembers(BindingFlags.Instance | BindingFlags.Public).Select(member => $"{type.Name}.{member.Name}"))
            .ToArray();

        var forbidden = new[] { "Control", "CommandPayload", "Credential", "Secret", "Password", "Scada" };
        Assert.DoesNotContain(publicNames, name => forbidden.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }
}
