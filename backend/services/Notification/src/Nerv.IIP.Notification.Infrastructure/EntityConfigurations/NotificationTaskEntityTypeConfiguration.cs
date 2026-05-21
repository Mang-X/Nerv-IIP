using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Notification.Infrastructure.EntityConfigurations;

public sealed class NotificationTaskEntityTypeConfiguration : IEntityTypeConfiguration<NotificationTask>
{
    public void Configure(EntityTypeBuilder<NotificationTask> builder)
    {
        builder.ToTable("notification_tasks", table => table.HasComment("Actionable notification tasks owned by task notification intents."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Notification task identifier.");
        builder.Property(x => x.NotificationIntentId)
            .HasConversion(x => x.Id, x => new NotificationIntentId(x))
            .IsRequired()
            .HasComment("Owning notification intent identifier.");
        builder.Property(x => x.MessageId)
            .HasConversion(x => x.Id, x => new NotificationMessageId(x))
            .IsRequired()
            .HasComment("Notification message that owns the task surface.");
        builder.Property(x => x.RecipientRef).IsRequired().HasMaxLength(256).HasComment("Recipient reference such as user or role.");
        builder.Property(x => x.TaskType).IsRequired().HasMaxLength(128).HasComment("Actionable notification task type.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32).HasComment("Notification task status.");
        builder.Property(x => x.ActionRef).HasMaxLength(512).HasComment("Optional action reference for task handling.");
        builder.Property(x => x.CreatedAtUtc).HasComment("UTC time when the task was created.");

        builder.HasIndex(x => new { x.RecipientRef, x.Status, x.CreatedAtUtc }).IsDescending(false, false, true);
        builder.HasIndex(x => x.NotificationIntentId);
        builder.HasIndex(x => x.MessageId);
        builder.HasOne<NotificationMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}
