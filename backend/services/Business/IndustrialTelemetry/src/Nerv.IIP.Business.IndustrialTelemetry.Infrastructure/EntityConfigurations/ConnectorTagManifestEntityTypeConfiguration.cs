using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.ConnectorTagManifestAggregate;

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.EntityConfigurations;

public sealed class ConnectorTagManifestEntityTypeConfiguration : IEntityTypeConfiguration<ConnectorTagManifest>
{
    public void Configure(EntityTypeBuilder<ConnectorTagManifest> builder)
    {
        builder.ToTable("connector_tag_manifests", table => table.HasComment("BusinessIndustrialTelemetry accepted connector tag manifest revisions."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Connector tag manifest identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Owning organization identifier.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Owning environment identifier.");
        builder.Property(x => x.CollectionConnectorId).HasColumnName("collection_connector_id").IsRequired().HasMaxLength(150).HasComment("Canonical collection connector identity owning this manifest.");
        builder.Property(x => x.SourceSystem).HasColumnName("source_system").IsRequired().HasMaxLength(100).HasComment("Source system that observed the accepted manifest.");
        builder.Property(x => x.ManifestRevision).HasColumnName("manifest_revision").IsRequired().HasMaxLength(64).HasComment("Lowercase SHA-256 revision of the accepted manifest payload.");
        builder.Property(x => x.ManifestObservedAtUtc).HasColumnName("manifest_observed_at_utc").IsConcurrencyToken().HasComment("Source observation time ordering accepted manifest revisions.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CollectionConnectorId }).IsUnique();
        builder.HasMany(x => x.Bindings)
            .WithOne()
            .HasForeignKey(x => x.ConnectorTagManifestId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Bindings).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
