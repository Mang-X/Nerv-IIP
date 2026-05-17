using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Ops.Infrastructure.EntityConfigurations;

public sealed class AuditRecordEntityTypeConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("audit_records");
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
        builder.Property(x => x.Action).IsRequired().HasMaxLength(128).HasComment("Audit action.");
        builder.Property(x => x.Actor).IsRequired().HasMaxLength(128).HasComment("Audit actor.");
        builder.Property(x => x.OccurredAtUtc).HasComment("Audit occurrence time in UTC.");
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128).HasComment("Correlation identifier.");

        builder.HasIndex(x => new { x.OperationTaskId, x.OccurredAtUtc });
    }
}
