using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;

namespace Nerv.IIP.Business.Inventory.Infrastructure.EntityConfigurations;

public sealed class StockLocationEntityTypeConfiguration : IEntityTypeConfiguration<StockLocation>
{
    public void Configure(EntityTypeBuilder<StockLocation> builder)
    {
        builder.ToTable("stock_locations", tableBuilder =>
            tableBuilder.HasComment("Inventory stock locations such as warehouse, zone, bin or logical stock area."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Stock location aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the location.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the location is valid.");
        builder.Property(x => x.LocationCode).HasColumnName("location_code").IsRequired().HasMaxLength(100).HasComment("Business stock location code.");
        builder.Property(x => x.LocationType).HasColumnName("location_type").IsRequired().HasMaxLength(50).HasComment("Location type such as warehouse, zone, bin or logical.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("MasterData site code associated with this location.");
        builder.Property(x => x.ParentLocationCode).HasColumnName("parent_location_code").HasMaxLength(100).HasComment("Optional parent stock location code.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("Location lifecycle status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the location was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the location was last changed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.LocationCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SiteCode, x.Status });
    }
}
