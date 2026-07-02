using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Ops.Infrastructure.EntityConfigurations;

public sealed class AuditRecordEntityTypeConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("audit_records", table => table.HasComment("Ops audit records for operation task lifecycle events and user-visible traceability."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new AuditRecordId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Audit record identifier.");
        builder.Property(x => x.OperationTaskId)
            .HasConversion(x => x.Id, x => new OperationTaskId(x))
            .HasMaxLength(64)
            .HasComment("Operation task identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(64).HasComment("Organization scope for the Ops audit chain.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(64).HasComment("Environment scope for the Ops audit chain.");
        builder.Property(x => x.SequenceNo).IsRequired().HasComment("Monotonic Ops audit chain sequence number within organization and environment scope.");
        builder.Property(x => x.PreviousIntegrityHash).IsRequired().HasMaxLength(80).HasComment("Previous audit record integrity hash in the organization and environment chain; empty for genesis.");
        builder.Property(x => x.Action).IsRequired().HasMaxLength(128).HasComment("Audit action.");
        builder.Property(x => x.Actor).IsRequired().HasMaxLength(128).HasComment("Audit actor.");
        builder.Property(x => x.OccurredAtUtc).HasComment("Audit occurrence time in UTC.");
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128).HasComment("Correlation identifier.");
        builder.Property(x => x.IntegrityHash).IsRequired().HasMaxLength(80).HasComment("Tamper-evident SHA-256 hash over immutable audit fields plus sequence and previous hash.");

        builder.HasIndex(x => new { x.OperationTaskId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SequenceNo }).IsUnique();
    }
}
