using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.EntityConfigurations;

public sealed class InspectionTaskEntityTypeConfiguration : IEntityTypeConfiguration<InspectionTask>
{
    public void Configure(EntityTypeBuilder<InspectionTask> builder)
    {
        builder.ToTable("inspection_tasks", tableBuilder =>
            tableBuilder.HasComment("Quality pending inspection task facts generated from upstream receipt and production events."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Inspection task aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the task.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the task applies.");
        builder.Property(x => x.InspectionPlanId).HasColumnName("inspection_plan_id").IsRequired().HasComment("Matched active inspection plan id.");
        builder.Property(x => x.InspectionRecordId).HasColumnName("inspection_record_id").HasComment("Inspection record id created from this task once completed.");
        builder.Property(x => x.SourceType).HasColumnName("source_type").IsRequired().HasMaxLength(50).HasComment("Task source type: receiving, operation or final.");
        builder.Property(x => x.SourceService).HasColumnName("source_service").IsRequired().HasMaxLength(100).HasComment("Upstream service that emitted the task source event.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(150).HasComment("Source document public id.");
        builder.Property(x => x.SourceDocumentLineId).HasColumnName("source_document_line_id").HasMaxLength(150).HasComment("Optional source document line or operation id.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("SKU code awaiting inspection.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Quantity awaiting inspection.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("Task quantity unit of measure code.");
        builder.Property(x => x.BatchNo).HasColumnName("batch_no").HasMaxLength(100).HasComment("Optional lot or batch number from the source event.");
        builder.Property(x => x.SerialNo).HasColumnName("serial_no").HasMaxLength(100).HasComment("Optional serial number from the source event.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Inspection task status: pending, in-progress or completed.");
        builder.Property(x => x.AssignedUserId).HasColumnName("assigned_user_id").HasMaxLength(150).HasComment("Optional inspector user id that started the task.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the task was generated.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the task was last changed.");
        builder.Property(x => x.DueAtUtc).HasColumnName("due_at_utc").IsRequired().HasComment("UTC due time for overdue inspection reminders.");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").HasComment("UTC time when inspection work started.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC time when inspection work completed.");
        builder.Property(x => x.OverdueReminderSentAtUtc).HasColumnName("overdue_reminder_sent_at_utc").HasComment("UTC time when the overdue reminder event was first emitted.");
        builder.Property(x => x.TriggerIdempotencyKey).HasColumnName("trigger_idempotency_key").IsRequired().HasMaxLength(300).HasComment("Idempotency key derived from the source event and source line.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status, x.DueAtUtc })
            .HasDatabaseName("ix_inspection_tasks_scope_status_due");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceType, x.SourceService, x.SourceDocumentId, x.SourceDocumentLineId, x.SkuCode })
            .IsUnique()
            .HasDatabaseName("ux_inspection_tasks_scope_source_sku");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TriggerIdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_inspection_tasks_scope_trigger_key");
    }
}
