using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

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
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the operation task fact was created.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskIdValue }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.OperationSequence });
    }
}
