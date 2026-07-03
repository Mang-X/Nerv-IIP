using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Notification.Infrastructure.EntityConfigurations;

public sealed class NotificationRecipientChannelBindingEntityTypeConfiguration : IEntityTypeConfiguration<NotificationRecipientChannelBinding>
{
    public void Configure(EntityTypeBuilder<NotificationRecipientChannelBinding> builder)
    {
        builder.ToTable("notification_recipient_channel_bindings", table => table.HasComment("Recipient to external delivery channel account bindings owned by Notification."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Recipient channel binding identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(128).HasComment("Organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(128).HasComment("Environment identifier.");
        builder.Property(x => x.RecipientRef).IsRequired().HasMaxLength(256).HasComment("Notification recipient reference, for example an IAM user ref.");
        builder.Property(x => x.Channel).IsRequired().HasMaxLength(64).HasComment("External delivery channel name.");
        builder.Property(x => x.RecipientAddress).IsRequired().HasMaxLength(512).HasComment("Provider-specific user id, email address or webhook URL; provider secrets are not stored here.");
        builder.Property(x => x.Enabled).HasComment("Whether this recipient binding is active.");
        builder.Property(x => x.CreatedAtUtc).HasComment("UTC time when the binding was created.");
        builder.Property(x => x.UpdatedAtUtc).HasComment("UTC time when the binding was last updated.");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");

        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RecipientRef, x.Channel }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Channel, x.Enabled });
    }
}
