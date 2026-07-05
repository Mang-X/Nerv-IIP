using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class OperationTaskEntityTypeConfiguration : IEntityTypeConfiguration<OperationTask>
{
    public void Configure(EntityTypeBuilder<OperationTask> builder)
    {
        builder.ToTable("operation_tasks", tableBuilder =>
            tableBuilder.HasComment("MES operation task facts created from routing step snapshots for scheduling and execution tracking."));
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.OperationTaskId);
        builder.Ignore(x => x.Duration);
        builder.Ignore(x => x.PausedDuration);
        builder.Ignore(x => x.LaborTime);
        builder.Ignore(x => x.MachineTime);
        builder.Ignore(x => x.AlternativeWorkCenterIdList);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Operation task aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the operation execution context.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("MES business work order id this operation belongs to.");
        builder.Property(x => x.OperationTaskIdValue).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("Business operation task id unique within organization and environment.");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired().HasMaxLength(30).HasComment("Operation lifecycle status used by the scheduler.");
        builder.Property(x => x.OperationSequence).HasColumnName("operation_sequence").IsRequired().HasComment("Routing operation sequence used for deterministic scheduling order.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("Primary MasterData work center public id.");
        builder.Property(x => x.AlternativeWorkCenterIds).HasColumnName("alternative_work_center_ids").HasMaxLength(1000).HasComment("Pipe-delimited alternate work center public ids copied from routing snapshot.");
        builder.Property(x => x.EarliestStartUtc).HasColumnName("earliest_start_utc").IsRequired().HasComment("Earliest UTC start time allowed for this operation.");
        builder.Property(x => x.DurationTicks).HasColumnName("duration_ticks").IsRequired().HasComment("Operation duration stored as .NET ticks for deterministic scheduler reconstruction.");
        builder.Property(x => x.ExistingStartUtc).HasColumnName("existing_start_utc").HasComment("Existing UTC start time for in-progress operation preservation.");
        builder.Property(x => x.ExistingEndUtc).HasColumnName("existing_end_utc").HasComment("Existing UTC end time for in-progress operation preservation.");
        builder.Property(x => x.PausedAtUtc).HasColumnName("paused_at_utc").HasComment("UTC time when the operation entered its current paused interval, if any.");
        builder.Property(x => x.PausedDurationTicks).HasColumnName("paused_duration_ticks").IsRequired().HasDefaultValue(0L).HasComment("Accumulated paused duration stored as .NET ticks and excluded from actual work time.");
        builder.Property(x => x.LaborTimeTicks).HasColumnName("labor_time_ticks").IsRequired().HasDefaultValue(0L).HasComment("Actual labor time stored as .NET ticks after paused duration deduction.");
        builder.Property(x => x.MachineTimeTicks).HasColumnName("machine_time_ticks").IsRequired().HasDefaultValue(0L).HasComment("Actual machine time stored as .NET ticks after paused duration deduction.");
        builder.Property(x => x.AssignedUserId).HasColumnName("assigned_user_id").HasMaxLength(100).HasComment("Assigned operator or person public id captured by MES dispatch.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").HasMaxLength(100).HasComment("Assigned MasterData device asset public id captured by MES dispatch.");
        builder.Property(x => x.ShiftId).HasColumnName("shift_id").HasMaxLength(100).HasComment("Assigned MasterData shift public id captured by MES dispatch.");
        builder.Property(x => x.AssignedAtUtc).HasColumnName("assigned_at_utc").HasComment("UTC time when MES dispatch assignment facts were captured.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the operation task fact was created.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Produced SKU code copied from the MES work order for downstream inspection triggers.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").IsRequired().HasMaxLength(30).HasComment("Produced quantity unit of measure for downstream inspection triggers.");
        builder.Property(x => x.PlannedQuantity).HasColumnName("planned_quantity").HasPrecision(18, 6).IsRequired().HasComment("Planned operation quantity used as the default good quantity for operation completion inspection triggers.");
        builder.Property(x => x.RequiresQualityInspection).HasColumnName("requires_quality_inspection").IsRequired().HasComment("Whether this operation completion should trigger a Quality inspection task.");
        builder.HasAlternateKey(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskIdValue })
            .HasName("ak_operation_tasks_scope_task");
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasPrincipalKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderIdValue })
            .HasForeignKey(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasConstraintName("fk_operation_tasks_work_orders")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.WorkOrderId).HasDatabaseName("ix_operation_tasks_work_order_id");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
            .HasDatabaseName("ix_operation_tasks_scope_work_order_fk");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.OperationSequence })
            .HasDatabaseName("ix_operation_tasks_scope_work_order_seq");
    }
}
