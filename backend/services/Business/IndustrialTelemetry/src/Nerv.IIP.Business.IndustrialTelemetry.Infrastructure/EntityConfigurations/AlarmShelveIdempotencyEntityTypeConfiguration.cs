using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmShelveIdempotencyAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class AlarmShelveIdempotencyEntityTypeConfiguration : IEntityTypeConfiguration<AlarmShelveIdempotency>
{
    public void Configure(EntityTypeBuilder<AlarmShelveIdempotency> builder)
    {
        builder.ToTable("alarm_shelve_idempotency", table => table.HasComment("Persistent per-(alarm, idempotency key) shelve dedup records with a payload fingerprint; makes shelve durably idempotent independent of the alarm window/status."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Shelve idempotency record identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasColumnName("organization_id").HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasColumnName("environment_id").HasComment("Owning environment identifier.");
        builder.Property(x => x.AlarmEventId).IsRequired().HasMaxLength(50).HasColumnName("alarm_event_id").HasComment("Referenced alarm event identifier (string form; no cross-aggregate FK).");
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(150).HasColumnName("idempotency_key").HasComment("Caller-minted idempotency key of the shelve operation.");
        builder.Property(x => x.PayloadFingerprint).IsRequired().HasMaxLength(64).HasColumnName("payload_fingerprint").HasComment("SHA-256 hex of the canonical shelve payload; same key + same fingerprint replays, same key + different fingerprint conflicts.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC time when the idempotency record was created.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.AlarmEventId, x.IdempotencyKey })
            .IsUnique();
    }
}
