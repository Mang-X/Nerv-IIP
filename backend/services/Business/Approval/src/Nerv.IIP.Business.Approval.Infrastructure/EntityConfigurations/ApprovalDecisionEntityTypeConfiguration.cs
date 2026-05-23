using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;

namespace Nerv.IIP.Business.Approval.Infrastructure.EntityConfigurations;

public sealed class ApprovalDecisionEntityTypeConfiguration : IEntityTypeConfiguration<ApprovalDecision>
{
    public void Configure(EntityTypeBuilder<ApprovalDecision> builder)
    {
        builder.ToTable("approval_decisions", table => table.HasComment("Append-only approval decision facts recorded by actor and step."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Approval decision id.");
        builder.Property(x => x.ChainId).HasColumnName("chain_id").IsRequired().HasComment("Owning approval chain id.");
        builder.Property(x => x.StepId).HasColumnName("step_id").IsRequired().HasComment("Approval step id resolved by this decision.");
        builder.Property(x => x.StepNo).HasColumnName("step_no").IsRequired().HasComment("Approval step number resolved by this decision.");
        builder.Property(x => x.ActorType).HasColumnName("actor_type").IsRequired().HasMaxLength(50).HasComment("Actor reference type such as user, group or permission.");
        builder.Property(x => x.ActorRef).HasColumnName("actor_ref").IsRequired().HasMaxLength(150).HasComment("Public actor reference that made the decision.");
        builder.Property(x => x.Decision).HasColumnName("decision").IsRequired().HasMaxLength(50).HasComment("Decision action: approve, reject or return.");
        builder.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(1000).HasComment("Optional approver comment.");
        builder.Property(x => x.DecidedAtUtc).HasColumnName("decided_at_utc").IsRequired().HasComment("UTC time when the decision was recorded.");
        builder.HasIndex(x => new { x.ChainId, x.StepNo, x.ActorType, x.ActorRef }).IsUnique();
    }
}
