using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.StationAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class StationEntityTypeConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> builder)
    {
        builder.ToTable("stations", tableBuilder =>
            tableBuilder.HasComment("Business master data stations (work units) under a production line."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Station aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the station.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the station is valid.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(100).HasComment("Business unique station code.");
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200).HasComment("Station display name.");
        builder.Property(x => x.LineCode).HasColumnName("line_code").IsRequired().HasMaxLength(100).HasComment("Parent production line code; site and workshop are inherited from the line.");
        builder.Property(x => x.WorkCenterCode).HasColumnName("work_center_code").HasMaxLength(100).HasComment("Optional capacity/cost work center association; not a hierarchy parent.");
        builder.Property(x => x.Disabled).HasColumnName("disabled").IsRequired().HasComment("Disabled flag that hides the station from new device and execution references.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the station was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the station was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.LineCode, x.Disabled });
        builder.HasIndex(x => new { x.WorkCenterCode, x.Disabled });
    }
}
