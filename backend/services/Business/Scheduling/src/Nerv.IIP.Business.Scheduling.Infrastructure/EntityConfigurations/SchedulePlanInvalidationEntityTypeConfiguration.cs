using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class SchedulePlanInvalidationEntityTypeConfiguration : IEntityTypeConfiguration<SchedulePlanInvalidation>
{
    public void Configure(EntityTypeBuilder<SchedulePlanInvalidation> builder)
    {
        builder.ToTable("schedule_plan_invalidations", table => table.HasComment("Event-driven Scheduling plan invalidation projection for APS replan decisions."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Schedule plan invalidation row id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Business environment id.");
        builder.Property(x => x.PlanId).HasColumnName("plan_id").HasMaxLength(96).IsRequired().HasComment("Generated schedule plan invalidated by the upstream event.");
        builder.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(256).IsRequired().HasComment("Source integration event identifier.");
        builder.Property(x => x.SourceEventType).HasColumnName("source_event_type").HasMaxLength(256).IsRequired().HasComment("Source integration event type.");
        builder.Property(x => x.SourceService).HasColumnName("source_service").HasMaxLength(128).IsRequired().HasComment("Service that produced the source event.");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(64).IsRequired().HasComment("Scheduling invalidation reason code.");
        builder.Property(x => x.AffectedResourceId).HasColumnName("affected_resource_id").HasMaxLength(96).HasComment("Affected resource or device asset id when the event targets equipment.");
        builder.Property(x => x.AffectedWorkOrderId).HasColumnName("affected_work_order_id").HasMaxLength(96).HasComment("Affected work order id when the event targets a work order.");
        builder.Property(x => x.AffectedOperationId).HasColumnName("affected_operation_id").HasMaxLength(96).HasComment("Affected operation id when available.");
        builder.Property(x => x.AffectedSkuCode).HasColumnName("affected_sku_code").HasMaxLength(96).HasComment("Affected SKU code when the event changes material readiness.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").HasComment("UTC time when the source event occurred.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").HasComment("UTC time when BusinessScheduling recorded the invalidation.");

        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PlanId, x.SourceEventType, x.SourceEventId })
            .IsUnique()
            .HasDatabaseName("ux_schedule_plan_invalidations_source_event");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.PlanId, x.RecordedAtUtc })
            .HasDatabaseName("ix_schedule_plan_invalidations_plan_recorded_at");
    }
}
