using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class ProductionReportLaborAllocationEntityTypeConfiguration : IEntityTypeConfiguration<ProductionReportLaborAllocation>
{
    public void Configure(EntityTypeBuilder<ProductionReportLaborAllocation> builder)
    {
        builder.ToTable("production_report_labor_allocations", table =>
        {
            table.HasComment("Immutable worker labor allocation snapshots created by completing MES production reports.");
            table.HasCheckConstraint("ck_production_report_labor_allocations_share_percent", "share_percent > 0 AND share_percent <= 100");
            table.HasCheckConstraint("ck_production_report_labor_allocations_ticks", "allocated_labor_ticks >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Production report labor allocation id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant scope.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment scope.");
        builder.Property(x => x.ReportNo).HasColumnName("report_no").IsRequired().HasMaxLength(100).HasComment("Completing production report number.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work order public id.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation task public id.");
        builder.Property(x => x.WorkerId).HasColumnName("worker_id").IsRequired().HasMaxLength(100).HasComment("Allocated MasterData worker user id snapshot.");
        builder.Property(x => x.WorkerName).HasColumnName("worker_name").HasMaxLength(200).HasComment("Allocated worker display name snapshot.");
        builder.Property(x => x.SharePercent).HasColumnName("share_percent").HasPrecision(7, 4).IsRequired().HasComment("Worker labor share captured when the operation completed.");
        builder.Property(x => x.AllocatedLaborTicks).HasColumnName("allocated_labor_ticks").IsRequired().HasComment("Final operation labor ticks allocated to this worker.");
        builder.HasOne<ProductionReport>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .HasConstraintName("fk_production_report_labor_allocations_reports")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasConstraintName("fk_production_report_labor_allocations_work_orders")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationTask>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId })
            .HasConstraintName("fk_production_report_labor_allocations_operation_tasks")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo, x.WorkerId })
            .IsUnique()
            .HasDatabaseName("ux_production_report_labor_allocations_scope_report_worker");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId, x.WorkerId })
            .HasDatabaseName("ix_production_report_labor_allocations_scope_task_worker");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasDatabaseName("ix_production_report_labor_allocations_scope_work_order");
    }
}
