using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.LifecycleAuditAggregate;

namespace Nerv.IIP.Business.MasterData.Infrastructure.EntityConfigurations;

internal sealed class MasterDataLifecycleAuditEntryEntityTypeConfiguration : IEntityTypeConfiguration<MasterDataLifecycleAuditEntry>
{
    public void Configure(EntityTypeBuilder<MasterDataLifecycleAuditEntry> builder)
    {
        builder.ToTable("master_data_lifecycle_audit", table => table.HasComment("Durable audit trail for master-data lifecycle state changes."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseGuidVersion7ValueGenerator().HasComment("Lifecycle audit entry identifier.");
        builder.Property(x => x.OrganizationId).HasMaxLength(64).IsRequired().HasComment("Organization scope.");
        builder.Property(x => x.EnvironmentId).HasMaxLength(64).IsRequired().HasComment("Environment scope.");
        builder.Property(x => x.ResourceType).HasMaxLength(64).IsRequired().HasComment("Master-data resource type.");
        builder.Property(x => x.ResourceId).HasMaxLength(160).IsRequired().HasComment("Persistent resource identifier.");
        builder.Property(x => x.ResourceCode).HasMaxLength(160).IsRequired().HasComment("Stable resource code or public identifier.");
        builder.Property(x => x.ResourceIdentity).HasMaxLength(300).IsRequired().HasComment("Canonical resource identity including composite-key qualifiers.");
        builder.Property(x => x.TargetEnabled).IsRequired().HasComment("Lifecycle state requested by the operation.");
        builder.Property(x => x.ActorId).HasMaxLength(200).IsRequired().HasComment("Trusted authenticated principal that requested the change.");
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired().HasComment("Required normalized lifecycle change reason.");
        builder.Property(x => x.OperationId).HasMaxLength(200).IsRequired().HasComment("Correlation or idempotency identity for the operation.");
        builder.Property(x => x.OccurredAtUtc).IsRequired().HasComment("UTC timestamp when the lifecycle change occurred.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationId }).IsUnique().HasDatabaseName("ux_master_data_lifecycle_audit_operation");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ResourceType, x.ResourceCode, x.OccurredAtUtc }).HasDatabaseName("ix_master_data_lifecycle_audit_resource");
    }
}
