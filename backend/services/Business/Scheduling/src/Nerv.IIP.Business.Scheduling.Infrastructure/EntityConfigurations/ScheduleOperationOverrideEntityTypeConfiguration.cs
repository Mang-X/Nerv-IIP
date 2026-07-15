using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class ScheduleOperationOverrideEntityTypeConfiguration : IEntityTypeConfiguration<ScheduleOperationOverride>
{
    public void Configure(EntityTypeBuilder<ScheduleOperationOverride> builder)
    {
        builder.ToTable("schedule_operation_overrides", table => table.HasComment("Current fixed operation assignments created by manual Scheduling adjustments or MES dispatch."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Override row id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Business environment id.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").HasMaxLength(128).IsRequired().HasComment("Real work-order public id.");
        builder.Property(x => x.OperationId).HasColumnName("operation_id").HasMaxLength(128).IsRequired().HasComment("Real operation public id.");
        builder.Property(x => x.OperationSequence).HasColumnName("operation_sequence").HasComment("Operation sequence within the work order.");
        builder.Property(x => x.ResourceId).HasColumnName("resource_id").HasMaxLength(128).IsRequired().HasComment("Fixed executable resource id.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").HasMaxLength(128).IsRequired().HasComment("Work center owning the fixed resource.");
        builder.Property(x => x.StartUtc).HasColumnName("start_utc").HasComment("Fixed start timestamp in UTC.");
        builder.Property(x => x.EndUtc).HasColumnName("end_utc").HasComment("Fixed end timestamp in UTC.");
        builder.Property(x => x.LockReasonCode).HasColumnName("lock_reason_code").HasMaxLength(64).IsRequired().HasComment("Explainable lock reason code.");
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired().HasComment("Scheduling API or MES dispatch source type.");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(128).HasComment("Optional source integration event id.");
        builder.Property(x => x.Actor).HasColumnName("actor").HasMaxLength(128).IsRequired().HasComment("Actor that created the current fact.");
        builder.Property(x => x.SourceOccurredAtUtc).HasColumnName("source_occurred_at_utc").IsConcurrencyToken().HasComment("Source ordering timestamp and optimistic concurrency token used to reject stale updates.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasComment("Last persistence update timestamp in UTC.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationId }).IsUnique();
    }
}
