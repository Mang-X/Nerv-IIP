using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class WorkOrderEntityTypeConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders", tableBuilder =>
        {
            tableBuilder.HasComment("MES durable work orders created from business demand and ProductEngineering production version references.");
            tableBuilder.HasCheckConstraint("ck_work_orders_version_positive", "version > 0");
            tableBuilder.HasCheckConstraint(
                "ck_work_orders_rework_source",
                "(work_order_type = 'standard' AND source_work_order_id IS NULL AND source_operation_task_id IS NULL AND source_defect_no IS NULL AND source_ncr_id IS NULL AND source_ncr_code IS NULL AND source_lot_no IS NULL AND source_serial_no IS NULL) OR (work_order_type = 'rework' AND source_work_order_id IS NOT NULL AND source_defect_no IS NOT NULL AND source_ncr_id IS NOT NULL AND source_ncr_code IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.WorkOrderId);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Work order aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the work order execution context.");
        builder.Property(x => x.WorkOrderIdValue).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("Business work order id unique within organization and environment.");
        builder.Property(x => x.SkuId).HasColumnName("sku_id").IsRequired().HasMaxLength(100).HasComment("MasterData SKU public id for the item being produced.");
        builder.Property(x => x.ProductionVersionId).HasColumnName("production_version_id").HasMaxLength(100).HasComment("ProductEngineering production version public id; MES does not duplicate engineering facts.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(50).HasComment("Unit of measure copied from the source production plan when the work order is converted from DemandPlanning.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).IsRequired().HasComment("Planned production quantity.");
        builder.Property(x => x.Priority).HasColumnName("priority").IsRequired().HasComment("Scheduling priority; rush work orders use a high priority value.");
        builder.Property(x => x.DueUtc).HasColumnName("due_utc").IsRequired().HasComment("UTC due time used by the deterministic rule scheduler.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("MES work order lifecycle status.");
        builder.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1L).IsRequired().IsConcurrencyToken().HasComment("Optimistic concurrency token advanced for every work-order lifecycle or execution mutation.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the MES work order fact was created.");
        builder.Property(x => x.CompletedQuantity).HasColumnName("completed_quantity").HasPrecision(18, 6).IsRequired().HasComment("Cumulative good production quantity reported against the work order.");
        builder.Property(x => x.ScrapQuantity).HasColumnName("scrap_quantity").HasPrecision(18, 6).IsRequired().HasComment("Cumulative scrap quantity reported against the work order.");
        builder.Property(x => x.CostReportCount).HasColumnName("cost_report_count").IsRequired().HasComment("Count of MES reports expected by downstream actual-cost closure.");
        builder.Property(x => x.MaterialMovementCount).HasColumnName("material_movement_count").IsRequired().HasComment("Count of Inventory material postings expected by downstream actual-cost closure.");
        builder.Property(x => x.CapitalizedUnitCost).HasColumnName("capitalized_unit_cost").HasPrecision(18, 6).HasComment("ERP-authoritative capitalized unit cost retained so receipt creation can converge regardless of event order.");
        builder.Property(x => x.OverReceiptTolerancePercent).HasColumnName("over_receipt_tolerance_percent").HasPrecision(9, 4).IsRequired().HasComment("Allowed over-production tolerance percentage for cumulative reported quantity.");
        builder.Property(x => x.ClosedAtUtc).HasColumnName("closed_at_utc").HasComment("UTC time when the completed work order was closed.");
        builder.Property(x => x.HoldReason).HasColumnName("hold_reason").HasMaxLength(200).HasComment("Reason code or text for holding the work order.");
        builder.Property(x => x.CancelReason).HasColumnName("cancel_reason").HasMaxLength(200).HasComment("Reason code or text for cancelling the work order.");
        builder.Property(x => x.MaterialRequirementSnapshotStatus)
            .HasColumnName("material_requirement_snapshot_status")
            .HasMaxLength(30)
            .HasComment("Latest durable material requirement snapshot outcome: captured or no-requirements; null means readiness is not proven.");
        builder.Property(x => x.MaterialRequirementSnapshotEvaluatedAtUtc)
            .HasColumnName("material_requirement_snapshot_evaluated_at_utc")
            .HasComment("UTC time when MES last proved the material requirement snapshot outcome.");
        builder.Property(x => x.MaterialRequirementSnapshotProductionVersionId)
            .HasColumnName("material_requirement_snapshot_production_version_id")
            .HasMaxLength(100)
            .HasComment("Production version provenance for the frozen material requirement outcome; it normally matches the current work order version, while a released engineering-change auto-rebind retains the release version.");
        builder.Property(x => x.WorkOrderType).HasColumnName("work_order_type").IsRequired().HasMaxLength(30).HasDefaultValue(WorkOrder.StandardType).HasComment("Work order type: standard or rework.");
        builder.Property(x => x.SourceWorkOrderId).HasColumnName("source_work_order_id").HasMaxLength(100).HasComment("MES source work order business id for a rework work order.");
        builder.Property(x => x.SourceOperationTaskId).HasColumnName("source_operation_task_id").HasMaxLength(100).HasComment("Optional MES source operation task business id for a rework work order.");
        builder.Property(x => x.SourceDefectNo).HasColumnName("source_defect_no").HasMaxLength(100).HasComment("MES defect number that resolved the rework source work order and operation.");
        builder.Property(x => x.SourceNcrId).HasColumnName("source_ncr_id").HasMaxLength(100).HasComment("Quality NCR public id that requested the rework work order.");
        builder.Property(x => x.SourceNcrCode).HasColumnName("source_ncr_code").HasMaxLength(100).HasComment("Quality NCR business code retained for rework traceability.");
        builder.Property(x => x.SourceLotNo).HasColumnName("source_lot_no").HasMaxLength(150).HasComment("Optional source lot from the Quality NCR rework request.");
        builder.Property(x => x.SourceSerialNo).HasColumnName("source_serial_no").HasMaxLength(150).HasComment("Optional source serial from the Quality NCR rework request.");
        builder.OwnsOne(x => x.SourcePlanReference, source =>
        {
            source.Property(x => x.SourceSystem)
                .HasColumnName("source_system")
                .HasMaxLength(100)
                .HasComment("Owning service that produced the source production plan reference, for example DemandPlanning.");
            source.Property(x => x.SourceDocumentType)
                .HasColumnName("source_document_type")
                .HasMaxLength(100)
                .HasComment("Source document type copied from the planning service, for example PlanningSuggestion.");
            source.Property(x => x.SourceDocumentId)
                .HasColumnName("source_document_id")
                .HasMaxLength(100)
                .HasComment("Source production plan or planning suggestion public id copied into MES for durable traceability.");
            source.Property(x => x.SourceDemandReference)
                .HasColumnName("source_demand_reference")
                .HasMaxLength(100)
                .HasComment("Optional DemandPlanning demand source reference used to trace the work order back to demand.");
            source.PrimitiveCollection(x => x.SourceDemandReferences)
                .HasColumnName("source_demand_references")
                .HasComment("All DemandPlanning demand source references pegged to the source suggestion (batched suggestions peg multiple demands); includes the primary reference. Null for legacy rows, which fall back to source_demand_reference.");
            source.HasIndex(x => new { x.SourceSystem, x.SourceDocumentId })
                .HasDatabaseName("ix_work_orders_source_plan");
        });
        builder.Navigation(x => x.SourcePlanReference).IsRequired(false);
        builder.HasAlternateKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasName("ak_work_orders_scope_work_order");
        builder.HasIndex(x => x.WorkOrderIdValue).HasDatabaseName("ix_work_orders_work_order_id");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuId, x.DueUtc })
            .HasDatabaseName("ix_work_orders_scope_sku_due");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceNcrId })
            .IsUnique()
            .HasFilter("source_ncr_id IS NOT NULL")
            .HasDatabaseName("ux_work_orders_scope_source_ncr");
    }
}
