using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class MaterialRequirementEntityTypeConfiguration : IEntityTypeConfiguration<MaterialRequirement>
{
    public void Configure(EntityTypeBuilder<MaterialRequirement> builder)
    {
        builder.ToTable("material_requirements", tableBuilder =>
            tableBuilder.HasComment("MES material requirement snapshots captured from released MBOM, Inventory and WMS readiness facts for work order execution."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Material requirement snapshot id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the material readiness context.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work order id this material requirement belongs to.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").HasMaxLength(100).HasComment("Optional MES operation task id this requirement belongs to.");
        builder.Property(x => x.MaterialId).HasColumnName("material_id").IsRequired().HasMaxLength(100).HasComment("MasterData material SKU id required by the work order or operation.");
        builder.Property(x => x.MaterialLotId).HasColumnName("material_lot_id").HasMaxLength(100).HasComment("Optional preferred or allocated material lot id from Inventory/WMS readiness.");
        builder.Property(x => x.RequiredQuantity).HasColumnName("required_quantity").HasPrecision(18, 6).IsRequired().HasComment("Required material quantity from released MBOM or operation demand snapshot.");
        builder.Property(x => x.AvailableQuantity).HasColumnName("available_quantity").HasPrecision(18, 6).IsRequired().HasComment("Available Inventory quantity snapshot for this requirement.");
        builder.Property(x => x.StagedQuantity).HasColumnName("staged_quantity").HasPrecision(18, 6).IsRequired().HasComment("WMS staged quantity snapshot for this requirement.");
        builder.Property(x => x.SourceSystem).HasColumnName("source_system").IsRequired().HasMaxLength(100).HasComment("Owning source system that produced the material readiness snapshot.");
        builder.Property(x => x.SourceSnapshotId).HasColumnName("source_snapshot_id").IsRequired().HasMaxLength(100).HasComment("Source snapshot id or version used to trace the readiness calculation.");
        builder.Property(x => x.CapturedAtUtc).HasColumnName("captured_at_utc").IsRequired().HasComment("UTC time when MES captured this material requirement readiness snapshot.");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasConstraintName("fk_material_requirements_work_orders")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.MaterialId, x.MaterialLotId })
            .HasDatabaseName("ix_material_requirements_scope_work_order_material");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId })
            .HasDatabaseName("ix_material_requirements_scope_operation");
    }
}
