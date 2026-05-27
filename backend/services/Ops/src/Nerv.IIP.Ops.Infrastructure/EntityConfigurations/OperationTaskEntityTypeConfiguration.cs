using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Ops.Infrastructure.EntityConfigurations;

public sealed class OperationTaskEntityTypeConfiguration : IEntityTypeConfiguration<OperationTask>
{
    public void Configure(EntityTypeBuilder<OperationTask> builder)
    {
        builder.ToTable("operation_tasks", table => table.HasComment("Ops operation task aggregate roots requested through Gateway and executed by connector hosts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new OperationTaskId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Operation task identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(128).HasComment("Organization identifier.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(128).HasComment("Environment identifier.");
        builder.Property(x => x.InstanceKey).IsRequired().HasMaxLength(256).HasComment("Target instance key.");
        builder.Property(x => x.OperationCode).IsRequired().HasMaxLength(128).HasComment("Operation code.");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(32).HasComment("Operation task status.");
        builder.Property(x => x.RequestedBy).IsRequired().HasMaxLength(128).HasComment("Requester.");
        builder.Property(x => x.RequestedAtUtc).HasComment("Requested time in UTC.");
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(256).HasComment("Request idempotency key.");
        builder.Property(x => x.IdempotencyScope).IsRequired().HasMaxLength(512).HasComment("Organization and environment scoped idempotency key.");
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128).HasComment("Correlation identifier.");
        builder.Property(x => x.ParametersJson).IsRequired().HasComment("JSON operation parameter dictionary produced by Gateway and Ops task creation, consumed by Connector Host execution; additive optional keys are compatible, required key or semantic changes require Ops contract versioning.");
        builder.Property(x => x.DefaultMaxAttempts).HasComment("Template-provided default maximum execution attempts captured at task creation.");
        builder.Property(x => x.DefaultLeaseDurationSeconds).HasComment("Template-provided default connector lease duration captured at task creation.");
        builder.Property(x => x.RequiresApproval).HasComment("Whether the selected template requires approval before task execution.");
        builder.Property(x => x.ApprovalStatus).HasMaxLength(32).HasComment("Operation approval status when approval is required.");
        builder.Property(x => x.ApprovalRequestedBy).HasMaxLength(128).HasComment("Actor that requested approval for a high-risk operation.");
        builder.Property(x => x.ApprovalRequestedAtUtc).HasComment("Approval request time in UTC.");
        builder.Property(x => x.ApprovalDecidedBy).HasMaxLength(128).HasComment("Actor that approved or rejected the operation.");
        builder.Property(x => x.ApprovalDecidedAtUtc).HasComment("Approval decision time in UTC.");
        builder.Property(x => x.ApprovalDecisionReason).HasMaxLength(512).HasComment("Approval decision reason.");
        builder.Property(x => x.Deleted).HasConversion(x => x.Value, x => new Deleted(x)).HasComment("Soft delete flag.");
        builder.Property(x => x.RowVersion).HasConversion(x => x.VersionNumber, x => new RowVersion(x)).HasComment("Optimistic row version.");

        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status, x.RequestedAtUtc });
        builder.HasIndex(x => x.IdempotencyScope).IsUnique();

        builder.HasMany(x => x.Attempts)
            .WithOne()
            .HasForeignKey(x => x.OperationTaskId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Attempts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.AuditRecords)
            .WithOne()
            .HasForeignKey(x => x.OperationTaskId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.AuditRecords).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
