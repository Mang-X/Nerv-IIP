using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;

namespace Nerv.IIP.Business.Approval.Infrastructure.EntityConfigurations;

public sealed class ApprovalStepEntityTypeConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    public void Configure(EntityTypeBuilder<ApprovalStep> builder)
    {
        builder.ToTable("approval_steps", table => table.HasComment("Runtime approval steps copied from the active template when a chain starts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Approval step id.");
        builder.Property(x => x.ChainId).HasColumnName("chain_id").IsRequired().HasComment("Owning approval chain id.");
        builder.Property(x => x.StepNo).HasColumnName("step_no").IsRequired().HasComment("Ordered approval step number; equal numbers are resolved as a parallel group.");
        builder.Property(x => x.StepName).HasColumnName("step_name").IsRequired().HasMaxLength(100).HasComment("Step name copied from the template.");
        builder.Property(x => x.ParallelGroupKey).HasColumnName("parallel_group_key").HasMaxLength(100).HasComment("Optional explicit parallel group key copied from the template.");
        builder.Property(x => x.CompletionPolicy).HasColumnName("completion_policy").IsRequired().HasMaxLength(20).HasComment("Runtime completion policy for the step number group: all or any.");
        builder.Property(x => x.ConditionExpression).HasColumnName("condition_expression").HasMaxLength(200).HasComment("Condition that caused this runtime step to be included.");
        builder.Property(x => x.ApproverType).HasColumnName("approver_type").IsRequired().HasMaxLength(50).HasComment("Approver reference type copied from the template.");
        builder.Property(x => x.ApproverRef).HasColumnName("approver_ref").IsRequired().HasMaxLength(150).HasComment("Public approver reference copied from the template.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Approval step status: pending, approved, rejected or returned.");
        builder.Property(x => x.DueAtUtc).HasColumnName("due_at_utc").HasComment("Optional UTC due time for this step.");
        builder.Property(x => x.ResolvedByActorType).HasColumnName("resolved_by_actor_type").HasMaxLength(50).HasComment("Actor type that resolved this step.");
        builder.Property(x => x.ResolvedByActorRef).HasColumnName("resolved_by_actor_ref").HasMaxLength(150).HasComment("Actor reference that resolved this step.");
        builder.Property(x => x.ResolvedDecision).HasColumnName("resolved_decision").HasMaxLength(50).HasComment("Decision action that resolved this step.");
        builder.Property(x => x.ResolvedComment).HasColumnName("resolved_comment").HasMaxLength(1000).HasComment("Optional approver comment captured with the decision.");
        builder.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc").HasComment("UTC time when this step was resolved.");
        builder.Property(x => x.OverdueNotifiedAtUtc).HasColumnName("overdue_notified_at_utc").HasComment("UTC time when the overdue event was emitted for this step.");
        builder.HasIndex(x => new { x.ChainId, x.StepNo, x.ApproverType, x.ApproverRef }).IsUnique();
        builder.HasIndex(x => new { x.ApproverType, x.ApproverRef, x.Status, x.DueAtUtc });
    }
}
