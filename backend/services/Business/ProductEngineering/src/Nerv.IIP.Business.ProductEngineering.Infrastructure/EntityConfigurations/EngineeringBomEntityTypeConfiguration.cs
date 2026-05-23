using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.EntityConfigurations;

public sealed class EngineeringBomEntityTypeConfiguration : IEntityTypeConfiguration<EngineeringBom>
{
    public void Configure(EntityTypeBuilder<EngineeringBom> builder)
    {
        builder.ToTable("engineering_boms", tableBuilder =>
            tableBuilder.HasComment("ProductEngineering released and draft engineering BOM versions."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Engineering BOM aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the EBOM is valid.");
        builder.Property(x => x.BomCode).HasColumnName("bom_code").IsRequired().HasMaxLength(100).HasComment("Engineering BOM business code.");
        builder.Property(x => x.Revision).HasColumnName("revision").IsRequired().HasMaxLength(50).HasComment("Engineering BOM revision.");
        builder.Property(x => x.ParentItemCode).HasColumnName("parent_item_code").IsRequired().HasMaxLength(100).HasComment("Parent engineering item code.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(30).HasComment("Engineering BOM lifecycle status.");
        builder.Property(x => x.EffectiveDate).HasColumnName("effective_date").HasComment("First effective date after release.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the EBOM was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the EBOM was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.BomCode, x.Revision }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ParentItemCode, x.Status });
        builder.OwnsMany(x => x.Lines, ConfigureLines);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureLines(OwnedNavigationBuilder<EngineeringBom, EngineeringBomLine> builder)
    {
        builder.ToTable("engineering_bom_lines", tableBuilder =>
            tableBuilder.HasComment("Engineering BOM component lines."));
        builder.WithOwner().HasForeignKey("engineering_bom_id");
        builder.Property<int>("id").ValueGeneratedOnAdd();
        builder.HasKey("id");
        builder.Property("engineering_bom_id").HasColumnName("engineering_bom_id").HasComment("Owning engineering BOM id.");
        builder.Property(x => x.ChildItemCode).HasColumnName("child_item_code").IsRequired().HasMaxLength(100).HasComment("Child engineering item code.");
        builder.Property(x => x.Quantity).HasColumnName("quantity").IsRequired().HasPrecision(18, 6).HasComment("Component quantity.");
        builder.Property(x => x.UnitOfMeasureCode).HasColumnName("unit_of_measure_code").IsRequired().HasMaxLength(50).HasComment("Quantity unit of measure code.");
        builder.HasIndex("engineering_bom_id", nameof(EngineeringBomLine.ChildItemCode)).IsUnique();
    }
}
