using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;

namespace Nerv.IIP.Business.Quality.Infrastructure.EntityConfigurations;

public sealed class PeriodicInspectionOperationEntityTypeConfiguration
    : IEntityTypeConfiguration<PeriodicInspectionOperation>
{
    public void Configure(EntityTypeBuilder<PeriodicInspectionOperation> builder)
    {
        builder.ToTable(
            "periodic_inspection_operations",
            table =>
            {
                table.HasComment("Quality-owned MES operation source facts staged for periodic inspection reconciliation.");
                table.HasCheckConstraint(
                    "ck_periodic_inspection_operations_release_snapshot",
                    "(sku_code IS NULL AND operation_sequence IS NULL AND work_center_id IS NULL AND released_at_utc IS NULL) OR "
                    + "(sku_code IS NOT NULL AND operation_sequence > 0 AND work_center_id IS NOT NULL AND released_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_periodic_inspection_operations_completion_snapshot",
                    "(completion_sku_code IS NULL AND completion_operation_sequence IS NULL AND completion_work_center_id IS NULL AND completion_uom_code IS NULL AND completed_at_utc IS NULL) OR "
                    + "(completion_sku_code IS NOT NULL AND completion_operation_sequence > 0 AND completion_work_center_id IS NOT NULL AND completion_uom_code IS NOT NULL AND completed_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_periodic_inspection_operations_completion_time",
                    "completed_at_utc IS NULL OR released_at_utc IS NULL OR completed_at_utc >= released_at_utc");
            });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Periodic inspection operation aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the operation facts.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the operation facts apply.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(150).HasComment("MES work order public id.");
        builder.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired().HasMaxLength(150).HasComment("MES operation task public id.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").HasMaxLength(100).HasComment("SKU snapshot from the work-order release event; null until release arrives.");
        builder.Property(x => x.OperationSequence).HasColumnName("operation_sequence").HasComment("Positive operation sequence from the work-order release event; null until release arrives.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").HasMaxLength(150).HasComment("Work center snapshot from the work-order release event; null until release arrives.");
        builder.Property(x => x.ReleasedAtUtc).HasColumnName("released_at_utc").HasComment("UTC time when MES released the work order; null while source facts are staged out of order.");
        builder.Property(x => x.CompletionSkuCode).HasColumnName("completion_sku_code").HasMaxLength(100).HasComment("SKU snapshot staged from an operation completion event.");
        builder.Property(x => x.CompletionOperationSequence).HasColumnName("completion_operation_sequence").HasComment("Positive operation sequence staged from an operation completion event.");
        builder.Property(x => x.CompletionWorkCenterId).HasColumnName("completion_work_center_id").HasMaxLength(150).HasComment("Work center snapshot staged from an operation completion event.");
        builder.Property(x => x.CompletionUomCode).HasColumnName("completion_uom_code").HasMaxLength(50).HasComment("Quantity UOM snapshot staged from an operation completion event.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC time when MES completed the operation; may precede the release event in delivery order.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.OperationId })
            .IsUnique()
            .HasDatabaseName("ux_periodic_inspection_operations_scope_operation");
        builder.HasMany(x => x.ProductionReports)
            .WithOne()
            .HasForeignKey(x => x.OperationContextId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.RuntimeContexts)
            .WithOne()
            .HasForeignKey(x => x.OperationContextId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PeriodicInspectionProductionReportEntityTypeConfiguration
    : IEntityTypeConfiguration<PeriodicInspectionProductionReport>
{
    public void Configure(EntityTypeBuilder<PeriodicInspectionProductionReport> builder)
    {
        builder.ToTable(
            "periodic_inspection_production_reports",
            table =>
            {
                table.HasComment("Immutable MES production-report facts used to reconcile periodic inspection watermarks.");
                table.HasCheckConstraint(
                    "ck_periodic_inspection_reports_reversal",
                    "(is_reversal AND good_quantity <= 0 AND reversed_report_no IS NOT NULL) OR "
                    + "(NOT is_reversal AND good_quantity >= 0 AND reversed_report_no IS NULL)");
            });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Production-report fact id.");
        builder.Property(x => x.OperationContextId).HasColumnName("operation_context_id").IsRequired().HasComment("Owning periodic inspection operation aggregate id.");
        builder.Property(x => x.ReportNo).HasColumnName("report_no").IsRequired().HasMaxLength(150).HasComment("MES production report business identity.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(150).HasComment("MES work center snapshot carried by the production report.");
        builder.Property(x => x.GoodQuantity).HasColumnName("good_quantity").IsRequired().HasPrecision(18, 6).HasComment("Signed good quantity from MES; reversals carry a non-positive value in the reported UOM.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(50).HasComment("MES-reported quantity unit of measure; all reports for one operation must match.");
        builder.Property(x => x.ReportedAtUtc).HasColumnName("reported_at_utc").IsRequired().HasComment("UTC business time recorded by MES.");
        builder.Property(x => x.IsReversal).HasColumnName("is_reversal").IsRequired().HasComment("Whether this fact reverses an earlier report.");
        builder.Property(x => x.ReversedReportNo).HasColumnName("reversed_report_no").HasMaxLength(150).HasComment("Original MES report number referenced by a reversal.");
        builder.HasIndex(x => new { x.OperationContextId, x.ReportNo })
            .IsUnique()
            .HasDatabaseName("ux_periodic_inspection_reports_operation_report");
    }
}

public sealed class PeriodicInspectionRuntimeContextEntityTypeConfiguration
    : IEntityTypeConfiguration<PeriodicInspectionRuntimeContext>
{
    public void Configure(EntityTypeBuilder<PeriodicInspectionRuntimeContext> builder)
    {
        builder.ToTable(
            "periodic_inspection_runtime_contexts",
            table =>
            {
                table.HasComment("Frozen per-plan periodic inspection runtime contexts with quantity/time watermarks and periodic task generation state.");
                table.HasCheckConstraint(
                    "ck_periodic_inspection_runtime_interval",
                    "(time_interval_hours IS NOT NULL AND time_interval_hours > 0) OR (quantity_interval IS NOT NULL AND quantity_interval > 0)");
                table.HasCheckConstraint(
                    "ck_periodic_inspection_runtime_assignment",
                    "assigned_inspector_user_id IS NULL OR assigned_team_id IS NULL");
                table.HasCheckConstraint(
                    "ck_periodic_inspection_runtime_status",
                    "(status = 'active' AND completed_at_utc IS NULL) OR (status = 'closed' AND completed_at_utc IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_periodic_inspection_runtime_high_water",
                    "quantity_high_water >= 0");
                table.HasCheckConstraint(
                    "ck_periodic_inspection_runtime_time_watermark",
                    "(last_generated_time_window_sequence = 0 AND time_schedule_anchor_at_utc IS NULL) OR "
                    + "(last_generated_time_window_sequence > 0 AND time_schedule_anchor_at_utc IS NOT NULL)");
            });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Periodic inspection runtime context id.");
        builder.Property(x => x.OperationContextId).HasColumnName("operation_context_id").IsRequired().HasComment("Owning periodic inspection operation aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id frozen at context creation.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id frozen at context creation.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(150).HasComment("MES work order public id frozen at context creation.");
        builder.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired().HasMaxLength(150).HasComment("MES operation task public id frozen at context creation.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("SKU snapshot from the release event.");
        builder.Property(x => x.OperationSequence).HasColumnName("operation_sequence").IsRequired().HasComment("Positive MES operation sequence snapshot.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(150).HasComment("Work center snapshot from the release event.");
        builder.Property(x => x.ReleasedAtUtc).HasColumnName("released_at_utc").IsRequired().HasComment("UTC work-order release time.");
        builder.Property(x => x.InspectionPlanId).HasColumnName("inspection_plan_id").IsRequired().HasComment("Immutable matched inspection plan id.");
        builder.Property(x => x.InspectionPlanVersion).HasColumnName("inspection_plan_version").IsRequired().HasComment("Immutable matched inspection plan version.");
        builder.Property(x => x.TimeIntervalHours).HasColumnName("time_interval_hours").HasPrecision(18, 6).HasComment("Frozen periodic time interval in hours.");
        builder.Property(x => x.QuantityInterval).HasColumnName("quantity_interval").HasPrecision(18, 6).HasComment("Frozen periodic quantity interval in the report UOM.");
        builder.Property(x => x.AssignedInspectorUserId).HasColumnName("assigned_inspector_user_id").HasMaxLength(150).HasComment("Frozen optional inspector assignment target.");
        builder.Property(x => x.AssignedTeamId).HasColumnName("assigned_team_id").HasMaxLength(150).HasComment("Frozen optional team assignment target.");
        builder.Property(x => x.FirstActivityAtUtc).HasColumnName("first_activity_at_utc").HasComment("Earliest UTC MES production activity time observed for the operation.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(50).HasComment("Authoritative MES production report UOM; null before the first report.");
        builder.Property(x => x.CumulativeGoodQuantity).HasColumnName("cumulative_good_quantity").IsRequired().HasPrecision(18, 6).HasComment("Current signed net good quantity including reversal effects.");
        builder.Property(x => x.QuantityHighWater).HasColumnName("quantity_high_water").IsRequired().HasPrecision(18, 6).HasComment("Monotonic accepted good-quantity high water; reversal facts neither advance nor roll it back.");
        builder.Property(x => x.TimeScheduleAnchorAtUtc).HasColumnName("time_schedule_anchor_at_utc").HasComment("Frozen UTC first-production anchor after the first time window is generated.");
        builder.Property(x => x.LastGeneratedTimeWindowSequence).HasColumnName("last_generated_time_window_sequence").IsRequired().HasComment("Last atomically generated time-window sequence; zero before generation.");
        builder.Property(x => x.NextTimeWindowAtUtc).HasColumnName("next_time_window_at_utc").HasComment("Persisted UTC due time for the next ungenerated time window; null before activity or after closure.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Runtime context status: active or closed.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC MES operation completion time when status is closed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.InspectionPlanId, x.WorkOrderId, x.OperationId })
            .IsUnique()
            .HasDatabaseName("ux_periodic_inspection_runtime_scope_plan_operation");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status, x.NextTimeWindowAtUtc })
            .HasDatabaseName("ix_periodic_inspection_runtime_scope_status_next_time");
    }
}
