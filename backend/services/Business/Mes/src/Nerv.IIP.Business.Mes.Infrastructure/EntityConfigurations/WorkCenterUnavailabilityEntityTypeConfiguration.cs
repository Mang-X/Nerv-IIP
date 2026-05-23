using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class WorkCenterUnavailabilityEntityTypeConfiguration : IEntityTypeConfiguration<WorkCenterUnavailability>
{
    public void Configure(EntityTypeBuilder<WorkCenterUnavailability> builder)
    {
        builder.ToTable("work_center_unavailabilities", tableBuilder =>
            tableBuilder.HasComment("MES scheduling constraint facts for unavailable work centers from maintenance or manual inputs."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Work center unavailability aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").HasMaxLength(100).HasComment("Organization tenant id; null means the scheduling constraint is global.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").HasMaxLength(100).HasComment("Environment id; null means the scheduling constraint is global.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("MasterData work center public id unavailable for scheduling.");
        builder.Property(x => x.FromUtc).HasColumnName("from_utc").IsRequired().HasComment("UTC start of the unavailable window.");
        builder.Property(x => x.ToUtc).HasColumnName("to_utc").HasComment("UTC end of the unavailable window; null means still unavailable.");
        builder.Property(x => x.Reason).HasColumnName("reason").IsRequired().HasMaxLength(200).HasComment("Business reason for the scheduling constraint.");
        builder.Property(x => x.DeviceAssetId).HasColumnName("device_asset_id").HasMaxLength(100).HasComment("Maintenance device asset public id that produced the unavailable window, when applicable.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.WorkCenterId, x.FromUtc, x.ToUtc })
            .HasDatabaseName("ix_wc_unavailability_scope_center_window");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DeviceAssetId, x.ToUtc })
            .HasDatabaseName("ix_wc_unavailability_scope_asset_open");
    }
}
