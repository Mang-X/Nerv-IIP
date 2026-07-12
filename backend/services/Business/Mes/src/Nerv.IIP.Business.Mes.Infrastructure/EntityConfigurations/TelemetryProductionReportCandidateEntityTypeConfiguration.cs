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
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).IsConcurrencyToken().HasComment("Candidate lifecycle status: draft, pending-confirmation, confirmed, or dismissed; used as an optimistic concurrency predicate.");
        builder.Property(x => x.SuspensionReason).HasColumnName("suspension_reason").HasMaxLength(100).HasComment("Reason direct posting was suspended, such as active-alarm or no-current-work-order.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when MES received the telemetry count delta.");
        builder.Property(x => x.ResolutionReason).HasColumnName("resolution_reason").HasMaxLength(500).HasComment("Operator supplied dismissal reason when dismissed.");
        builder.Property(x => x.ResolvedBy).HasColumnName("resolved_by").HasMaxLength(200).HasComment("Authenticated actor that confirmed or dismissed the candidate.");
        builder.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc").HasComment("UTC time when the candidate reached a terminal state.");
        builder.Property(x => x.ProductionReportId).HasColumnName("production_report_id").HasMaxLength(100).HasComment("Production report aggregate id created by confirmation.");
        builder.HasMany(x => x.Transitions).WithOne().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceIdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_telemetry_report_candidates_scope_source");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status, x.CreatedAtUtc })
            .HasDatabaseName("ix_telemetry_report_candidates_scope_status_created");
    }
}

public sealed class TelemetryProductionReportCandidateTransitionEntityTypeConfiguration : IEntityTypeConfiguration<TelemetryProductionReportCandidateTransition>
{
    public void Configure(EntityTypeBuilder<TelemetryProductionReportCandidateTransition> builder)
    {
        builder.ToTable("telemetry_production_report_candidate_transitions", t => t.HasComment("Immutable audit history for telemetry report candidate lifecycle transitions."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Transition id.");
        builder.Property(x => x.CandidateId).HasColumnName("candidate_id").IsRequired().HasComment("Owning telemetry candidate id.");
        builder.Property(x => x.FromStatus).HasColumnName("from_status").IsRequired().HasMaxLength(50).HasComment("Status before the transition.");
        builder.Property(x => x.ToStatus).HasColumnName("to_status").IsRequired().HasMaxLength(50).HasComment("Status after the transition.");
        builder.Property(x => x.Actor).HasColumnName("actor").IsRequired().HasMaxLength(200).HasComment("Authenticated transition actor.");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).HasComment("Optional human disposition reason.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired().HasComment("UTC transition time.");
        builder.HasIndex(x => new { x.CandidateId, x.OccurredAtUtc }).HasDatabaseName("ix_telemetry_candidate_transitions_candidate_time");
    }
}
