using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.AlarmRuleAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class AlarmRuleEntityTypeConfiguration : IEntityTypeConfiguration<AlarmRule>
{
    public void Configure(EntityTypeBuilder<AlarmRule> builder)
    {
        builder.ToTable("alarm_rules", table => table.HasComment("BusinessIndustrialTelemetry alarm rule threshold configuration."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Alarm rule identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(100).HasColumnName("organization_id").HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(100).HasColumnName("environment_id").HasComment("Owning environment identifier.");
        builder.Property(x => x.DeviceAssetId).IsRequired().HasMaxLength(150).HasColumnName("device_asset_id").HasComment("Referenced MasterData device asset identifier.");
        builder.Property(x => x.RuleCode).IsRequired().HasMaxLength(100).HasColumnName("rule_code").HasComment("Stable alarm rule code unique within a device.");
        builder.Property(x => x.AlarmCode).IsRequired().HasMaxLength(100).HasColumnName("alarm_code").HasComment("Alarm code raised when the rule condition is met.");
        builder.Property(x => x.Severity).IsRequired().HasMaxLength(50).HasColumnName("severity").HasComment("Alarm severity emitted by this rule.");
        builder.Property(x => x.TagKey).IsRequired().HasMaxLength(150).HasColumnName("tag_key").HasComment("Telemetry tag key evaluated by this alarm rule.");
        builder.Property(x => x.ComparisonOperator).IsRequired().HasMaxLength(8).HasColumnName("comparison_operator").HasComment("Threshold comparison operator such as >= or <.");
        builder.Property(x => x.ThresholdValue).HasPrecision(18, 6).HasColumnName("threshold_value").HasComment("Numeric threshold value used by the rule condition.");
        builder.Property(x => x.UnitCode).IsRequired().HasMaxLength(50).HasColumnName("unit_code").HasComment("Unit of measure code for the threshold.");
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasComment("Whether the alarm rule is enabled for evaluation.");
        builder.Property(x => x.DeadbandValue).HasPrecision(18, 6).HasColumnName("deadband_value").HasComment("Deadband value applied before clearing a threshold alarm.");
        builder.Property(x => x.OnDelaySeconds).HasColumnName("on_delay_seconds").HasComment("Continuous breach seconds required before raising the alarm.");
        builder.Property(x => x.OffDelaySeconds).HasColumnName("off_delay_seconds").HasComment("Continuous return-to-normal seconds required before clearing the alarm.");
        builder.Property(x => x.MinDurationSeconds).HasColumnName("min_duration_seconds").HasComment("Minimum breach duration seconds required before raising the alarm.");
        builder.Property(x => x.Priority).IsRequired().HasMaxLength(50).HasColumnName("priority").HasComment("Independent alarm priority, separate from severity.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC time when the alarm rule was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasComment("UTC time when the alarm rule was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.RuleCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.TagKey });
    }
}
