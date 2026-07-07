using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryTagAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class TelemetryTagEntityTypeConfiguration : IEntityTypeConfiguration<TelemetryTag>
{
    public void Configure(EntityTypeBuilder<TelemetryTag> builder)
    {
        builder.ToTable("telemetry_tags", table => table.HasComment("BusinessIndustrialTelemetry telemetry tag mapping metadata."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Telemetry tag identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasColumnName("organization_id").HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasColumnName("environment_id").HasComment("Owning environment identifier.");
        builder.Property(x => x.DeviceAssetId).IsRequired().HasMaxLength(150).HasColumnName("device_asset_id").HasComment("Referenced MasterData device asset identifier.");
        builder.Property(x => x.TagKey).IsRequired().HasMaxLength(150).HasColumnName("tag_key").HasComment("Telemetry tag key unique within a device stream.");
        builder.Property(x => x.ValueType).IsRequired().HasMaxLength(50).HasColumnName("value_type").HasComment("Telemetry value type such as number, bool, or text.");
        builder.Property(x => x.UnitCode).IsRequired().HasMaxLength(50).HasColumnName("unit_code").HasComment("Unit of measure code for summarized telemetry values.");
        builder.Property(x => x.SamplingPolicy).IsRequired().HasMaxLength(100).HasColumnName("sampling_policy").HasComment("Configured ingestion sampling policy.");
        builder.Property(x => x.IsWritable).HasColumnName("is_writable").HasComment("Whether this telemetry tag may be used as a validated device control write target.");
        builder.Property(x => x.ControlMinValue).HasColumnName("control_min_value").HasComment("Optional minimum allowed control value for numeric device control writes.");
        builder.Property(x => x.ControlMaxValue).HasColumnName("control_max_value").HasComment("Optional maximum allowed control value for numeric device control writes.");
        builder.Property(x => x.ControlAllowedValuesJson).IsRequired().HasColumnName("control_allowed_values_json").HasDefaultValue("[]").HasComment("JSON array of optional allowed literal values for device control writes; produced by IndustrialTelemetry tag metadata and consumed by device control validation, additive values are compatible.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC time when the tag mapping was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasComment("UTC time when the tag mapping was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey }).IsUnique();
    }
}
