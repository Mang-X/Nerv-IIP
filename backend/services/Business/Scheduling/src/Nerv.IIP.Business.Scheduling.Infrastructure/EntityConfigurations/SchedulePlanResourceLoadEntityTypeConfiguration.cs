using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class SchedulePlanResourceLoadEntityTypeConfiguration : IEntityTypeConfiguration<SchedulePlanResourceLoad>
{
    public void Configure(EntityTypeBuilder<SchedulePlanResourceLoad> builder)
    {
        builder.ToTable("schedule_plan_resource_loads", table => table.HasComment("BusinessScheduling resource load windows for a schedule plan."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Schedule plan resource load row id.");
        builder.Property(x => x.SchedulePlanId).HasColumnName("schedule_plan_id").HasComment("Owning schedule plan aggregate id.");
        builder.Property(x => x.ResourceId).HasColumnName("resource_id").HasMaxLength(96).IsRequired().HasComment("Resource id for the load window.");
        builder.Property(x => x.WindowStartUtc).HasColumnName("window_start_utc").HasComment("Load window start timestamp in UTC.");
        builder.Property(x => x.WindowEndUtc).HasColumnName("window_end_utc").HasComment("Load window end timestamp in UTC.");
        builder.Property(x => x.AssignedMinutes).HasColumnName("assigned_minutes").HasComment("Resource occupied minutes in the window, including processing plus setup/changeover time.");
        builder.Property(x => x.AvailableMinutes).HasColumnName("available_minutes").HasComment("Available capacity minutes in the window.");
        builder.Property(x => x.Utilization).HasColumnName("utilization").HasPrecision(18, 6).HasComment("Assigned minutes divided by available minutes.");
    }
}
