using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class SchedulePlanConflictEntityTypeConfiguration : IEntityTypeConfiguration<SchedulePlanConflict>
{
    public void Configure(EntityTypeBuilder<SchedulePlanConflict> builder)
    {
        builder.ToTable("schedule_plan_conflicts", table => table.HasComment("BusinessScheduling conflicts detected while generating a schedule plan."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Schedule plan conflict row id.");
        builder.Property(x => x.SchedulePlanId).HasColumnName("schedule_plan_id").HasComment("Owning schedule plan aggregate id.");
        builder.Property(x => x.ConflictPublicId).HasColumnName("conflict_id").HasMaxLength(128).IsRequired().HasComment("Public conflict id.");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasConversion<string>().HasMaxLength(64).HasComment("Conflict reason code.");
        builder.Property(x => x.Severity).HasColumnName("severity").HasConversion<string>().HasMaxLength(32).HasComment("Conflict severity.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").HasMaxLength(96).HasComment("Affected work order reference.");
        builder.Property(x => x.OperationId).HasColumnName("operation_id").HasMaxLength(96).HasComment("Affected operation reference.");
        builder.Property(x => x.ResourceId).HasColumnName("resource_id").HasMaxLength(96).HasComment("Affected resource reference.");
        builder.Property(x => x.Message).HasColumnName("message").HasMaxLength(512).IsRequired().HasComment("Human-readable conflict explanation.");
    }
}
