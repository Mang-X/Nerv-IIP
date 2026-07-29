using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ScopeContextAuditAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

internal sealed class MasterDataScopeContextAuditEntryEntityTypeConfiguration
    : IEntityTypeConfiguration<MasterDataScopeContextAuditEntry>
{
    public void Configure(EntityTypeBuilder<MasterDataScopeContextAuditEntry> builder)
    {
        builder.ToTable(
            "master_data_scope_context_audit",
            table => table.HasComment("Durable audit trail for master-data changes that alter principal scope candidates."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Scope context audit entry identifier.");
        builder.Property(x => x.OrganizationId).HasMaxLength(64).IsRequired().HasComment("Organization scope.");
        builder.Property(x => x.EnvironmentId).HasMaxLength(64).IsRequired().HasComment("Environment scope.");
        builder.Property(x => x.OperationKind).HasMaxLength(64).IsRequired().HasComment("Stable scope-context mutation kind.");
        builder.Property(x => x.ResourceType).HasMaxLength(64).IsRequired().HasComment("Master-data resource type.");
        builder.Property(x => x.ResourceId).HasMaxLength(160).IsRequired().HasComment("Persistent resource identifier.");
        builder.Property(x => x.ResourceCode).HasMaxLength(160).IsRequired().HasComment("Stable resource code.");
        builder.Property(x => x.ResourceIdentity).HasMaxLength(300).IsRequired().HasComment("Canonical resource identity.");
        builder.Property(x => x.ActorId).HasMaxLength(200).IsRequired().HasComment("Trusted authenticated principal.");
        builder.Property(x => x.CorrelationId).HasMaxLength(200).IsRequired().HasComment("Request correlation identity.");
        builder.Property(x => x.CausationId).HasMaxLength(200).IsRequired().HasComment("Request causation identity.");
        builder.Property(x => x.OperationId).HasMaxLength(200).IsRequired().HasComment("Idempotency key or correlation identity.");
        builder.Property(x => x.BeforeJson).HasColumnType("jsonb").IsRequired().HasComment("Canonical authorization-relevant state before the mutation.");
        builder.Property(x => x.AfterJson).HasColumnType("jsonb").IsRequired().HasComment("Canonical authorization-relevant state after the mutation.");
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired().HasComment("Normalized reason or stable system reason code.");
        builder.Property(x => x.OccurredAtUtc).IsRequired().HasComment("UTC timestamp when the mutation occurred.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationId })
            .HasDatabaseName("ix_master_data_scope_context_audit_operation");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ResourceType, x.ResourceCode, x.OccurredAtUtc })
            .HasDatabaseName("ix_master_data_scope_context_audit_resource");
    }
}
