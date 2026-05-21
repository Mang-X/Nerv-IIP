using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Notification.Infrastructure.EntityConfigurations;

public sealed class NotificationMessageEntityTypeConfiguration : IEntityTypeConfiguration<NotificationMessage>
{
    public void Configure(EntityTypeBuilder<NotificationMessage> builder)
    {
        builder.ToTable("notification_messages", table => table.HasComment("Recipient-specific in-app notification messages owned by notification intents."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Notification message identifier.");
        builder.Property(x => x.NotificationIntentId)
            .HasConversion(x => x.Id, x => new NotificationIntentId(x))
            .IsRequired()
            .HasComment("Owning notification intent identifier.");
        builder.Property(x => x.RecipientRef).IsRequired().HasMaxLength(256).HasComment("Recipient reference such as user or role.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32).HasComment("Message read status.");
        builder.Property(x => x.Severity).IsRequired().HasMaxLength(32).HasComment("Notification severity copied from the intent.");
        builder.Property(x => x.Title).IsRequired().HasMaxLength(256).HasComment("User-visible notification title.");
        builder.Property(x => x.Summary).IsRequired().HasMaxLength(2000).HasComment("User-visible notification summary.");
        builder.Property(x => x.ResourceType).HasMaxLength(128).HasComment("Optional weak resource reference type.");
        builder.Property(x => x.ResourceId).HasMaxLength(256).HasComment("Optional weak resource reference identifier.");
        builder.Property(x => x.FileId).HasMaxLength(256).HasComment("Optional FileStorage file identifier reference.");
        builder.Property(x => x.CreatedAtUtc).HasComment("UTC time when the message was created.");
        builder.Property(x => x.ReadAtUtc).HasComment("UTC time when the recipient first marked the message read.");
        builder.Ignore(x => x.IsRead);

        builder.HasIndex(x => new { x.RecipientRef, x.Status, x.CreatedAtUtc }).IsDescending(false, false, true);
        builder.HasIndex(x => x.NotificationIntentId);
    }
}
