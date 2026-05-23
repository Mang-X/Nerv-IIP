using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.EntityConfigurations;

public sealed class EngineeringItemEntityTypeConfiguration : IEntityTypeConfiguration<EngineeringItem>
{
    public void Configure(EntityTypeBuilder<EngineeringItem> builder)
    {
        builder.ToTable("engineering_items", tableBuilder =>
            tableBuilder.HasComment("ProductEngineering versioned engineering item revisions used by EBOM authoring."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Engineering item aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the item revision is valid.");
        builder.Property(x => x.ItemCode).HasColumnName("item_code").IsRequired().HasMaxLength(100).HasComment("Engineering item code.");
        builder.Property(x => x.Revision).HasColumnName("revision").IsRequired().HasMaxLength(50).HasComment("Engineering item revision.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Engineering item display name.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(30).HasComment("Engineering item lifecycle status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the item revision was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the item revision was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ItemCode, x.Revision }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status });
    }
}
