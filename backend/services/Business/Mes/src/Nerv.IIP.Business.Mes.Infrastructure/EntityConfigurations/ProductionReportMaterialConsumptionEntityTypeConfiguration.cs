using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class ProductionReportMaterialConsumptionEntityTypeConfiguration : IEntityTypeConfiguration<ProductionReportMaterialConsumption>
{
    public void Configure(EntityTypeBuilder<ProductionReportMaterialConsumption> builder)
    {
        builder.ToTable("production_report_material_consumptions", tableBuilder =>
            tableBuilder.HasComment("MES material lot consumption facts referenced by production reports for work order and material traceability."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Production report material consumption id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the material consumption fact.");
        builder.Property(x => x.ReportNo).HasColumnName("report_no").IsRequired().HasMaxLength(100).HasComment("MES production report number that consumed this material lot.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work order id associated with the material consumption.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("MES operation task id associated with the material consumption.");
        builder.Property(x => x.MaterialId).HasColumnName("material_id").IsRequired().HasMaxLength(100).HasComment("Material SKU id consumed by the production report.");
        builder.Property(x => x.MaterialLotId).HasColumnName("material_lot_id").IsRequired().HasMaxLength(100).HasComment("Actual material lot id consumed by the production report.");
        builder.Property(x => x.ConsumedQuantity).HasColumnName("consumed_quantity").HasPrecision(18, 6).IsRequired().HasComment("Consumed material quantity for this lot.");
        builder.Property(x => x.MaterialIssueRequestNo).HasColumnName("material_issue_request_no").IsRequired().HasMaxLength(100).HasComment("MES material issue request number that supplied the consumed lot.");
        builder.HasOne<ProductionReport>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo })
            .HasConstraintName("fk_report_material_consumptions_reports")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.MaterialLotId })
            .HasDatabaseName("ix_report_material_consumptions_scope_lot");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasDatabaseName("ix_report_material_consumptions_scope_work_order");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReportNo, x.MaterialId, x.MaterialLotId })
            .IsUnique()
            .HasDatabaseName("ux_report_material_consumptions_report_material_lot");
    }
}
