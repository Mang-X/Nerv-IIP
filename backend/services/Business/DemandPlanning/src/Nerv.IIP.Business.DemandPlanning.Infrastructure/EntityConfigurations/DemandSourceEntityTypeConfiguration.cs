using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.DemandSourceAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.EntityConfigurations;

public sealed class DemandSourceEntityTypeConfiguration : IEntityTypeConfiguration<DemandSource>
{
    public void Configure(EntityTypeBuilder<DemandSource> builder)
    {
        builder.ToTable("demand_sources", table => table.HasComment("DemandPlanning owned demand source facts for MPS and MRP input."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Demand source aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id that owns the demand.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Planning environment id, such as dev, test, or production planning space.");
        builder.Property(x => x.DemandType).HasColumnName("demand_type").HasMaxLength(32).IsRequired().HasComment("Demand type: forecast, sales-order, safety-stock, or manual.");
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(128).IsRequired().HasComment("External or manual source reference unique in the planning scope.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").HasMaxLength(64).IsRequired().HasComment("Demanded finished-good SKU code snapshot.");
        builder.Property(x => x.UomCode).HasColumnName("uom_code").HasMaxLength(32).IsRequired().HasComment("Demand quantity unit of measure snapshot.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").HasMaxLength(64).IsRequired().HasComment("Demand site code snapshot.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).HasComment("Positive demand quantity.");
        builder.Property(x => x.DueDate).HasColumnName("due_date").HasComment("Demand due date bucket.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC timestamp when the demand source was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasComment("UTC timestamp when the demand source was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DemandType, x.SourceReference }).IsUnique();
    }
}
