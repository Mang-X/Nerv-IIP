using Nerv.IIP.Business.Mes.Infrastructure.IntegrationEvents;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class ScheduleReleaseWatermarkEntityTypeConfiguration : IEntityTypeConfiguration<ScheduleReleaseWatermark>
{
    public void Configure(EntityTypeBuilder<ScheduleReleaseWatermark> builder)
    {
        builder.ToTable("schedule_release_watermarks", tableBuilder =>
            tableBuilder.HasComment("Highest revoked Scheduling release revision consumed by MES for each business scope."));
        builder.HasKey(x => new { x.OrganizationId, x.EnvironmentId })
            .HasName("pk_schedule_release_watermarks");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(100).HasComment("Organization tenant id.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(100).HasComment("Environment id for the scheduling scope.");
        builder.Property(x => x.RevokedPlanId).HasColumnName("revoked_plan_id").HasMaxLength(100).IsRequired().HasComment("Plan id owning the highest revoked release revision consumed in this scope.");
        builder.Property(x => x.RevokedReleaseRevision).HasColumnName("revoked_release_revision").IsRequired().HasComment("Highest revoked monotonic Scheduling release revision consumed in this scope.");
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc").IsRequired().HasComment("UTC occurrence time of the highest consumed revocation.");
    }
}
