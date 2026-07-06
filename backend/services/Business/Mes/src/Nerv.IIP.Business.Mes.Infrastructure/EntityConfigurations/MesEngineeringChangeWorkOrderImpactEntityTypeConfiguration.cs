using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class MesEngineeringChangeWorkOrderImpactEntityTypeConfiguration : IEntityTypeConfiguration<MesEngineeringChangeWorkOrderImpact>
{
    public void Configure(EntityTypeBuilder<MesEngineeringChangeWorkOrderImpact> builder)
    {
        builder.ToTable("engineering_change_work_order_impacts", tableBuilder =>
            tableBuilder.HasComment("MES work-order impacts and archived production-version references detected from ProductEngineering ECO release events."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("MES engineering change impact identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the MES execution context.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES work order id affected by the ECO, or production-version marker id for archived-version guards.");
        builder.Property(x => x.SkuId).HasColumnName("sku_id").IsRequired().HasMaxLength(100).HasComment("MasterData SKU public id for the affected work order, or * for archived production-version marker rows.");
        builder.Property(x => x.WorkOrderStatusAtDetection).HasColumnName("work_order_status_at_detection").IsRequired().HasMaxLength(50).HasComment("MES work order status observed when the ECO impact was detected.");
        builder.Property(x => x.ChangeNumber).HasColumnName("change_number").IsRequired().HasMaxLength(100).HasComment("ProductEngineering ECO number that caused the MES impact.");
        builder.Property(x => x.ArchivedProductionVersionId).HasColumnName("archived_production_version_id").IsRequired().HasMaxLength(100).HasComment("ProductEngineering production version id archived by the ECO release.");
        builder.Property(x => x.SupersededByProductionVersionId).HasColumnName("superseded_by_production_version_id").HasMaxLength(100).HasComment("Successor ProductEngineering production version id when the ECO declares one.");
        builder.Property(x => x.EffectiveDate).HasColumnName("effective_date").IsRequired().HasComment("Factory business date when the ECO became effective.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("MES ECO impact status: archived-production-version, pending-decision, auto-rebound, blocked-for-manual-confirmation, or decided.");
        builder.Property(x => x.DetectedAtUtc).HasColumnName("detected_at_utc").IsRequired().HasComment("UTC time when MES consumed the ECO release and detected this impact.");
        builder.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(50).HasComment("Planner or process-engineer decision for a started affected work order.");
        builder.Property(x => x.DecidedBy).HasColumnName("decided_by").HasMaxLength(100).HasComment("User or actor id that recorded the MES ECO decision.");
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasMaxLength(500).HasComment("Human-readable basis for continuing or aborting the affected work order.");
        builder.Property(x => x.DecidedAtUtc).HasColumnName("decided_at_utc").HasComment("UTC time when the MES ECO decision was recorded.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ArchivedProductionVersionId, x.Status })
            .HasDatabaseName("ix_eco_impacts_scope_archived_pv_status");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.ChangeNumber })
            .IsUnique()
            .HasDatabaseName("ux_eco_impacts_scope_work_order_change");
    }
}
