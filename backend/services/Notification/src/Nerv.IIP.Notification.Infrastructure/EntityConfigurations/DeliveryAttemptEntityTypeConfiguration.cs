using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Notification.Infrastructure.EntityConfigurations;

public sealed class DeliveryAttemptEntityTypeConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable("delivery_attempts", table => table.HasComment("Notification delivery attempt records for future provider integrations."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Delivery attempt identifier.");
        builder.Property(x => x.NotificationMessageId)
            .HasConversion(x => x.Id, x => new NotificationMessageId(x))
            .IsRequired()
            .HasComment("Notification message identifier targeted by the attempt.");
        builder.Property(x => x.Channel).IsRequired().HasMaxLength(64).HasComment("Delivery channel name.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32).HasComment("Delivery attempt status.");
        builder.Property(x => x.AttemptedAtUtc).HasComment("UTC time when delivery was attempted.");
        builder.Property(x => x.AttemptNo).HasComment("One-based delivery attempt number for this message and channel.");
        builder.Property(x => x.NextRetryAtUtc).HasComment("UTC time when a failed attempt becomes eligible for retry; null after success or dead letter.");
        builder.Property(x => x.FailureReason).HasMaxLength(1000).HasComment("Optional provider failure reason.");

        builder.HasIndex(x => x.NotificationMessageId);
        builder.HasIndex(x => new { x.Channel, x.Status, x.AttemptedAtUtc });
        builder.HasIndex(x => new { x.Status, x.NextRetryAtUtc });
        builder.HasOne<NotificationMessage>().WithMany().HasForeignKey(x => x.NotificationMessageId).OnDelete(DeleteBehavior.Cascade);
    }
}
