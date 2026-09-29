using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class SchedulePlanAssignmentEntityTypeConfiguration : IEntityTypeConfiguration<SchedulePlanAssignment>
{
    public void Configure(EntityTypeBuilder<SchedulePlanAssignment> builder)
    {
        builder.ToTable("schedule_plan_assignments", table => table.HasComment("BusinessScheduling operation assignments in a schedule plan."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Schedule plan assignment row id.");
        builder.Property(x => x.SchedulePlanId).HasColumnName("schedule_plan_id").HasComment("Owning schedule plan aggregate id.");
        builder.Property(x => x.AssignmentId).HasColumnName("assignment_id").HasMaxLength(128).IsRequired().HasComment("Public assignment id.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").HasMaxLength(96).IsRequired().HasComment("Public work order reference.");
        builder.Property(x => x.OperationId).HasColumnName("operation_id").HasMaxLength(96).IsRequired().HasComment("Public operation reference.");
        builder.Property(x => x.OperationSequence).HasColumnName("operation_sequence").HasComment("Operation sequence within the work order route.");
        builder.Property(x => x.ResourceId).HasColumnName("resource_id").HasMaxLength(96).IsRequired().HasComment("Assigned resource id.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").HasMaxLength(96).IsRequired().HasComment("Assigned work center id.");
        builder.Property(x => x.StandardOperationCode).HasColumnName("standard_operation_code").HasMaxLength(100).HasComment("ProductEngineering standard operation code used by MES to resolve current SOP or electronic work instructions.");
        builder.Property(x => x.StartUtc).HasColumnName("start_utc").HasComment("Assignment start timestamp in UTC.");
        builder.Property(x => x.EndUtc).HasColumnName("end_utc").HasComment("Assignment end timestamp in UTC.");
        builder.Property(x => x.IsLocked).HasColumnName("is_locked").HasComment("Whether this assignment came from a locked input.");
        builder.Property(x => x.ExplanationCode).HasColumnName("explanation_code").HasMaxLength(96).IsRequired().HasComment("Scheduling explanation code.");
        builder.HasIndex(x => new { x.SchedulePlanId, x.AssignmentId }).IsUnique();
        builder.HasMany(x => x.Segments).WithOne().HasForeignKey(x => x.SchedulePlanAssignmentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Segments).AutoInclude();
    }
}

public sealed class SchedulePlanAssignmentSegmentEntityTypeConfiguration : IEntityTypeConfiguration<SchedulePlanAssignmentSegment>
{
    public void Configure(EntityTypeBuilder<SchedulePlanAssignmentSegment> builder)
    {
        builder.ToTable("schedule_plan_assignment_segments", table => table.HasComment("Actual production intervals within an interruptible schedule assignment."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Schedule assignment segment row id.");
        builder.Property(x => x.SchedulePlanAssignmentId).HasColumnName("schedule_plan_assignment_id").HasComment("Owning schedule assignment row id.");
        builder.Property(x => x.SegmentIndex).HasColumnName("segment_index").HasComment("Zero-based order of the production interval within its assignment.");
        builder.Property(x => x.StartUtc).HasColumnName("start_utc").HasComment("Actual segment start timestamp in UTC.");
        builder.Property(x => x.EndUtc).HasColumnName("end_utc").HasComment("Actual segment end timestamp in UTC.");
        builder.HasIndex(x => new { x.SchedulePlanAssignmentId, x.SegmentIndex }).IsUnique();
    }
}
