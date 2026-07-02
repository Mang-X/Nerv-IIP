using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class DefectRecordEntityTypeConfiguration : IEntityTypeConfiguration<DefectRecord>
{
    public void Configure(EntityTypeBuilder<DefectRecord> builder)
    {
        builder.ToTable("defect_records", tableBuilder =>
            tableBuilder.HasComment("MES in-process defect facts recorded against work orders and optional operation tasks."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Defect record aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the defect record.");
        builder.Property(x => x.DefectNo).HasColumnName("defect_no").IsRequired().HasMaxLength(100).HasComment("MES defect record number allocated by the service numbering counter.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work order id that produced the defect.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").HasMaxLength(100).HasComment("Optional MES operation task id that produced the defect.");
        builder.Property(x => x.DefectCode).HasColumnName("defect_code").IsRequired().HasMaxLength(100).HasComment("Defect reason or code captured by MES.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).IsRequired().HasComment("Defect quantity captured by MES.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("MES defect lifecycle status.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired().HasComment("UTC time when the defect was recorded.");
        builder.Property(x => x.NcrId).HasColumnName("ncr_id").HasMaxLength(100).HasComment("Quality NCR public id linked to this MES defect when disposition is known.");
        builder.Property(x => x.NcrCode).HasColumnName("ncr_code").HasMaxLength(100).HasComment("Quality NCR business code linked to this MES defect when disposition is known.");
        builder.Property(x => x.DispositionType).HasColumnName("disposition_type").HasMaxLength(100).HasComment("Quality disposition type accepted for this MES defect.");
        builder.Property(x => x.DispositionReferenceId).HasColumnName("disposition_reference_id").HasMaxLength(100).HasComment("Downstream disposition reference such as rework work order, scrap movement or return document.");
        builder.Property(x => x.ClosedAtUtc).HasColumnName("closed_at_utc").HasComment("UTC time when the MES defect was closed by non-rework disposition.");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasConstraintName("fk_defect_records_work_orders")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DefectNo })
            .IsUnique()
            .HasDatabaseName("ux_defect_records_scope_defect_no");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.RecordedAtUtc })
            .HasDatabaseName("ix_defect_records_scope_work_order_time");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId, x.RecordedAtUtc })
            .HasDatabaseName("ix_defect_records_scope_operation_time");
    }
}
