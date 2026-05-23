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
        builder.Property(x => x.RaisedAtUtc).HasColumnName("raised_at_utc").HasComment("UTC time when the alarm was raised.");
        builder.Property(x => x.ExternalAlarmId).IsRequired().HasMaxLength(150).HasColumnName("external_alarm_id").HasComment("External alarm identifier used for idempotent ingestion.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(50).HasColumnName("status").HasComment("Alarm lifecycle status.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").HasComment("UTC time when the alarm was recorded.");
        builder.Property(x => x.ClearedAtUtc).HasColumnName("cleared_at_utc").HasComment("UTC time when the alarm was cleared.");
        builder.Property(x => x.ClearedBy).HasMaxLength(150).HasColumnName("cleared_by").HasComment("Actor or system that cleared the alarm.");
        builder.Property(x => x.ClearReason).HasMaxLength(300).HasColumnName("clear_reason").HasComment("Reason recorded when the alarm was cleared.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ExternalAlarmId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.RaisedAtUtc });
    }
}
