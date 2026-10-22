using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelSerialCounterAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.EntityConfigurations;

public sealed class LabelSerialCounterEntityTypeConfiguration : IEntityTypeConfiguration<LabelSerialCounter>
{
    public void Configure(EntityTypeBuilder<LabelSerialCounter> builder)
    {
        builder.ToTable("label_serial_counters", tableBuilder =>
            tableBuilder.HasComment("Persistent serial allocation counters partitioned by organization, environment, and exact serial text width."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Label serial counter aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the serial allocation scope.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id for the serial allocation scope.");
        builder.Property(x => x.SerialNumberLength).HasColumnName("serial_number_length").IsRequired().HasComment("Fixed Base62 serial text width that defines an exact collision partition.");
        builder.Property(x => x.CurrentValue).HasColumnName("current_value").IsRequired().HasComment("Highest monotonically allocated counter value in the collision partition.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SerialNumberLength })
            .IsUnique()
            .HasDatabaseName("UX_label_serial_counters_scope");
    }
}
