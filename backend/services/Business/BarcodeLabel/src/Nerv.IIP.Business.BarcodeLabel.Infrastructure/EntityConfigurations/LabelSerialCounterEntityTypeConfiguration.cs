using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.EntityConfigurations;

public sealed class LabelSerialCounterEntityTypeConfiguration : IEntityTypeConfiguration<LabelSerialCounter>
{
    public void Configure(EntityTypeBuilder<LabelSerialCounter> builder)
    {
        builder.ToTable("label_serial_counters", tableBuilder =>
            tableBuilder.HasComment("Persistent serial allocation counters scoped by organization, environment and barcode rule."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Label serial counter aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the serial allocation scope.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the serial allocation scope.");
        builder.Property(x => x.BarcodeRuleId).HasColumnName("barcode_rule_id").IsRequired().HasComment("Barcode rule id for the serial allocation scope.");
        builder.Property(x => x.CurrentValue).HasColumnName("current_value").IsRequired().HasComment("Highest monotonically allocated counter value in the scope.");
        builder.HasOne<BarcodeRule>()
            .WithMany()
            .HasForeignKey(x => x.BarcodeRuleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.BarcodeRuleId })
            .IsUnique()
            .HasDatabaseName("UX_label_serial_counters_scope");
    }
}
