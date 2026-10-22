using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Business.Quality.Infrastructure.IntegrationEvents;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Quality.Infrastructure.EntityConfigurations;

public sealed class ProcessedIntegrationEventEntityTypeConfiguration
    : IEntityTypeConfiguration<ProcessedIntegrationEvent>
{
    public const string UniqueIndexName = "ux_quality_processed_integration_events_consumer_event_id";

    public void Configure(EntityTypeBuilder<ProcessedIntegrationEvent> builder)
    {
        builder.ToTable(
            "processed_integration_events",
            table => table.HasComment("Integration events processed by BusinessQuality using the ADR 0011 event-id consumer inbox."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Processed integration event identifier.");
        builder.Property(x => x.ConsumerName).HasColumnName("consumer_name").IsRequired().HasMaxLength(256).HasComment("BusinessQuality integration event consumer name.");
        builder.Property(x => x.EventId).HasColumnName("event_id").IsRequired().HasMaxLength(256).HasComment("Globally unique source event id used with consumer_name as the minimum inbox key.");
        builder.Property(x => x.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(256).HasComment("Integration event type.");
        builder.Property(x => x.EventVersion).HasColumnName("event_version").HasComment("Integration event contract version.");
        builder.Property(x => x.SourceService).HasColumnName("source_service").IsRequired().HasMaxLength(128).HasComment("Service that produced the integration event.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(512).HasComment("Publisher business idempotency key retained for traceability.");
        builder.Property(x => x.ProcessedAtUtc).HasColumnName("processed_at_utc").HasComment("UTC time when BusinessQuality accepted the event into its transactional inbox.");

        builder.HasIndex(x => new { x.ConsumerName, x.EventId })
            .IsUnique()
            .HasDatabaseName(UniqueIndexName);
        builder.HasIndex(x => new { x.SourceService, x.EventType, x.ProcessedAtUtc })
            .HasDatabaseName("ix_quality_processed_integration_events_source_type_processed_at");
    }
}
