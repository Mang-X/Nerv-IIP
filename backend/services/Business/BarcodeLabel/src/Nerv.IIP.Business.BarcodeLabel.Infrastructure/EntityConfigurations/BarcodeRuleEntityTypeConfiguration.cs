using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.EntityConfigurations;

public sealed class BarcodeRuleEntityTypeConfiguration : IEntityTypeConfiguration<BarcodeRule>
{
    public void Configure(EntityTypeBuilder<BarcodeRule> builder)
    {
        builder.ToTable("barcode_rules", tableBuilder =>
            tableBuilder.HasComment("Barcode rule facts for deterministic label value generation."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Barcode rule aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the barcode rule.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the barcode rule applies.");
        builder.Property(x => x.RuleCode).HasColumnName("rule_code").IsRequired().HasMaxLength(100).HasComment("Business barcode rule code unique in an organization and environment.");
        builder.Property(x => x.BarcodeType).HasColumnName("barcode_type").IsRequired().HasMaxLength(50).HasComment("Barcode symbology such as code128, qr or datamatrix.");
        builder.Property(x => x.Prefix).HasColumnName("prefix").IsRequired().HasMaxLength(40).HasComment("Barcode prefix included in generated label values.");
        builder.Property(x => x.Length).HasColumnName("length").IsRequired().HasComment("Maximum generated barcode length.");
        builder.Property(x => x.ChecksumRule).HasColumnName("checksum_rule").IsRequired().HasMaxLength(50).HasComment("Checksum policy name for generated barcode values.");
        builder.PrimitiveCollection(x => x.AllowedSourceDocumentTypes).HasColumnName("allowed_source_document_types").HasComment("Allowed source document types for this barcode rule.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(30).HasComment("Rule lifecycle status: active or inactive.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the rule was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the rule was last changed.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RuleCode }).IsUnique();
    }
}
