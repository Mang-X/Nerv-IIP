using Nerv.IIP.Business.DemandPlanning.Domain.AggregatesModel.PlanningSuggestionAggregate;

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.EntityConfigurations;

public sealed class PeggingLinkEntityTypeConfiguration : IEntityTypeConfiguration<PeggingLink>
{
    public void Configure(EntityTypeBuilder<PeggingLink> builder)
    {
        builder.ToTable("mrp_pegging_links", table => table.HasComment("DemandPlanning MRP pegging links from suggestions back to demand and input snapshots."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("MRP pegging link entity id.");
        builder.Property(x => x.PlanningSuggestionId).HasColumnName("planning_suggestion_id").HasComment("Owning planning suggestion id.");
        builder.Property(x => x.PeggingType).HasColumnName("pegging_type").HasMaxLength(32).IsRequired().HasComment("Pegging type, such as demand or bom-component.");
        builder.Property(x => x.DemandSourceReference).HasColumnName("demand_source_reference").HasMaxLength(128).IsRequired().HasComment("Demand source reference that caused the suggestion.");
        builder.Property(x => x.ParentSkuCode).HasColumnName("parent_sku_code").HasMaxLength(64).IsRequired().HasComment("Parent SKU code in the MRP explanation.");
        builder.Property(x => x.ComponentSkuCode).HasColumnName("component_sku_code").HasMaxLength(64).HasComment("Component SKU code in the MRP explanation when relevant.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 6).HasComment("Pegged quantity attributable to the demand.");
        builder.Property(x => x.ProductionVersionReference).HasColumnName("production_version_reference").HasMaxLength(128).HasComment("ProductEngineering production version snapshot reference.");
        builder.Property(x => x.ManufacturingBomReference).HasColumnName("manufacturing_bom_reference").HasMaxLength(128).HasComment("ProductEngineering manufacturing BOM snapshot reference.");
        builder.Property(x => x.RoutingReference).HasColumnName("routing_reference").HasMaxLength(128).HasComment("ProductEngineering routing snapshot reference.");
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32).IsRequired().HasComment("Requirement source type such as sales, forecast, safety-stock, mps, component, or scheduled-receipt.");
        builder.Property(x => x.GrossDemandQuantity).HasColumnName("gross_demand_quantity").HasPrecision(18, 6).HasComment("Gross requirement quantity represented by this pegging link.");
    }
}
