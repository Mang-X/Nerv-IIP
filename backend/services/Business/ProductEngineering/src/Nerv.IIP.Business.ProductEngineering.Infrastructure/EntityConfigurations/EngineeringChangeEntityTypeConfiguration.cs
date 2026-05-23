using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.EntityConfigurations;

public sealed class EngineeringChangeEntityTypeConfiguration : IEntityTypeConfiguration<EngineeringChange>
{
    public void Configure(EntityTypeBuilder<EngineeringChange> builder)
    {
        builder.ToTable("engineering_changes", tableBuilder =>
            tableBuilder.HasComment("ProductEngineering ECO and ECN change release facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Engineering change aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the change applies.");
        builder.Property(x => x.ChangeNumber).HasColumnName("change_number").IsRequired().HasMaxLength(100).HasComment("Engineering change order or notice number.");
        builder.Property(x => x.Reason).HasColumnName("reason").IsRequired().HasMaxLength(500).HasComment("Engineering change reason.");
        builder.Property(x => x.ApprovalReferenceId).HasColumnName("approval_reference_id").IsRequired().HasMaxLength(150).HasComment("Business approval chain or approval result reference id.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasConversion<string>().HasMaxLength(30).HasComment("Engineering change lifecycle status.");
        builder.Property(x => x.EffectiveDate).HasColumnName("effective_date").HasComment("First effective date after release.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the engineering change was opened.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the engineering change was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ChangeNumber }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status });
        builder.OwnsMany(x => x.AffectedVersions, ConfigureAffectedVersions);
        builder.Navigation(x => x.AffectedVersions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureAffectedVersions(OwnedNavigationBuilder<EngineeringChange, EngineeringChangeAffectedVersion> builder)
    {
        builder.ToTable("engineering_change_affected_versions", tableBuilder =>
            tableBuilder.HasComment("Engineering change affected document, BOM, routing or production version references."));
        builder.WithOwner().HasForeignKey("engineering_change_id");
        builder.Property<int>("id").ValueGeneratedOnAdd();
        builder.HasKey("id");
        builder.Property("engineering_change_id").HasColumnName("engineering_change_id").HasComment("Owning engineering change id.");
        builder.Property(x => x.VersionKind).HasColumnName("version_kind").IsRequired().HasMaxLength(100).HasComment("Affected version kind such as document, engineering-bom, manufacturing-bom, routing or production-version.");
        builder.Property(x => x.VersionId).HasColumnName("version_id").IsRequired().HasMaxLength(150).HasComment("Affected version id.");
        builder.HasIndex("engineering_change_id", nameof(EngineeringChangeAffectedVersion.VersionKind), nameof(EngineeringChangeAffectedVersion.VersionId)).IsUnique();
    }
}
