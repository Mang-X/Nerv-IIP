using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class AndonCallEntityTypeConfiguration : IEntityTypeConfiguration<AndonCall>
{
    public void Configure(EntityTypeBuilder<AndonCall> builder)
    {
        builder.ToTable("andon_calls", table => table.HasComment("MES exception calls with immutable first response and independent single escalation facts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Andon call aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization owning the call.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment owning the call.");
        builder.Property(x => x.RaiseIntentKey).HasColumnName("raise_intent_key").IsRequired().HasMaxLength(150).HasComment("Creation intent identity unique within organization and environment; retained after closure.");
        builder.Property(x => x.Category).HasColumnName("category").HasConversion<string>().IsRequired().HasMaxLength(30).HasComment("MaterialShortage, Equipment, Quality or Process call category.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("Source MES work order business id frozen when raised.");
        builder.Property(x => x.OperationTaskIdValue).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("Source MES operation task business id frozen when raised.");
        builder.Property(x => x.WorkCenterId).HasColumnName("work_center_id").IsRequired().HasMaxLength(100).HasComment("Source work center public id frozen when raised.");
        builder.Property(x => x.CallerId).HasColumnName("caller_id").IsRequired().HasMaxLength(100).HasComment("IAM principal id that raised the call.");
        builder.Property(x => x.RaisedAtUtc).HasColumnName("raised_at_utc").IsRequired().HasComment("UTC instant when the call was raised.");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().IsRequired().HasMaxLength(20).HasComment("Open, Claimed or Closed lifecycle; escalation does not change it.");
        builder.Property(x => x.ResponderId).HasColumnName("responder_id").HasMaxLength(100).HasComment("First claimant IAM principal id; only this person may close the call.");
        builder.Property(x => x.ClaimIntentKey).HasColumnName("claim_intent_key").HasMaxLength(150).HasComment("Accepted claim intent retained for replay after closure.");
        builder.Property(x => x.FirstRespondedAtUtc).HasColumnName("first_responded_at_utc").HasComment("First successful claim UTC instant; null means no response yet, not zero duration.");
        builder.Property(x => x.CloseIntentKey).HasColumnName("close_intent_key").HasMaxLength(150).HasComment("Accepted close intent retained for replay.");
        builder.Property(x => x.ClosedAtUtc).HasColumnName("closed_at_utc").HasComment("UTC instant when the responder closed the call.");
        builder.Property(x => x.EscalatedAtUtc).HasColumnName("escalated_at_utc").HasComment("Single unclaimed-timeout escalation UTC instant, independent of lifecycle.");
        builder.Property(x => x.EscalationRecipientId).HasColumnName("escalation_recipient_id").HasMaxLength(100).HasComment("Configured escalation recipient IAM principal id frozen at escalation.");
        builder.Property(x => x.RowVersion).HasColumnName("row_version")
            .HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version protecting lifecycle and escalation writes.");
        builder.Ignore(x => x.ResponseDuration);
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RaiseIntentKey }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Category, x.Status, x.EscalatedAtUtc, x.RaisedAtUtc });
    }
}
