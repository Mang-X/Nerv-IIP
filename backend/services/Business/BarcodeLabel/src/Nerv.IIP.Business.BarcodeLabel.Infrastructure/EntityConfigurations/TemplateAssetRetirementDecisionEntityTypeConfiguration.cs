using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.TemplateAssetRetirementDecisionAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.EntityConfigurations;

public sealed class TemplateAssetRetirementDecisionEntityTypeConfiguration
    : IEntityTypeConfiguration<TemplateAssetRetirementDecision>
{
    public void Configure(EntityTypeBuilder<TemplateAssetRetirementDecision> builder)
    {
        builder.ToTable("template_asset_retirement_decisions", tableBuilder =>
            tableBuilder.HasComment("BarcodeLabel-owned decisions that permanently fence template assets from new use."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Retirement decision id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the decision.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id that owns the decision.");
        builder.Property(x => x.LabelTemplateId).HasColumnName("label_template_id").IsRequired().HasComment("BarcodeLabel template that owns the FileStorage asset.");
        builder.Property(x => x.TemplateCode).HasColumnName("template_code").IsRequired().HasMaxLength(100).HasComment("Frozen FileStorage owner id for the template asset.");
        builder.Property(x => x.TemplateFileId).HasColumnName("template_file_id").IsRequired().HasMaxLength(150).HasComment("FileStorage file id permanently fenced from new BarcodeLabel use.");
        builder.Property(x => x.TemplateAssetSha256).HasColumnName("template_asset_sha256").IsRequired().HasMaxLength(71).HasComment("Frozen canonical SHA-256 asset digest.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128).HasComment("Caller supplied idempotency key for decision creation.");
        builder.Property(x => x.RequesterSubject).HasColumnName("requester_subject").IsRequired().HasMaxLength(200).HasComment("Authenticated final-user subject captured for audit.");
        builder.Property(x => x.Permission).HasColumnName("permission").IsRequired().HasMaxLength(150).HasComment("Permission proven when the decision was created.");
        builder.Property(x => x.Reason).HasColumnName("reason").IsRequired().HasMaxLength(500).HasComment("Final-user supplied retirement reason.");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired().HasMaxLength(150).HasComment("Safe upstream audit correlation id.");
        builder.Property(x => x.ReferenceResult).HasColumnName("reference_result").IsRequired().HasMaxLength(30).HasComment("Frozen BarcodeLabel reference evaluation result.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("Retirement decision execution status.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the decision was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the decision was last changed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_template_asset_retirement_decisions_idempotency");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.TemplateFileId })
            .IsUnique()
            .HasDatabaseName("UX_template_asset_retirement_decisions_file");
        builder.HasIndex(x => x.LabelTemplateId);
    }
}
