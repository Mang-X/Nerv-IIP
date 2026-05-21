using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ShiftAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class ShiftEntityTypeConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("shifts", tableBuilder =>
            tableBuilder.HasComment("Business master data shift definitions used by calendars, teams and execution planning."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Shift aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the shift.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the shift is valid.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Business unique shift code.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Shift display name.");
        builder.Property(x => x.StartsAt).HasColumnName("starts_at").IsRequired().HasComment("Local start time of the shift.");
        builder.Property(x => x.EndsAt).HasColumnName("ends_at").IsRequired().HasComment("Local end time of the shift.");
        builder.Property(x => x.CrossesMidnight).HasColumnName("crosses_midnight").IsRequired().HasComment("Flag that indicates the shift ends on the next local day.");
        builder.Property(x => x.PaidMinutes).HasColumnName("paid_minutes").IsRequired().HasComment("Paid or planned working minutes in the shift.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the shift from active use.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the shift was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the shift was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => x.Disabled);
    }
}
