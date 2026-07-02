using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.UomConversionAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class UomConversionEntityTypeConfiguration : IEntityTypeConfiguration<UomConversion>
{
    public void Configure(EntityTypeBuilder<UomConversion> builder)
    {
        builder.ToTable("uom_conversions", tableBuilder =>
            tableBuilder.HasComment("Business master data unit conversion rules with effective dates and rounding policy."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("UOM conversion aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the UOM conversion.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the UOM conversion is valid.");
        builder.Property(x => x.FromUomCode).HasColumnName("from_uom_code").IsRequired().HasMaxLength(50).HasComment("Source unit of measure code.");
        builder.Property(x => x.ToUomCode).HasColumnName("to_uom_code").IsRequired().HasMaxLength(50).HasComment("Target unit of measure code.");
        builder.Property(x => x.Factor).HasColumnName("factor").IsRequired().HasPrecision(24, 12).HasComment("Positive multiplicative factor used for conversion.");
        builder.Property(x => x.Offset).HasColumnName("offset").IsRequired().HasPrecision(24, 12).HasComment("Additive offset applied after the conversion factor.");
        builder.Property(x => x.Precision).HasColumnName("precision").IsRequired().HasComment("Decimal precision used for converted values.");
        builder.Property(x => x.RoundingMode).HasColumnName("rounding_mode").IsRequired().HasMaxLength(80).HasComment("Rounding mode used for converted values.");
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from").IsRequired().HasComment("Business date from which the conversion rule is effective.");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasComment("Optional business date through which the conversion rule is effective.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the conversion rule from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the conversion rule was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the conversion rule was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.FromUomCode, x.ToUomCode, x.EffectiveFrom }).IsUnique();
        builder.HasIndex(x => new { x.FromUomCode, x.ToUomCode });
        builder.HasIndex(x => x.Disabled);
    }
}
