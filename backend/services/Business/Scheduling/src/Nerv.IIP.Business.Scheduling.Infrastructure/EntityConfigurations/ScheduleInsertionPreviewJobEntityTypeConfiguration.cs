using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleInsertionPreviewJobAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class ScheduleInsertionPreviewJobEntityTypeConfiguration : IEntityTypeConfiguration<ScheduleInsertionPreviewJob>
{
    public void Configure(EntityTypeBuilder<ScheduleInsertionPreviewJob> builder)
    {
        builder.ToTable("schedule_insertion_preview_jobs", table => table.HasComment("Scheduling-owned asynchronous insertion preview jobs."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Insertion-preview job id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Business environment id.");
        builder.Property(x => x.InputJson).HasColumnName("input_json").HasColumnType("jsonb").IsRequired().HasComment("SchedulingInsertionPreviewInputContract v1; Scheduling owns and reads the accepted horizon and order selections.");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).HasComment("Created, Running, Completed or Failed execution fact.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC acceptance timestamp.");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").HasComment("UTC calculation start timestamp.");
        builder.Property(x => x.FinishedAtUtc).HasColumnName("finished_at_utc").HasComment("UTC terminal timestamp.");
        builder.Property(x => x.PreviewJson).HasColumnName("preview_json").HasColumnType("jsonb").HasComment("SchedulePlanContract v1 Preview result; Scheduling writes and reads it without persisting a plan.");
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasComment("Displayable calculation failure reason.");
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
    }
}
