using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.EntityConfigurations;

public sealed class TemplateAssetRetirementReplayFenceEntityTypeConfiguration : IEntityTypeConfiguration<TemplateAssetRetirementReplayFence>
{
    public void Configure(EntityTypeBuilder<TemplateAssetRetirementReplayFence> builder)
    {
        builder.ToTable("template_asset_retirement_replay_fences", table => table.HasComment("Permanent minimal retirement facts; no requester, reason, proof or object content."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().HasComment("Original retirement decision identity, not a new generated identity.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization owning the retired asset.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment owning the retired asset.");
        builder.Property(x => x.TemplateFileId).HasColumnName("template_file_id").IsRequired().HasMaxLength(150).HasComment("File identity permanently prohibited from reuse.");
        builder.Property(x => x.IdempotencyKeyDigest).HasColumnName("idempotency_key_digest").IsRequired().HasMaxLength(64).HasComment("SHA-256 uppercase hex digest of the UTF-8 caller key; no original caller text.");
        builder.Property(x => x.ReplayUntilUtc).HasColumnName("replay_until_utc").HasComment("Frozen UTC boundary for replay-window-expired.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TemplateFileId }).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.IdempotencyKeyDigest }).IsUnique();
    }
}
