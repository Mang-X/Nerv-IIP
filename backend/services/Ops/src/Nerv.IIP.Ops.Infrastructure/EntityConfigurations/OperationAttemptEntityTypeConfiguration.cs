using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Ops.Infrastructure.EntityConfigurations;

public sealed class OperationAttemptEntityTypeConfiguration : IEntityTypeConfiguration<OperationAttempt>
{
    public void Configure(EntityTypeBuilder<OperationAttempt> builder)
    {
        builder.ToTable("operation_attempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new OperationAttemptId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Operation attempt identifier.");
        builder.Property(x => x.OperationTaskId)
            .HasConversion(x => x.Id, x => new OperationTaskId(x))
            .HasMaxLength(64)
            .HasComment("Operation task identifier.");
        builder.Property(x => x.ConnectorHostId).IsRequired().HasMaxLength(128).HasComment("Connector host identifier.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32).HasComment("Attempt status.");
        builder.Property(x => x.StartedAtUtc).HasComment("Attempt start time in UTC.");
        builder.Property(x => x.FinishedAtUtc).HasComment("Attempt finish time in UTC.");
        builder.Property(x => x.FailureJson).HasComment("Serialized failure reason.");

        builder.HasIndex(x => x.OperationTaskId);
    }
}
