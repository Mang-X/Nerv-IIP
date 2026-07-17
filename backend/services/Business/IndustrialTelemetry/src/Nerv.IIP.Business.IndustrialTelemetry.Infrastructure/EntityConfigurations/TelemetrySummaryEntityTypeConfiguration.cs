using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetrySummaryAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class TelemetrySummaryEntityTypeConfiguration : IEntityTypeConfiguration<TelemetrySummary>
{
    public void Configure(EntityTypeBuilder<TelemetrySummary> builder)
    {
        builder.ToTable("telemetry_summaries", table => table.HasComment("BusinessIndustrialTelemetry coarse telemetry summary buckets."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Telemetry summary identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasColumnName("organization_id").HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasColumnName("environment_id").HasComment("Owning environment identifier.");
        builder.Property(x => x.DeviceAssetId).IsRequired().HasMaxLength(150).HasColumnName("device_asset_id").HasComment("Referenced MasterData device asset identifier.");
        builder.Property(x => x.TagKey).IsRequired().HasMaxLength(150).HasColumnName("tag_key").HasComment("Telemetry tag key summarized by this bucket.");
        builder.Property(x => x.BucketStartUtc).HasColumnName("bucket_start_utc").HasComment("Inclusive UTC start of the summary bucket.");
        builder.Property(x => x.BucketEndUtc).HasColumnName("bucket_end_utc").HasComment("Exclusive UTC end of the summary bucket.");
        builder.Property(x => x.BucketEndUnixTimeMilliseconds).HasColumnName("bucket_end_unix_time_milliseconds").HasComment("Exclusive UTC bucket end represented as Unix time milliseconds for provider-neutral ordering and late-bucket checks.");
        builder.Property(x => x.SampleCount).HasColumnName("sample_count").HasComment("Number of raw samples represented by the summary.");
        builder.Property(x => x.MinValue).HasColumnName("min_value").HasPrecision(18, 6).HasComment("Minimum numeric value in the bucket.");
        builder.Property(x => x.MaxValue).HasColumnName("max_value").HasPrecision(18, 6).HasComment("Maximum numeric value in the bucket.");
        builder.Property(x => x.AverageValue).HasColumnName("average_value").HasPrecision(18, 6).HasComment("Average numeric value in the bucket.");
        builder.Property(x => x.SourceSequence).HasMaxLength(150).HasColumnName("source_sequence").HasComment("Source sequence used for idempotent summary ingestion.");
        builder.Property(x => x.SourceSystem).HasMaxLength(100).HasColumnName("source_system").HasComment("External source system that produced the telemetry summary.");
        builder.Property(x => x.SourceConnector).HasMaxLength(150).HasColumnName("source_connector").HasComment("Connector instance or adapter that delivered the telemetry summary.");
        builder.Property(x => x.CollectionConnectorId).HasMaxLength(150).HasColumnName("collection_connector_id").HasComment("Canonical collection connector identity used for manifest coverage joins when supplied.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").HasComment("UTC time when the summary was recorded.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceSystem, x.SourceConnector, x.DeviceAssetId, x.TagKey, x.SourceSequence })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.BucketStartUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.BucketEndUnixTimeMilliseconds });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CollectionConnectorId, x.DeviceAssetId, x.TagKey, x.BucketEndUtc })
            .HasDatabaseName("IX_telemetry_summaries_connector_coverage");
    }
}
