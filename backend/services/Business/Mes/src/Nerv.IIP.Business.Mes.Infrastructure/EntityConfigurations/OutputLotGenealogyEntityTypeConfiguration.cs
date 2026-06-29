using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class OutputLotGenealogyEntityTypeConfiguration : IEntityTypeConfiguration<OutputLotGenealogy>
{
    public void Configure(EntityTypeBuilder<OutputLotGenealogy> builder)
    {
        builder.ToTable("output_lot_genealogies", tableBuilder =>
            tableBuilder.HasComment("MES output lot genealogy breakpoints linking reported finished-goods lots to work orders, operations, reports, and consumed material facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Output lot genealogy aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the MES genealogy context.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES business work order id that produced the lot.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES output operation task id that produced the lot.");
        builder.Property(x => x.ReportNo).HasColumnName("report_no").IsRequired().HasMaxLength(100).HasComment("MES production report number that created the output lot breakpoint.");
        builder.Property(x => x.ProducedLotNo).HasColumnName("produced_lot_no").IsRequired().HasMaxLength(100).HasComment("Produced finished-goods lot number assigned by MES.");
        builder.Property(x => x.SerialNo).HasColumnName("serial_no").HasMaxLength(100).HasComment("Optional produced serial number assigned by MES.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).IsRequired().HasComment("Good quantity represented by this output lot breakpoint.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the output lot genealogy breakpoint was recorded.");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasConstraintName("fk_output_lot_genealogies_work_orders")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationTask>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId })
            .HasConstraintName("fk_output_lot_genealogies_operation_tasks")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductionReport>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .HasConstraintName("fk_output_lot_genealogies_reports")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ProducedLotNo })
            .IsUnique()
            .HasDatabaseName("ux_output_lot_genealogies_scope_lot");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .IsUnique()
            .HasDatabaseName("ux_output_lot_genealogies_scope_report");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId })
            .HasDatabaseName("ix_output_lot_genealogies_scope_operation");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasDatabaseName("ix_output_lot_genealogies_scope_work_order");
    }
}
