using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class TelemetryProductionReportCandidateEntityTypeConfiguration : IEntityTypeConfiguration<TelemetryProductionReportCandidate>
{
    public void Configure(EntityTypeBuilder<TelemetryProductionReportCandidate> builder)
    {
        builder.ToTable("telemetry_production_report_candidates", tableBuilder =>
            tableBuilder.HasComment("MES telemetry count deltas awaiting manual confirmation or retained as configured report drafts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Telemetry production report candidate aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the candidate.");
        builder.Property(x => x.SourceIdempotencyKey).HasColumnName("source_idempotency_key").IsRequired().HasMaxLength(512).HasComment("IndustrialTelemetry event idempotency key; unique candidate source boundary.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").IsRequired().HasMaxLength(150).HasComment("Device asset that produced the counter delta.");
        builder.Property(x => x.TagKey).HasColumnName("tag_key").IsRequired().HasMaxLength(150).HasComment("Production-count telemetry tag key.");
        builder.Property(x => x.ReportingMode).HasColumnName("reporting_mode").IsRequired().HasMaxLength(20).HasComment("Configured telemetry report mode: posted or draft.");
        builder.Property(x => x.GoodQuantity).HasColumnName("good_quantity").IsRequired().HasPrecision(18, 6).HasComment("Positive good-quantity delta derived from the monotonic telemetry counter.");
        builder.Property(x => x.BucketStartUtc).HasColumnName("bucket_start_utc").IsRequired().HasComment("Inclusive UTC telemetry counter bucket start.");
        builder.Property(x => x.BucketEndUtc).HasColumnName("bucket_end_utc").IsRequired().HasComment("Exclusive UTC telemetry counter bucket end.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").HasMaxLength(100).HasComment("MES-local mapped work center when available.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").HasMaxLength(100).HasComment("Current MES work order resolved for the counter delta when unambiguous.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").HasMaxLength(100).HasComment("Current MES operation task resolved for the counter delta when unambiguous.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Candidate status: draft or pending-confirmation.");
        builder.Property(x => x.SuspensionReason).HasColumnName("suspension_reason").HasMaxLength(100).HasComment("Reason direct posting was suspended, such as active-alarm or no-current-work-order.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when MES received the telemetry count delta.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceIdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_telemetry_report_candidates_scope_source");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status, x.CreatedAtUtc })
            .HasDatabaseName("ix_telemetry_report_candidates_scope_status_created");
    }
}
