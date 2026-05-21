using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UnitOfMeasureAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class UnitOfMeasureEntityTypeConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("units_of_measure", tableBuilder =>
            tableBuilder.HasComment("Business master data units of measure used by material, inventory, quality, planning and execution."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Unit of measure aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the unit of measure.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the unit of measure is valid.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(50).HasComment("Business unique unit of measure code.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(120).HasComment("Unit of measure display name.");
        builder.Property(x => x.DimensionType).HasColumnName("dimension_type").IsRequired().HasMaxLength(80).HasComment("Physical or business dimension such as mass, volume, count, time or potency.");
        builder.Property(x => x.Precision).HasColumnName("precision").IsRequired().HasComment("Decimal precision allowed when values are expressed in this unit.");
        builder.Property(x => x.RoundingMode).HasColumnName("rounding_mode").IsRequired().HasMaxLength(80).HasComment("Rounding mode used when converting values to this unit.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the unit from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the unit was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the unit was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.DimensionType, x.Disabled });
    }
}
