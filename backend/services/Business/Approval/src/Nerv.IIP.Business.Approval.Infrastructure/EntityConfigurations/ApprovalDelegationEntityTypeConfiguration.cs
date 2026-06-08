using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalDelegationAggregate;

namespace Nerv.IIP.Business.Approval.Infrastructure.EntityConfigurations;

public sealed class ApprovalDelegationEntityTypeConfiguration : IEntityTypeConfiguration<ApprovalDelegation>
{
    public void Configure(EntityTypeBuilder<ApprovalDelegation> builder)
    {
        builder.ToTable("approval_delegations", table => table.HasComment("Business approval actor delegation authorizations."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Approval delegation id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the delegation.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the delegation applies.");
        builder.Property(x => x.DelegatorActorType).HasColumnName("delegator_actor_type").IsRequired().HasMaxLength(50).HasComment("Delegating actor type such as user, group or permission.");
        builder.Property(x => x.DelegatorActorRef).HasColumnName("delegator_actor_ref").IsRequired().HasMaxLength(150).HasComment("Public actor reference that delegates approval authority.");
        builder.Property(x => x.DelegateActorType).HasColumnName("delegate_actor_type").IsRequired().HasMaxLength(50).HasComment("Delegate actor type such as user, group or permission.");
        builder.Property(x => x.DelegateActorRef).HasColumnName("delegate_actor_ref").IsRequired().HasMaxLength(150).HasComment("Public actor reference that receives delegated approval authority.");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(100).HasComment("Optional document type scope for this delegation.");
        builder.Property(x => x.EffectiveFromUtc).HasColumnName("effective_from_utc").IsRequired().HasComment("UTC time when the delegation starts.");
        builder.Property(x => x.EffectiveToUtc).HasColumnName("effective_to_utc").IsRequired().HasComment("UTC time when the delegation expires.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Delegation status: active or revoked.");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).HasComment("Optional reason recorded when creating the delegation.");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired().HasMaxLength(150).HasComment("Public actor reference that created the delegation.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the delegation was created.");
        builder.Property(x => x.RevokedBy).HasColumnName("revoked_by").HasMaxLength(150).HasComment("Public actor reference that revoked the delegation.");
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc").HasComment("UTC time when the delegation was revoked.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status, x.DelegateActorRef });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DelegatorActorRef, x.DocumentType });
    }
}
