using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class BusinessPartnerEntityTypeConfiguration : IEntityTypeConfiguration<BusinessPartner>
{
    public void Configure(EntityTypeBuilder<BusinessPartner> builder)
    {
        builder.ToTable("business_partners", tableBuilder =>
            tableBuilder.HasComment("Business master data partners such as suppliers, customers and carriers."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Business partner aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the business partner.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the business partner is valid.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Business unique partner code within the organization and environment.");
        builder.Property(x => x.PartnerType).HasColumnName("partner_type").IsRequired().HasMaxLength(80).HasComment("Primary business partner role kept for backward-compatible list filters.");
        builder.Property(x => x.PartnerRoles).HasColumnName("partner_roles").HasColumnType("text[]").IsRequired().HasComment("All roles held by the business partner, such as supplier, customer or carrier.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Business partner display name.");
        builder.Property(x => x.TaxId).HasColumnName("tax_id").HasMaxLength(100).HasComment("Optional tax registration id unique within the organization and environment.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the partner from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the business partner was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the business partner was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TaxId }).IsUnique().HasFilter("tax_id IS NOT NULL");
        builder.HasIndex(x => new { x.PartnerType, x.Disabled });
    }
}
