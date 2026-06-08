using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkshopAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class WorkshopEntityTypeConfiguration : IEntityTypeConfiguration<Workshop>
{
    public void Configure(EntityTypeBuilder<Workshop> builder)
    {
        builder.ToTable("workshops", tableBuilder =>
            tableBuilder.HasComment("Business master data workshops used as organizational and area grouping under a site."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Workshop aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the workshop.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the workshop is valid.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Business unique workshop code.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Workshop display name.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("Site or plant code that contains the workshop.");
        builder.Property(x => x.ManagerUserId).HasColumnName("manager_user_id").HasMaxLength(100).HasComment("Optional IAM user id for the workshop manager.");
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).HasComment("Optional workshop description for operations and dashboard grouping.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the workshop from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the workshop was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the workshop was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.SiteCode, x.Disabled });
    }
}
