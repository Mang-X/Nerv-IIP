using Nerv.IIP.Business.DemandPlanning.Infrastructure.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.EntityConfigurations;

public sealed class ProcessedIntegrationEventEntityTypeConfiguration : IEntityTypeConfiguration<ProcessedIntegrationEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedIntegrationEvent> builder)
    {
        builder.ToTable("processed_integration_events", table => table.HasComment("Integration events already processed by DemandPlanning for idempotent consumption."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Processed integration event identifier.");
        builder.Property(x => x.ConsumerName).HasColumnName("consumer_name").IsRequired().HasMaxLength(256).HasComment("DemandPlanning integration event consumer name.");
        builder.Property(x => x.EventId).HasColumnName("event_id").IsRequired().HasMaxLength(256).HasComment("Source event identifier retained for traceability.");
        builder.Property(x => x.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(256).HasComment("Integration event type.");
        builder.Property(x => x.EventVersion).HasColumnName("event_version").HasComment("Integration event contract version.");
        builder.Property(x => x.SourceService).HasColumnName("source_service").IsRequired().HasMaxLength(128).HasComment("Service that produced the integration event.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(512).HasComment("Deterministic idempotency key unique within the consumer.");
        builder.Property(x => x.ProcessedAtUtc).HasColumnName("processed_at_utc").HasComment("UTC time when DemandPlanning processed the event.");
        builder.HasIndex(x => new { x.ConsumerName, x.IdempotencyKey }).IsUnique().HasDatabaseName(ProcessedIntegrationEventInbox.UniqueIndexName);
        builder.HasIndex(x => new { x.SourceService, x.EventType, x.ProcessedAtUtc }).HasDatabaseName("ix_dp_processed_events_source_type_processed_at");
    }
}
