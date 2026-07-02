using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

public sealed class CodeRuleEntityTypeConfiguration : IEntityTypeConfiguration<CodeRule>
{
    public void Configure(EntityTypeBuilder<CodeRule> builder)
    {
        builder.ToTable("code_rules", table =>
            table.HasComment("Business master data code generation rules available to the coding engine."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Code rule aggregate id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the code rule.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the code rule is valid.");
        builder.Property(x => x.RuleKey).HasColumnName("rule_key").IsRequired().HasMaxLength(100).HasComment("Stable code rule key used by application create commands.");
        builder.Property(x => x.DisplayName).HasColumnName("display_name").IsRequired().HasMaxLength(200).HasComment("Human-readable code rule name.");
        builder.Property(x => x.AppliesTo).HasColumnName("applies_to").IsRequired().HasMaxLength(200).HasComment("Resource or document type governed by the code rule.");
        builder.Property(x => x.Scope).HasColumnName("scope").IsRequired().HasComment("Bit flags describing the allocation scope dimensions.");
        builder.Property(x => x.SegmentsJson).HasColumnName("segments").HasColumnType("jsonb").IsRequired().HasComment("Ordered code rule segment definition JSON.");
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasComment("Whether this code rule is active for new allocations.");
        builder.Property(x => x.Version).HasColumnName("version").IsRequired().HasComment("Code rule definition version.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when the code rule was created.");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().HasComment("UTC time when the code rule was last updated.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RuleKey }).IsUnique().HasDatabaseName("ux_code_rules_scope");
    }
}

public sealed class CodeRuleVersionEntityTypeConfiguration : IEntityTypeConfiguration<CodeRuleVersion>
{
    public void Configure(EntityTypeBuilder<CodeRuleVersion> builder)
    {
        builder.ToTable("code_rule_versions", table =>
            table.HasComment("Versioned audit records for business master data code rule configuration changes."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Code rule version record id.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant id that owns the code rule version.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment id where the code rule version is valid.");
        builder.Property(x => x.RuleKey).HasColumnName("rule_key").IsRequired().HasMaxLength(100).HasComment("Stable code rule key governed by this version.");
        builder.Property(x => x.DisplayName).HasColumnName("display_name").IsRequired().HasMaxLength(200).HasComment("Human-readable code rule name for this version.");
        builder.Property(x => x.AppliesTo).HasColumnName("applies_to").IsRequired().HasMaxLength(200).HasComment("Resource or document type governed by this version.");
        builder.Property(x => x.Scope).HasColumnName("scope").IsRequired().HasComment("Bit flags describing allocation scope dimensions for this version.");
        builder.Property(x => x.SegmentsJson).HasColumnName("segments").HasColumnType("jsonb").IsRequired().HasComment("Ordered code rule segment definition JSON for this version.");
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasComment("Whether this version allows new allocations when effective.");
        builder.Property(x => x.Version).HasColumnName("version").IsRequired().HasComment("Monotonic version number within organization, environment and rule key.");
        builder.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(50).HasComment("Governance status for this version, such as active or scheduled.");
        builder.Property(x => x.EffectiveFromUtc).HasColumnName("effective_from_utc").IsRequired().HasComment("UTC instant when this version may become effective for new allocations.");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").IsRequired().HasMaxLength(100).HasComment("Operator or system principal that created this version.");
        builder.Property(x => x.ChangeReason).HasColumnName("change_reason").IsRequired().HasMaxLength(500).HasComment("Audited reason for the code rule configuration change.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired().HasComment("UTC time when this version record was created.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RuleKey, x.Version }).IsUnique().HasDatabaseName("ux_code_rule_versions_scope_version");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RuleKey, x.EffectiveFromUtc }).HasDatabaseName("ix_code_rule_versions_effective");
    }
}
