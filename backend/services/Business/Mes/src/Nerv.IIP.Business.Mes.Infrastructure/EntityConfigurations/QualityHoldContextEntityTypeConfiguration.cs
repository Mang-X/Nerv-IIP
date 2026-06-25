using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class QualityHoldContextEntityTypeConfiguration : IEntityTypeConfiguration<QualityHoldContext>
{
    public void Configure(EntityTypeBuilder<QualityHoldContext> builder)
    {
        builder.ToTable("quality_hold_contexts", tableBuilder =>
            tableBuilder.HasComment("MES quality hold contexts projected from Quality inspection result facts for work order release and start gates."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Quality hold context id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the quality hold context.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work order id affected by the inspection result.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").HasMaxLength(100).HasComment("Optional MES operation task id affected by the inspection result.");
        builder.Property(x => x.SourceService).HasColumnName("source_service").IsRequired().HasMaxLength(100).HasComment("Source service referenced by the Quality inspection record.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(100).HasComment("Source document id referenced by the Quality inspection record.");
        builder.Property(x => x.InspectionRecordId).HasColumnName("inspection_record_id").IsRequired().HasMaxLength(100).HasComment("Quality inspection record id that last updated this hold context.");
        builder.Property(x => x.InspectionPlanId).HasColumnName("inspection_plan_id").HasMaxLength(100).HasComment("Optional Quality inspection plan id used for the inspection result.");
        builder.Property(x => x.Result).HasColumnName("result").IsRequired().HasMaxLength(50).HasComment("Latest Quality inspection result for the MES execution context.");
        builder.Property(x => x.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(100).HasComment("Latest Quality integration event type applied to this context.");
        builder.Property(x => x.DispositionReason).HasColumnName("disposition_reason").HasMaxLength(500).HasComment("Optional Quality disposition or rejection reason for the hold context.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired().HasComment("UTC time when the latest Quality inspection result was recorded.");
        builder.Property(x => x.Active).HasColumnName("active").IsRequired().HasComment("Whether this Quality context currently blocks MES release or operation start.");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasConstraintName("fk_quality_hold_contexts_work_orders")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceService, x.SourceDocumentId })
            .IsUnique()
            .HasDatabaseName("ux_quality_hold_contexts_scope_source");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.Active })
            .HasDatabaseName("ix_quality_hold_contexts_scope_work_order_active");
    }
}
