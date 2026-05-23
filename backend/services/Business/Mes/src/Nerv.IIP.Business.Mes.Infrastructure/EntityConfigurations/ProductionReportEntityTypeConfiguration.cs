using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class ProductionReportEntityTypeConfiguration : IEntityTypeConfiguration<ProductionReport>
{
    public void Configure(EntityTypeBuilder<ProductionReport> builder)
    {
        builder.ToTable("production_reports", tableBuilder =>
            tableBuilder.HasComment("MES production report facts recording good and scrap quantities for operation execution."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Production report aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the production report.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES business work order id reported against.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation task id reported against.");
        builder.Property(x => x.GoodQuantity).HasColumnName("good_quantity").HasPrecision(18, 6).IsRequired().HasComment("Good quantity reported for the operation.");
        builder.Property(x => x.ScrapQuantity).HasColumnName("scrap_quantity").HasPrecision(18, 6).IsRequired().HasComment("Scrap quantity reported for the operation.");
        builder.Property(x => x.CompletesOperation).HasColumnName("completes_operation").IsRequired().HasComment("Whether this report marks the operation as completed.");
        builder.Property(x => x.ReportedAtUtc).HasColumnName("reported_at_utc").IsRequired().HasComment("UTC time when production was reported.");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasConstraintName("fk_production_reports_work_orders")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OperationTask>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId })
            .HasConstraintName("fk_production_reports_operation_tasks")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.WorkOrderId).HasDatabaseName("ix_production_reports_work_order_id");
        builder.HasIndex(x => x.OperationTaskId).HasDatabaseName("ix_production_reports_operation_task_id");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasDatabaseName("ix_production_reports_scope_work_order_fk");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId })
            .HasDatabaseName("ix_production_reports_scope_operation_fk");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.OperationTaskId, x.ReportedAtUtc })
            .HasDatabaseName("ix_production_reports_scope_operation_time");
    }
}
