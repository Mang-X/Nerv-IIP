using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.EntityConfigurations;

public sealed class ManufacturingBomEntityTypeConfiguration : IEntityTypeConfiguration<ManufacturingBom>
{
    public void Configure(EntityTypeBuilder<ManufacturingBom> builder)
    {
        builder.ToTable("manufacturing_boms", tableBuilder =>
            tableBuilder.HasComment("ProductEngineering manufacturing BOM versions that reference released EBOM facts and process recipe lines."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Manufacturing BOM aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the MBOM is valid.");
        builder.Property(x => x.BomCode).HasColumnName("bom_code").IsRequired().HasMaxLength(100).HasComment("Manufacturing BOM business code.");
        builder.Property(x => x.Revision).HasColumnName("revision").IsRequired().HasMaxLength(50).HasComment("Manufacturing BOM revision.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Produced SKU code.");
        builder.Property(x => x.EngineeringBomVersionId).HasColumnName("engineering_bom_version_id").IsRequired().HasMaxLength(150).HasComment("Released EBOM version id used as design source.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(30).HasComment("Manufacturing BOM lifecycle status.");
        builder.Property(x => x.EffectiveDate).HasColumnName("effective_date").HasComment("First effective date after release.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the MBOM was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the MBOM was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.BomCode, x.Revision }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SkuCode, x.Status });
        builder.OwnsMany(x => x.MaterialLines, ConfigureMaterialLines);
        builder.OwnsMany(x => x.RecipeLines, ConfigureRecipeLines);
        builder.Navigation(x => x.MaterialLines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.RecipeLines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureMaterialLines(OwnedNavigationBuilder<ManufacturingBom, ManufacturingBomMaterialLine> builder)
    {
        builder.ToTable("manufacturing_bom_material_lines", tableBuilder =>
            tableBuilder.HasComment("Manufacturing BOM SKU material lines."));
        builder.WithOwner().HasForeignKey("manufacturing_bom_id");
        builder.Property<int>("id").ValueGeneratedOnAdd();
        builder.HasKey("id");
        builder.Property("manufacturing_bom_id").HasColumnName("manufacturing_bom_id").HasComment("Owning manufacturing BOM id.");
        builder.Property(x => x.SkuCode).HasColumnName("sku_code").IsRequired().HasMaxLength(100).HasComment("Consumed SKU code.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Consumed material quantity.");
        builder.Property(x => x.UnitOfMeasureCode).HasColumnName("unit_of_measure_code").IsRequired().HasMaxLength(50).HasComment("Quantity unit of measure code.");
        builder.Property(x => x.ScrapRate).HasColumnName("scrap_rate").IsRequired().HasPrecision(18, 6).HasComment("Expected scrap rate for the material line.");
        builder.HasIndex("manufacturing_bom_id", nameof(ManufacturingBomMaterialLine.SkuCode)).IsUnique();
    }

    private static void ConfigureRecipeLines(OwnedNavigationBuilder<ManufacturingBom, ManufacturingBomRecipeLine> builder)
    {
        builder.ToTable("manufacturing_bom_recipe_lines", tableBuilder =>
            tableBuilder.HasComment("Manufacturing BOM process recipe and formula parameter lines."));
        builder.WithOwner().HasForeignKey("manufacturing_bom_id");
        builder.Property<int>("id").ValueGeneratedOnAdd();
        builder.HasKey("id");
        builder.Property("manufacturing_bom_id").HasColumnName("manufacturing_bom_id").HasComment("Owning manufacturing BOM id.");
        builder.Property(x => x.ParameterCode).HasColumnName("parameter_code").IsRequired().HasMaxLength(100).HasComment("Process parameter code.");
        builder.Property(x => x.TargetValue).HasColumnName("target_value").IsRequired().HasMaxLength(200).HasComment("Target process parameter value.");
        builder.Property(x => x.UnitOfMeasureCode).HasColumnName("unit_of_measure_code").IsRequired().HasMaxLength(50).HasComment("Parameter unit of measure code.");
        builder.HasIndex("manufacturing_bom_id", nameof(ManufacturingBomRecipeLine.ParameterCode)).IsUnique();
    }
}
