using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;

namespace Nerv.IIP.Business.Maintenance.Infrastructure.EntityConfigurations;

public sealed class MaintenanceWorkOrderEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceWorkOrder>
{
    public void Configure(EntityTypeBuilder<MaintenanceWorkOrder> builder)
    {
        builder.ToTable("maintenance_work_orders", table => table.HasComment("Maintenance work orders, alarm references, asset availability and completion facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Maintenance work order id.");
        AddTenantColumns(builder);
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").IsRequired().HasMaxLength(150).HasComment("MasterData device asset public id or code reference.");
        builder.Property(x => x.Priority).HasColumnName("priority").IsRequired().HasMaxLength(50).HasComment("Maintenance priority.");
        builder.Property(x => x.SourceAlarmId).HasColumnName("source_alarm_id").HasMaxLength(150).HasComment("IndustrialTelemetry alarm id that opened this work order, when applicable.");
        builder.Property(x => x.SourcePlanCode).HasColumnName("source_plan_code").HasMaxLength(100).HasComment("Maintenance plan code that generated this work order, when applicable.");
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(50).HasComment("Work order source type such as alarm, plan or inspection.");
        builder.Property(x => x.SourceReferenceId).HasColumnName("source_reference_id").HasMaxLength(150).HasComment("Source fact reference id for source-type idempotency and traceability.");
        builder.Property(x => x.DiagnosticDescription).HasColumnName("diagnostic_description").HasMaxLength(1000).HasComment("Diagnostic description captured when the work order was opened from an upstream fact.");
        builder.Property(x => x.OpenedBy).HasColumnName("opened_by").IsRequired().HasMaxLength(150).HasComment("Actor or source that opened the work order.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(50).HasComment("Maintenance work order lifecycle status.");
        builder.Property(x => x.OpenedAtUtc).HasColumnName("opened_at_utc").IsRequired().HasComment("UTC time when work order was opened.");
        builder.Property(x => x.AlarmCleared).HasColumnName("alarm_cleared").IsRequired().HasComment("Whether the source IndustrialTelemetry alarm has been cleared while awaiting maintenance confirmation.");
        builder.Property(x => x.AlarmClearedAtUtc).HasColumnName("alarm_cleared_at_utc").HasComment("UTC time when the source alarm was cleared.");
        builder.Property(x => x.AssetUnavailable).HasColumnName("asset_unavailable").IsRequired().HasComment("Whether Maintenance marked the device unavailable.");
        builder.Property(x => x.AssetUnavailableReason).HasColumnName("asset_unavailable_reason").HasMaxLength(500).HasComment("Reason published with maintenance.AssetUnavailable.");
        builder.Property(x => x.AssetUnavailableFromUtc).HasColumnName("asset_unavailable_from_utc").HasComment("UTC start time of asset unavailability.");
        builder.Property(x => x.CompletionResult).HasColumnName("completion_result").HasMaxLength(1000).HasComment("Maintenance completion result.");
        builder.Property(x => x.DowntimeReasonCode).HasColumnName("downtime_reason_code").HasMaxLength(100).HasComment("Downtime reason attribution code.");
        builder.Property(x => x.DowntimeMinutes).HasColumnName("downtime_minutes").HasComment("Attributed downtime minutes.");
        builder.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").HasComment("UTC completion time.");
        builder.HasMany(x => x.SparePartLines).WithOne().HasForeignKey("MaintenanceWorkOrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.SparePartLines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceAlarmId }).IsUnique();
        // PostgreSQL treats NULL values as distinct, so manual and planned rows without source metadata do not collide.
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceType, x.SourceReferenceId })
            .IsUnique()
            .HasDatabaseName("ux_maintenance_work_orders_source_reference");
    }

    internal static void AddTenantColumns<T>(EntityTypeBuilder<T> builder)
        where T : class
    {
        builder.Property<string>("OrganizationId").HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property<string>("EnvironmentId").HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id.");
    }
}

public sealed class SparePartLineEntityTypeConfiguration : IEntityTypeConfiguration<SparePartLine>
{
    public void Configure(EntityTypeBuilder<SparePartLine> builder)
    {
        builder.ToTable("maintenance_work_order_spare_part_lines", table => table.HasComment("Spare part demand lines recorded by Maintenance; not inventory balances."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Spare part line id.");
        builder.Property<MaintenanceWorkOrderId>("MaintenanceWorkOrderId").HasColumnName("maintenance_work_order_id").IsRequired().HasComment("Owning maintenance work order id.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Referenced spare part SKU code.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Required spare part quantity.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(50).HasComment("Optional spare part unit of measure code.");
    }
}

public sealed class MaintenancePlanEntityTypeConfiguration : IEntityTypeConfiguration<MaintenancePlan>
{
    public void Configure(EntityTypeBuilder<MaintenancePlan> builder)
    {
        builder.ToTable("maintenance_plans", table => table.HasComment("Preventive maintenance plan schedule facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Maintenance plan id.");
        MaintenanceWorkOrderEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").IsRequired().HasMaxLength(150).HasComment("MasterData device asset public id or code reference.");
        builder.Property(x => x.PlanCode).HasColumnName("plan_code").IsRequired().HasMaxLength(100).HasComment("Maintenance plan code.");
        builder.Property(x => x.Interval).HasColumnName("interval").IsRequired().HasMaxLength(50).HasComment("Explicit maintenance interval expression, for example ISO-8601 P7D.");
        builder.Property(x => x.StartsOn).HasColumnName("starts_on").IsRequired().HasComment("Plan start date.");
        builder.Property(x => x.LastGeneratedOn).HasColumnName("last_generated_on").HasComment("Last business date for which the plan generated a maintenance work order.");
        builder.Property(x => x.NextDueOn).HasColumnName("next_due_on").IsRequired().HasComment("Next business date on which the preventive maintenance plan is due.");
        builder.Property(x => x.Owner).HasColumnName("owner").IsRequired().HasMaxLength(150).HasComment("Plan owner or team.");
        builder.Property(x => x.WindowStartUtc).HasColumnName("window_start_utc").HasComment("UTC start of the optional runtime availability maintenance window.");
        builder.Property(x => x.WindowEndUtc).HasColumnName("window_end_utc").HasComment("UTC end of the optional runtime availability maintenance window.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC creation time.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PlanCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.WindowStartUtc, x.WindowEndUtc });
    }
}

public sealed class MaintenanceInspectionEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceInspection>
{
    public void Configure(EntityTypeBuilder<MaintenanceInspection> builder)
    {
        builder.ToTable("maintenance_inspections", table => table.HasComment("Maintenance inspection facts linked to a plan or work order."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Maintenance inspection id.");
        MaintenanceWorkOrderEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.PlanId).HasColumnName("maintenance_plan_id").HasComment("Referenced maintenance plan id, if applicable.");
        builder.Property(x => x.WorkOrderId).HasColumnName("maintenance_work_order_id").HasComment("Referenced maintenance work order id, if applicable.");
        builder.Property(x => x.Inspector).HasColumnName("inspector").IsRequired().HasMaxLength(150).HasComment("Inspector actor.");
        builder.Property(x => x.Result).HasColumnName("result").IsRequired().HasMaxLength(1000).HasComment("Inspection result.");
        builder.Property(x => x.InspectedAtUtc).HasColumnName("inspected_at_utc").IsRequired().HasComment("UTC inspection time.");
    }
}

public sealed class DowntimeReasonEntityTypeConfiguration : IEntityTypeConfiguration<DowntimeReason>
{
    public void Configure(EntityTypeBuilder<DowntimeReason> builder)
    {
        builder.ToTable("downtime_reasons", table => table.HasComment("Maintenance downtime reason reference facts owned by Maintenance."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Downtime reason id.");
        MaintenanceWorkOrderEntityTypeConfiguration.AddTenantColumns(builder);
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").IsRequired().HasMaxLength(100).HasComment("Downtime reason code.");
        builder.Property(x => x.Description).HasColumnName("description").IsRequired().HasMaxLength(500).HasComment("Downtime reason description.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReasonCode }).IsUnique();
    }
}
