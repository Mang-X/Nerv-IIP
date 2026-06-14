using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ProductCategoryAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class ProductCategoryEntityTypeConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("product_categories", tableBuilder =>
            tableBuilder.HasComment("Business master data product category hierarchy."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Product category aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the product category.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the product category is valid.");
        builder.Property(x => x.CategoryCode).HasColumnName("category_code").IsRequired().HasMaxLength(100).HasComment("Business unique product category code.");
        builder.Property(x => x.CategoryName).HasColumnName("category_name").IsRequired().HasMaxLength(200).HasComment("Product category display name.");
        builder.Property(x => x.ParentCode).HasColumnName("parent_code").HasMaxLength(100).HasComment("Optional parent category code in the same organization and environment.");
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).HasComment("Optional product category description.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the product category from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the product category was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the product category was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CategoryCode }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ParentCode, x.Disabled });
        builder.HasIndex(x => x.Disabled);
    }
}
