using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using NetCorePal.Extensions.Domain;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Notification.Infrastructure.EntityConfigurations;

public sealed class NotificationIntentEntityTypeConfiguration : IEntityTypeConfiguration<NotificationIntent>
{
    public void Configure(EntityTypeBuilder<NotificationIntent> builder)
    {
        builder.ToTable("notification_intents", table => table.HasComment("Notification intent aggregate roots submitted by platform services for in-app messages and tasks."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Notification intent identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(128).HasComment("Organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(128).HasComment("Environment identifier.");
        builder.Property(x => x.SourceService).IsRequired().HasMaxLength(128).HasComment("Service that produced the notification intent.");
        builder.Property(x => x.SourceEventType).IsRequired().HasMaxLength(256).HasComment("Source event type that produced the intent.");
        builder.Property(x => x.SourceEventId).IsRequired().HasMaxLength(256).HasComment("Source event identifier used for traceability.");
        builder.Property(x => x.IntentType).IsRequired().HasMaxLength(32).HasComment("Notification intent kind such as message or task.");
        builder.Property(x => x.Severity).IsRequired().HasMaxLength(32).HasComment("Notification severity.");
        builder.Property(x => x.DedupeKey).IsRequired().HasMaxLength(512).HasComment("Organization and environment scoped dedupe key.");
        builder.Property(x => x.ResourceType).HasMaxLength(128).HasComment("Optional weak resource reference type.");
        builder.Property(x => x.ResourceId).HasMaxLength(256).HasComment("Optional weak resource reference identifier.");
        builder.Property(x => x.FileId).HasMaxLength(256).HasComment("Optional FileStorage file identifier reference.");
        builder.Property(x => x.Title).IsRequired().HasMaxLength(256).HasComment("User-visible notification title.");
        builder.Property(x => x.Summary).IsRequired().HasMaxLength(2000).HasComment("User-visible notification summary.");
        builder.Property(x => x.CreatedAtUtc).HasComment("UTC time when the intent was created.");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");

        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceService, x.SourceEventType, x.DedupeKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceEventId });
        builder.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.NotificationIntentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Tasks).WithOne().HasForeignKey(x => x.NotificationIntentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.Tasks).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
