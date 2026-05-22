using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.EntityConfigurations;

public sealed class ProductionVersionEntityTypeConfiguration : IEntityTypeConfiguration<ProductionVersion>
{
    public void Configure(EntityTypeBuilder<ProductionVersion> builder)
    {
        builder.ToTable("production_versions", tableBuilder =>
            tableBuilder.HasComment("ProductEngineering production versions binding released MBOM and routing versions for planning and MES work order creation."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Production version aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the version is valid.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Finished or semi-finished SKU code.");
        builder.Property(x => x.MbomVersionId).HasColumnName("mbom_version_id").IsRequired().HasMaxLength(100).HasComment("Released manufacturing BOM version id.");
        builder.Property(x => x.RoutingVersionId).HasColumnName("routing_version_id").IsRequired().HasMaxLength(100).HasComment("Released routing version id.");
        builder.Property(x => x.ValidFrom).HasColumnName("valid_from").IsRequired().HasComment("First effective production date.");
        builder.Property(x => x.ValidTo).HasColumnName("valid_to").HasComment("Last effective production date, null for open-ended.");
        builder.Property(x => x.LotSizeMin).HasColumnName("lot_size_min").HasPrecision(18, 6).HasComment("Minimum lot size covered by this version.");
        builder.Property(x => x.LotSizeMax).HasColumnName("lot_size_max").HasPrecision(18, 6).HasComment("Maximum lot size covered by this version.");
        builder.Property(x => x.Priority).HasColumnName("priority").IsRequired().HasComment("Selection priority among matching production versions.");
        builder.Property(x => x.IsDefault).HasColumnName("is_default").IsRequired().HasComment("Default version flag for the SKU and effective window.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(20).HasComment("Production version lifecycle status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the version was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the version was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.Status });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.IsDefault, x.ValidFrom, x.ValidTo });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.MbomVersionId, x.RoutingVersionId });
    }
}
