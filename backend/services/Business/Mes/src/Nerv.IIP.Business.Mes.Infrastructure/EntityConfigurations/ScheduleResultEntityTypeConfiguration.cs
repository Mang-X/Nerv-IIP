using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class ScheduleResultEntityTypeConfiguration : IEntityTypeConfiguration<ScheduleResult>
{
    public void Configure(EntityTypeBuilder<ScheduleResult> builder)
    {
        builder.ToTable("schedule_results", tableBuilder =>
            tableBuilder.HasComment("MES schedule result facts produced by the deterministic rule scheduler."));
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.Assignments);
        builder.Ignore(x => x.AffectedWorkOrderIds);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Schedule result aggregate id.");
        builder.Property(x => x.ScheduleVersion).HasColumnName("schedule_version").IsRequired().HasComment("Monotonic schedule version preserving current MES behavior.");
        builder.Property(x => x.Trigger).HasColumnName("trigger").HasConversion<string>().IsRequired().HasMaxLength(30).HasComment("Business trigger that caused the schedule run.");
        builder.Property(x => x.ScheduledAtUtc).HasColumnName("scheduled_at_utc").IsRequired().HasComment("UTC time requested for the schedule run.");
        builder.Property(x => x.AssignmentsJson).HasColumnName("assignments_json").IsRequired().HasColumnType("text").HasComment("JSON schedule assignments produced by MES scheduler; producer is MES, consumers are MES/WMS/read APIs, compatibility is append-only fields.");
        builder.Property(x => x.AffectedWorkOrderIdsJson).HasColumnName("affected_work_order_ids_json").IsRequired().HasColumnType("text").HasComment("JSON affected work order id list produced by MES scheduler; producer is MES, consumers are MES/WMS/read APIs, compatibility is append-only fields.");
        builder.HasIndex(x => x.ScheduleVersion).IsUnique();
        builder.HasIndex(x => new { x.Trigger, x.ScheduledAtUtc });
    }
}
