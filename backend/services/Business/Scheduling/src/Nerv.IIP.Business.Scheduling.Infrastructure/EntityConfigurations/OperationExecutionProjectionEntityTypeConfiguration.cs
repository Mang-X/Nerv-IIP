using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.OperationExecutionProjectionAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class OperationExecutionProjectionEntityTypeConfiguration
    : IEntityTypeConfiguration<OperationExecutionProjection>
{
    public void Configure(EntityTypeBuilder<OperationExecutionProjection> builder)
    {
        builder.ToTable(
            "operation_execution_projections",
            table => table.HasComment("Latest MES and quality execution facts projected at operation-task grain."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Projection row id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Business environment id.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").HasMaxLength(128).IsRequired().HasComment("MES work-order public id.");
        builder.Property(x => x.OperationId).HasColumnName("operation_id").HasMaxLength(128).IsRequired().HasComment("MES operation-task public id.");
        builder.Property(x => x.OperationSequence).HasColumnName("operation_sequence").HasComment("Optional operation sequence supplied by MES lifecycle facts.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").HasMaxLength(128).HasComment("Optional work center supplied by MES execution facts.");
        builder.Property(x => x.ActualStartedAtUtc).HasColumnName("actual_started_at_utc").HasComment("Earliest observed operation start timestamp in UTC.");
        builder.Property(x => x.ActualCompletedAtUtc).HasColumnName("actual_completed_at_utc").HasComment("Latest accepted operation completion timestamp in UTC.");
        builder.Property(x => x.IsPaused).HasColumnName("is_paused").IsRequired().HasComment("Whether the latest lifecycle fact leaves the operation paused.");
        builder.Property(x => x.CompletedQuantity).HasColumnName("completed_quantity").HasPrecision(18, 6).IsRequired().HasComment("Net reported good quantity, including negative reversal deltas.");
        builder.Property(x => x.IsDowntimeBlocked).HasColumnName("is_downtime_blocked").IsRequired().HasComment("Whether any operation-scoped downtime fact is active.");
        builder.Property(x => x.IsQualityBlocked).HasColumnName("is_quality_blocked").IsRequired().HasComment("Whether the latest operation-scoped quality result blocks execution.");
        builder.Property(x => x.LifecycleOccurredAtUtc).HasColumnName("lifecycle_occurred_at_utc").HasComment("Ordering watermark for lifecycle state facts in UTC.");
        builder.Property(x => x.LifecycleEventId).HasColumnName("lifecycle_event_id").HasMaxLength(128).HasComment("Integration event id that supplied the current lifecycle state.");
        builder.Property(x => x.DowntimeOccurredAtUtc).HasColumnName("downtime_occurred_at_utc").HasComment("Ordering watermark for downtime state facts in UTC.");
        builder.Property(x => x.DowntimeEventId).HasColumnName("downtime_event_id").HasMaxLength(128).HasComment("Integration event id that supplied the current downtime state.");
        builder.Property(x => x.QualityOccurredAtUtc).HasColumnName("quality_occurred_at_utc").HasComment("Ordering watermark for quality state facts in UTC.");
        builder.Property(x => x.QualityEventId).HasColumnName("quality_event_id").HasMaxLength(128).HasComment("Integration event id that supplied the current quality state.");
        builder.Property(x => x.LatestSourceOccurredAtUtc).HasColumnName("latest_source_occurred_at_utc").IsRequired().HasComment("Latest accepted source-fact timestamp across all execution axes in UTC.");
        builder.Property(x => x.LatestSourceEventId).HasColumnName("latest_source_event_id").HasMaxLength(128).IsRequired().HasComment("Integration event id for the latest accepted source fact.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId, x.OperationId }).IsUnique();
        builder.HasMany(x => x.DowntimeStates)
            .WithOne()
            .HasForeignKey(x => x.OperationExecutionProjectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
