using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

internal sealed class ToolingAuditEntryEntityTypeConfiguration : IEntityTypeConfiguration<ToolingAuditEntry>
{
    public void Configure(EntityTypeBuilder<ToolingAuditEntry> builder)
    {
        builder.ToTable(
            "tooling_audit_entries",
            table =>
            {
                table.HasComment("Append-only audit facts for governed tooling register, status, and usage operations.");
                table.HasCheckConstraint(
                    "ck_tooling_audit_operation_kind",
                    "\"OperationKind\" IN ('tooling-register', 'tooling-status', 'tooling-usage')");
                table.HasCheckConstraint(
                    "ck_tooling_audit_summary_shape",
                    "(\"OperationKind\" = 'tooling-register' AND \"BeforeStatus\" IS NULL AND \"AfterStatus\" = 'Available' AND \"BeforeUsageCount\" IS NULL AND \"AfterUsageCount\" = 0 AND \"UsageDelta\" IS NULL AND \"Reason\" IS NULL) OR " +
                    "(\"OperationKind\" = 'tooling-status' AND \"BeforeStatus\" IS NOT NULL AND \"AfterStatus\" IS NOT NULL AND \"BeforeUsageCount\" IS NULL AND \"AfterUsageCount\" IS NULL AND \"UsageDelta\" IS NULL AND \"Reason\" IS NOT NULL) OR " +
                    "(\"OperationKind\" = 'tooling-usage' AND \"BeforeStatus\" IS NULL AND \"AfterStatus\" IS NULL AND \"BeforeUsageCount\" >= 0 AND \"AfterUsageCount\" = \"BeforeUsageCount\" + \"UsageDelta\" AND \"UsageDelta\" > 0 AND \"Reason\" IS NULL)");
            });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Tooling audit entry identifier.");
        builder.Property(x => x.OrganizationId).HasMaxLength(64).IsRequired().HasComment("Organization scope.");
        builder.Property(x => x.EnvironmentId).HasMaxLength(64).IsRequired().HasComment("Environment scope.");
        builder.Property(x => x.OperationKind).HasMaxLength(32).IsRequired().HasComment("Governed operation: tooling-register, tooling-status, or tooling-usage.");
        builder.Property(x => x.ToolingAssetId).HasMaxLength(160).IsRequired().HasComment("Persistent tooling asset identifier without a cross-table foreign key.");
        builder.Property(x => x.ToolingCode).HasMaxLength(64).IsRequired().HasComment("Canonical tooling code targeted by the operation.");
        builder.Property(x => x.ActorId).HasMaxLength(200).IsRequired().HasComment("Trusted authenticated principal forwarded by the authorized caller.");
        builder.Property(x => x.CorrelationId).HasMaxLength(200).IsRequired().HasComment("Request correlation identity.");
        builder.Property(x => x.CausationId).HasMaxLength(200).IsRequired().HasComment("Request causation identity.");
        builder.Property(x => x.OperationId).HasMaxLength(200).IsRequired().HasComment("Stable idempotency identity for the governed operation.");
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64).IsFixedLength().IsRequired().HasComment("SHA-256 fingerprint of the canonical operation, target, and whitelisted request summary.");
        builder.Property(x => x.BeforeStatus).HasConversion<string>().HasMaxLength(32).HasComment("Tooling status before a status operation; otherwise null.");
        builder.Property(x => x.AfterStatus).HasConversion<string>().HasMaxLength(32).HasComment("Tooling status after register or status operation; otherwise null.");
        builder.Property(x => x.BeforeUsageCount).HasComment("Usage count before a usage operation; otherwise null.");
        builder.Property(x => x.AfterUsageCount).HasComment("Usage count after register or usage operation; otherwise null.");
        builder.Property(x => x.UsageDelta).HasComment("Positive usage increment for a usage operation; otherwise null.");
        builder.Property(x => x.Reason).HasMaxLength(500).HasComment("Normalized status change reason; otherwise null.");
        builder.Property(x => x.OccurredAtUtc).IsRequired().HasComment("UTC timestamp when the governed operation occurred.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationId })
            .IsUnique()
            .HasDatabaseName("ux_tooling_audit_operation");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ToolingCode, x.OccurredAtUtc })
            .HasDatabaseName("ix_tooling_audit_target_time");
    }
}
