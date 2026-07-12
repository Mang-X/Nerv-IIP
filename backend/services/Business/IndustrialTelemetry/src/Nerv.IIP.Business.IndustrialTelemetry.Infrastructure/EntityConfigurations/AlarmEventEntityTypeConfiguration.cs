using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmEventAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class AlarmEventEntityTypeConfiguration : IEntityTypeConfiguration<AlarmEvent>
{
    public void Configure(EntityTypeBuilder<AlarmEvent> builder)
    {
        builder.ToTable("alarm_events", table => table.HasComment("BusinessIndustrialTelemetry controlled alarm lifecycle events."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Alarm event identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasColumnName("organization_id").HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasColumnName("environment_id").HasComment("Owning environment identifier.");
        builder.Property(x => x.DeviceAssetId).IsRequired().HasMaxLength(150).HasColumnName("device_asset_id").HasComment("Referenced MasterData device asset identifier.");
        builder.Property(x => x.AlarmCode).IsRequired().HasMaxLength(100).HasColumnName("alarm_code").HasComment("External or normalized alarm code.");
        builder.Property(x => x.Severity).IsRequired().HasMaxLength(50).HasColumnName("severity").HasComment("Alarm severity level.");
        builder.Property(x => x.Priority).IsRequired().HasMaxLength(50).HasColumnName("priority").HasComment("Independent alarm priority.");
        builder.Property(x => x.TagKey).HasMaxLength(150).HasColumnName("tag_key").HasComment("Telemetry tag key whose observed value raised this alarm.");
        builder.Property(x => x.ObservedValue).HasPrecision(18, 6).HasColumnName("observed_value").HasComment("Observed process value that raised this alarm.");
        builder.Property(x => x.ThresholdValue).HasPrecision(18, 6).HasColumnName("threshold_value").HasComment("Rule threshold value when this alarm was raised.");
        builder.Property(x => x.UnitCode).HasMaxLength(50).HasColumnName("unit_code").HasComment("Observed and threshold unit code.");
        builder.Property(x => x.RaisedAtUtc).HasColumnName("raised_at_utc").HasComment("UTC time when the alarm was raised.");
        builder.Property(x => x.ExternalAlarmId).IsRequired().HasMaxLength(150).HasColumnName("external_alarm_id").HasComment("External alarm identifier used for idempotent ingestion.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(50).HasColumnName("status").HasComment("Alarm lifecycle status.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").HasComment("UTC time when the alarm was recorded.");
        builder.Property(x => x.ClearedAtUtc).HasColumnName("cleared_at_utc").HasComment("UTC time when the alarm was cleared.");
        builder.Property(x => x.ClearedBy).HasMaxLength(150).HasColumnName("cleared_by").HasComment("Actor or system that cleared the alarm.");
        builder.Property(x => x.ClearReason).HasMaxLength(300).HasColumnName("clear_reason").HasComment("Reason recorded when the alarm was cleared.");
        builder.Property(x => x.AcknowledgedAtUtc).HasColumnName("acknowledged_at_utc").HasComment("UTC time when an operator acknowledged the active alarm.");
        builder.Property(x => x.AcknowledgedBy).HasMaxLength(150).HasColumnName("acknowledged_by").HasComment("Actor that acknowledged the active alarm.");
        builder.Property(x => x.ShelvedAtUtc).HasColumnName("shelved_at_utc").HasComment("UTC time when the alarm was temporarily shelved.");
        builder.Property(x => x.ShelvedUntilUtc).HasColumnName("shelved_until_utc").HasComment("UTC expiry time for temporary alarm shelving.");
        builder.Property(x => x.ShelvedBy).HasMaxLength(150).HasColumnName("shelved_by").HasComment("Actor that shelved the alarm.");
        builder.Property(x => x.ShelveReason).HasMaxLength(300).HasColumnName("shelve_reason").HasComment("Reason recorded when the alarm was shelved.");
        builder.Property(x => x.ShelveIdempotencyKey).HasMaxLength(150).HasColumnName("shelve_idempotency_key").HasComment("Idempotency key of the last applied shelve operation; a delayed duplicate delivery with the same key no-ops regardless of window.");
        builder.Property(x => x.EscalatedAtUtc).HasColumnName("escalated_at_utc").HasComment("UTC time when the alarm was escalated.");
        builder.Property(x => x.EscalationReason).HasMaxLength(100).HasColumnName("escalation_reason").HasComment("Reason code that triggered alarm escalation.");
        builder.Property(x => x.EscalationRecipientRefsText).HasMaxLength(1000).HasColumnName("escalation_recipient_refs").HasComment("Semicolon-separated Notification recipient refs used for alarm escalation.");
        builder.Ignore(x => x.EscalationRecipientRefs);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.AlarmCode, x.ExternalAlarmId })
            .IsUnique()
            .HasFilter("status <> 'cleared'");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.ExternalAlarmId })
            .IsUnique()
            .HasFilter("status <> 'cleared' AND tag_key IS NOT NULL");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.RaisedAtUtc });
    }
}
