using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;

namespace Nerv.IIP.Business.Approval.Infrastructure.EntityConfigurations;

public sealed class ApprovalTemplateStepEntityTypeConfiguration : IEntityTypeConfiguration<ApprovalTemplateStep>
{
    public void Configure(EntityTypeBuilder<ApprovalTemplateStep> builder)
    {
        builder.ToTable("approval_template_steps", table => table.HasComment("Ordered approver definitions owned by an approval template."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Approval template step id.");
        builder.Property(x => x.TemplateId).HasColumnName("template_id").IsRequired().HasComment("Owning approval template id.");
        builder.Property(x => x.StepNo).HasColumnName("step_no").IsRequired().HasComment("Ordered approval step number; equal numbers form an explicit parallel group.");
        builder.Property(x => x.StepName).HasColumnName("step_name").IsRequired().HasMaxLength(100).HasComment("Human-readable step name.");
        builder.Property(x => x.ParallelGroupKey).HasColumnName("parallel_group_key").HasMaxLength(100).HasComment("Optional explicit parallel group key for steps at the same step number.");
        builder.Property(x => x.ApproverType).HasColumnName("approver_type").IsRequired().HasMaxLength(50).HasComment("Approver reference type such as user, group or permission.");
        builder.Property(x => x.ApproverRef).HasColumnName("approver_ref").IsRequired().HasMaxLength(150).HasComment("Public approver reference; IAM facts are not copied.");
        builder.Property(x => x.DueInHours).HasColumnName("due_in_hours").HasComment("Optional due interval in hours after chain start.");
        builder.HasIndex(x => new { x.TemplateId, x.StepNo, x.ApproverType, x.ApproverRef }).IsUnique();
    }
}
