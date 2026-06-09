using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class ReferenceDataCodeEntityTypeConfiguration : IEntityTypeConfiguration<ReferenceDataCode>
{
    public void Configure(EntityTypeBuilder<ReferenceDataCode> builder)
    {
        builder.ToTable("reference_data_codes", tableBuilder =>
            tableBuilder.HasComment("Business master data controlled reference codes shared by business domains."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Reference data code aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the reference data code.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the reference data code is valid.");
        builder.Property(x => x.CodeSet).HasColumnName("code_set").IsRequired().HasMaxLength(100).HasComment("Reserved reference code set name such as material-type, storage-condition, quality-reason or compliance-tag.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Business unique code inside the code set.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Reference data code display name.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the reference code from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the reference code was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the reference code was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.CodeSet, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.CodeSet, x.Disabled });
    }
}
