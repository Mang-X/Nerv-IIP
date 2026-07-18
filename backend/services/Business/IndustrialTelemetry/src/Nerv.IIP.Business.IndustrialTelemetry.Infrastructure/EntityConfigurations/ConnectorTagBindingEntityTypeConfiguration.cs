using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.ConnectorTagManifestAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class ConnectorTagBindingEntityTypeConfiguration : IEntityTypeConfiguration<ConnectorTagBinding>
{
    public void Configure(EntityTypeBuilder<ConnectorTagBinding> builder)
    {
        builder.ToTable("connector_tag_bindings", table =>
        {
            table.HasComment("BusinessIndustrialTelemetry current and retired connector device-tag bindings.");
            table.HasCheckConstraint(
                "ck_connector_tag_bindings_activation_status",
                "activation_status IN ('pending', 'active', 'error', 'disabled')");
            table.HasCheckConstraint(
                "ck_connector_tag_bindings_current_retirement",
                "(is_current AND retired_at_utc IS NULL) OR (NOT is_current AND retired_at_utc IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Connector tag binding identifier.");
        builder.Property(x => x.ConnectorTagManifestId).HasColumnName("connector_tag_manifest_id").HasComment("Owning connector tag manifest identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Owning organization identifier duplicated for scoped lookups.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Owning environment identifier duplicated for scoped lookups.");
        builder.Property(x => x.CollectionConnectorId).HasColumnName("collection_connector_id").IsRequired().HasMaxLength(150).HasComment("Canonical collection connector identity owning the binding.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").IsRequired().HasMaxLength(150).HasComment("Referenced MasterData device asset identifier.");
        builder.Property(x => x.TagKey).HasColumnName("tag_key").IsRequired().HasMaxLength(150).HasComment("Normalized telemetry tag key exposed by the connector.");
        builder.Property(x => x.Enabled).HasColumnName("enabled").HasComment("Whether the manifest enables collection for the binding.");
        builder.Property(x => x.ProtocolAddress).HasColumnName("protocol_address").HasMaxLength(500).HasComment("Optional protocol-native source address for diagnostics.");
        builder.Property(x => x.IsCurrent).HasColumnName("is_current").HasComment("Whether the binding is present in the accepted manifest revision.");
        builder.Property(x => x.RetiredAtUtc).HasColumnName("retired_at_utc").HasComment("Accepted manifest observation time that retired the binding.");
        builder.Property(x => x.ActivationStatus).HasColumnName("activation_status").IsRequired().HasMaxLength(16).HasComment("Latest independently ordered activation status.");
        builder.Property(x => x.ActivationObservedAtUtc).HasColumnName("activation_observed_at_utc").HasComment("UTC source observation time displayed for the latest activation update.");
        builder.Property(x => x.ActivationObservedAtUtcTicks).HasColumnName("activation_observed_at_utc_ticks").HasComment("Exact .NET UTC ticks ordering activation updates without timestamptz precision loss.");
        builder.Property(x => x.ActivationErrorCode).HasColumnName("activation_error_code").HasMaxLength(128).HasComment("Sanitized bounded connector activation error code.");
        builder.Property(x => x.ActivationErrorMessage).HasColumnName("activation_error_message").HasMaxLength(500).HasComment("Sanitized bounded connector activation error message.");
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken().HasComment("Application-managed optimistic concurrency version incremented by binding mutations.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CollectionConnectorId, x.DeviceAssetId, x.TagKey }).IsUnique();
        builder.HasIndex(x => new { x.ConnectorTagManifestId, x.IsCurrent });
    }
}
