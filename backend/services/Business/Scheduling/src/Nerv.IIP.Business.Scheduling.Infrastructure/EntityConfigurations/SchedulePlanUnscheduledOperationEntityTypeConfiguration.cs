using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class SchedulePlanUnscheduledOperationEntityTypeConfiguration : IEntityTypeConfiguration<SchedulePlanUnscheduledOperation>
{
    public void Configure(EntityTypeBuilder<SchedulePlanUnscheduledOperation> builder)
    {
        builder.ToTable("schedule_plan_unscheduled_operations", table => table.HasComment("BusinessScheduling operations that could not be assigned inside the plan horizon."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Schedule plan unscheduled operation row id.");
        builder.Property(x => x.SchedulePlanId).HasColumnName("schedule_plan_id").HasComment("Owning schedule plan aggregate id.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").HasMaxLength(96).IsRequired().HasComment("Public work order reference.");
        builder.Property(x => x.OperationId).HasColumnName("operation_id").HasMaxLength(96).IsRequired().HasComment("Public operation reference.");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasConversion<string>().HasMaxLength(64).HasComment("Reason code explaining why the operation was not scheduled.");
        builder.Property(x => x.Message).HasColumnName("message").HasMaxLength(512).IsRequired().HasComment("Human-readable unscheduled operation explanation.");
    }
}
