using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRawSampleAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class TelemetryRawSampleEntityTypeConfiguration : IEntityTypeConfiguration<TelemetryRawSample>
{
    public void Configure(EntityTypeBuilder<TelemetryRawSample> builder)
    {
        builder.ToTable("telemetry_raw_samples", table => table.HasComment("BusinessIndustrialTelemetry raw historian ingest bucket details."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Raw historian sample identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasColumnName("organization_id").HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasColumnName("environment_id").HasComment("Owning environment identifier.");
        builder.Property(x => x.DeviceAssetId).IsRequired().HasMaxLength(150).HasColumnName("device_asset_id").HasComment("Referenced MasterData device asset identifier.");
        builder.Property(x => x.TagKey).IsRequired().HasMaxLength(150).HasColumnName("tag_key").HasComment("Telemetry tag key represented by this raw historian bucket.");
        builder.Property(x => x.BucketStartUtc).HasColumnName("bucket_start_utc").HasComment("Inclusive UTC start of the raw historian bucket.");
        builder.Property(x => x.BucketEndUtc).HasColumnName("bucket_end_utc").HasComment("Exclusive UTC end of the raw historian bucket.");
        builder.Property(x => x.BucketEndUnixTimeMilliseconds).HasColumnName("bucket_end_unix_time_milliseconds").HasComment("Exclusive UTC bucket end represented as Unix time milliseconds for provider-neutral retention scans.");
        builder.Property(x => x.SampleCount).HasColumnName("sample_count").HasComment("Number of collector samples represented by the raw historian bucket.");
        builder.Property(x => x.MinValue).HasColumnName("min_value").HasPrecision(18, 6).HasComment("Minimum numeric value in the raw historian bucket.");
        builder.Property(x => x.MaxValue).HasColumnName("max_value").HasPrecision(18, 6).HasComment("Maximum numeric value in the raw historian bucket.");
        builder.Property(x => x.AverageValue).HasColumnName("average_value").HasPrecision(18, 6).HasComment("Weighted average input value for historian downsampling.");
        builder.Property(x => x.FirstValue).HasColumnName("first_value").HasPrecision(18, 6).HasComment("First observed numeric value in the raw historian bucket.");
        builder.Property(x => x.LastValue).HasColumnName("last_value").HasPrecision(18, 6).HasComment("Last observed numeric value in the raw historian bucket.");
        builder.Property(x => x.SourceSequence).IsRequired().HasMaxLength(150).HasColumnName("source_sequence").HasComment("Source sequence used for idempotent raw historian ingestion.");
        builder.Property(x => x.SourceSystem).HasMaxLength(100).HasColumnName("source_system").HasComment("External source system that produced the raw historian bucket.");
        builder.Property(x => x.SourceConnector).HasMaxLength(150).HasColumnName("source_connector").HasComment("Connector instance or adapter that delivered the raw historian bucket.");
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc").HasComment("UTC time when the raw historian bucket was recorded.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceSystem, x.SourceConnector, x.DeviceAssetId, x.TagKey, x.SourceSequence })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.BucketStartUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.BucketEndUnixTimeMilliseconds });
    }
}
