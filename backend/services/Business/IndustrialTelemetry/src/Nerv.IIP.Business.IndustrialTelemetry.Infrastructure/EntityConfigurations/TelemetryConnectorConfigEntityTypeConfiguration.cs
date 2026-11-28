using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryConnectorConfigAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class TelemetryConnectorConfigEntityTypeConfiguration : IEntityTypeConfiguration<TelemetryConnectorConfig>
{
    public void Configure(EntityTypeBuilder<TelemetryConnectorConfig> builder)
    {
        builder.ToTable("telemetry_connector_configs", table => table.HasComment("Desired telemetry collector configuration."));
        builder.HasKey(x => new { x.OrganizationId, x.EnvironmentId, x.ConnectorId });
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(100);
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(100);
        builder.Property(x => x.ConnectorId).HasColumnName("connector_id").HasMaxLength(150);
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(150);
        builder.Property(x => x.Protocol).HasColumnName("protocol").HasMaxLength(20);
        builder.Property(x => x.Endpoint).HasColumnName("endpoint").HasMaxLength(500);
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled");
        builder.Property(x => x.PollIntervalSeconds).HasColumnName("poll_interval_seconds");
        builder.Property(x => x.MappingsJson).HasColumnName("mappings_json");
        builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}
