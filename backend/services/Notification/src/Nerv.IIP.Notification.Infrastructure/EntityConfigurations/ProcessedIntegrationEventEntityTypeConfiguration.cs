using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Notification.Infrastructure.IntegrationEvents;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Notification.Infrastructure.EntityConfigurations;

public sealed class ProcessedIntegrationEventEntityTypeConfiguration : IEntityTypeConfiguration<ProcessedIntegrationEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedIntegrationEvent> builder)
    {
        builder.ToTable("processed_integration_events", table => table.HasComment("Integration events already processed by Notification for idempotent consumption."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Processed integration event identifier.");
        builder.Property(x => x.ConsumerName).IsRequired().HasMaxLength(256).HasComment("Notification integration event consumer name.");
        builder.Property(x => x.EventId).IsRequired().HasMaxLength(256).HasComment("Source integration event identifier retained for traceability; idempotency uses IdempotencyKey.");
        builder.Property(x => x.EventType).IsRequired().HasMaxLength(256).HasComment("Integration event type.");
        builder.Property(x => x.EventVersion).HasComment("Integration event contract version.");
        builder.Property(x => x.SourceService).IsRequired().HasMaxLength(128).HasComment("Service that produced the integration event.");
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(512).HasComment("Deterministic Notification idempotency key unique within a consumer.");
        builder.Property(x => x.ProcessedAtUtc).HasComment("UTC time when Notification processed the event.");

        builder.HasIndex(x => new { x.ConsumerName, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_processed_integration_events_consumer_idempotency_key");
        builder.HasIndex(x => new { x.SourceService, x.EventType, x.ProcessedAtUtc });
    }
}
