using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.EntityConfigurations;

public sealed class LabelSerialCounterEntityTypeConfiguration : IEntityTypeConfiguration<LabelSerialCounter>
{
    public void Configure(EntityTypeBuilder<LabelSerialCounter> builder)
    {
        builder.ToTable("label_serial_counters", tableBuilder =>
        {
            tableBuilder.HasComment("Persistent per-rule allocator state for generated unit serial numbers.");
            tableBuilder.HasCheckConstraint("ck_label_serial_counters_current_value_positive", "current_value > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Serial counter aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id owning the serial namespace.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id owning the serial namespace.");
        builder.Property(x => x.BarcodeRuleId).HasColumnName("barcode_rule_id").IsRequired().HasComment("Barcode rule owning the serial namespace.");
        builder.Property(x => x.CurrentValue).HasColumnName("current_value").IsRequired().HasComment("Last value atomically reserved in this serial namespace.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.BarcodeRuleId }).IsUnique();
    }
}
