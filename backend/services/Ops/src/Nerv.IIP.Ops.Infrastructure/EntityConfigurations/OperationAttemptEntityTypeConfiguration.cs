using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Ops.Infrastructure.EntityConfigurations;

public sealed class OperationAttemptEntityTypeConfiguration : IEntityTypeConfiguration<OperationAttempt>
{
    public void Configure(EntityTypeBuilder<OperationAttempt> builder)
    {
        builder.ToTable("operation_attempts", table => table.HasComment("Ops operation execution attempts created when connector hosts claim operation tasks."));
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
        builder.Property(x => x.FailureJson).HasComment("JSON failure details produced by Connector Host execution, consumed by Ops and Gateway diagnostics; additive optional keys are compatible, removing or changing key semantics requires Ops contract versioning.");
        builder.Property(x => x.LeaseId).HasMaxLength(64).HasComment("Lease identifier returned by Ops claim and required for heartbeat or abandon updates; null for legacy attempts created before lease claim protocol fields existed.");
        builder.Property(x => x.LeasedAtUtc).HasComment("UTC time when Ops granted this lease.");
        builder.Property(x => x.LeasedUntilUtc).HasComment("UTC time when the lease expires and becomes eligible for requeue.");
        builder.Property(x => x.AttemptNo).HasComment("One-based attempt number for this operation task.");
        builder.Property(x => x.MaxAttempts).HasComment("Maximum attempts allowed before an expired or abandoned task becomes failed.");
        builder.Property(x => x.AbandonReason).HasMaxLength(256).HasComment("Reason recorded when the attempt lease is abandoned or times out.");

        builder.HasIndex(x => x.OperationTaskId);
        builder.HasIndex(x => new { x.Status, x.LeasedUntilUtc });
    }
}
