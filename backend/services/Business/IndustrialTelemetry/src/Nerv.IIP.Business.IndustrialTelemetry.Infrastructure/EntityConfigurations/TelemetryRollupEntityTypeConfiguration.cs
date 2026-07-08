using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryRollupAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class TelemetryRollupEntityTypeConfiguration : IEntityTypeConfiguration<TelemetryRollup>
{
    public void Configure(EntityTypeBuilder<TelemetryRollup> builder)
    {
        builder.ToTable("telemetry_rollups", table => table.HasComment("BusinessIndustrialTelemetry historian hourly and daily rollups."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Telemetry historian rollup identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasColumnName("organization_id").HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasColumnName("environment_id").HasComment("Owning environment identifier.");
        builder.Property(x => x.DeviceAssetId).IsRequired().HasMaxLength(150).HasColumnName("device_asset_id").HasComment("Referenced MasterData device asset identifier.");
        builder.Property(x => x.TagKey).IsRequired().HasMaxLength(150).HasColumnName("tag_key").HasComment("Telemetry tag key represented by this historian rollup.");
        builder.Property(x => x.Grain).HasConversion<string>().HasMaxLength(20).HasColumnName("grain").HasComment("Historian rollup grain: Hourly or Daily.");
        builder.Property(x => x.WindowStartUtc).HasColumnName("window_start_utc").HasComment("Inclusive UTC start of the historian rollup window.");
        builder.Property(x => x.WindowEndUtc).HasColumnName("window_end_utc").HasComment("Exclusive UTC end of the historian rollup window.");
        builder.Property(x => x.DailyWindowStartUtc).HasColumnName("daily_window_start_utc").HasComment("UTC day window start used for indexed hourly-to-daily downsampling anti-joins.");
        builder.Property(x => x.WindowEndUnixTimeMilliseconds).HasColumnName("window_end_unix_time_milliseconds").HasComment("Exclusive UTC rollup end represented as Unix time milliseconds for provider-neutral retention scans.");
        builder.Property(x => x.SampleCount).HasColumnName("sample_count").HasComment("Number of raw samples represented by the historian rollup.");
        builder.Property(x => x.MinValue).HasColumnName("min_value").HasPrecision(18, 6).HasComment("Minimum numeric value in the historian rollup.");
        builder.Property(x => x.MaxValue).HasColumnName("max_value").HasPrecision(18, 6).HasComment("Maximum numeric value in the historian rollup.");
        builder.Property(x => x.AverageValue).HasColumnName("average_value").HasPrecision(18, 6).HasComment("Weighted average value in the historian rollup.");
        builder.Property(x => x.FirstValue).HasColumnName("first_value").HasPrecision(18, 6).HasComment("First observed numeric value in the historian rollup.");
        builder.Property(x => x.LastValue).HasColumnName("last_value").HasPrecision(18, 6).HasComment("Last observed numeric value in the historian rollup.");
        builder.Property(x => x.SourceSequence).IsRequired().HasMaxLength(200).HasColumnName("source_sequence").HasComment("Deterministic historian rollup source sequence for idempotent downsampling.");
        builder.Property(x => x.RolledUpAtUtc).HasColumnName("rolled_up_at_utc").HasComment("UTC time when the historian rollup was created.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.Grain, x.WindowStartUtc }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.Grain, x.DailyWindowStartUtc })
            .HasDatabaseName("IX_telemetry_rollups_daily_window");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey, x.Grain, x.WindowEndUnixTimeMilliseconds });
    }
}
