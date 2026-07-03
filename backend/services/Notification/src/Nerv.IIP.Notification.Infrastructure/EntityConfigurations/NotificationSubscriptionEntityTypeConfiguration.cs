using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Notification.Infrastructure.EntityConfigurations;

public sealed class NotificationSubscriptionEntityTypeConfiguration : IEntityTypeConfiguration<NotificationSubscription>
{
    public void Configure(EntityTypeBuilder<NotificationSubscription> builder)
    {
        builder.ToTable("notification_subscriptions", table => table.HasComment("Recipient notification type subscriptions for external delivery channels."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Notification subscription identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(128).HasComment("Organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(128).HasComment("Environment identifier.");
        builder.Property(x => x.RecipientRef).IsRequired().HasMaxLength(256).HasComment("Notification recipient reference.");
        builder.Property(x => x.NotificationType).IsRequired().HasMaxLength(256).HasComment("Notification type, usually the source event type or wildcard '*'.");
        builder.Property(x => x.Channel).IsRequired().HasMaxLength(64).HasComment("Delivery channel included by the subscription.");
        builder.Property(x => x.Enabled).HasComment("Whether this subscription is active.");
        builder.Property(x => x.CreatedAtUtc).HasComment("UTC time when the subscription was created.");
        builder.Property(x => x.UpdatedAtUtc).HasComment("UTC time when the subscription was last updated.");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");

        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RecipientRef, x.NotificationType, x.Channel }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.NotificationType, x.Channel, x.Enabled });
    }
}
