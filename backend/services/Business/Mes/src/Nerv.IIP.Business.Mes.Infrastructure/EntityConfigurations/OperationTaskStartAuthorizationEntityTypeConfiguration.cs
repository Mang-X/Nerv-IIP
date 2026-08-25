using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class OperationTaskStartAuthorizationEntityTypeConfiguration
    : IEntityTypeConfiguration<OperationTaskStartAuthorization>
{
    public void Configure(EntityTypeBuilder<OperationTaskStartAuthorization> builder)
    {
        builder.ToTable("operation_task_start_authorizations", table =>
            table.HasComment("Immutable internal authorization facts for starting an MES operation before preceding operations complete."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Stable authorization fact identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization tenant scope.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment scope.");
        builder.Property(x => x.OperationTaskId).HasColumnName("operation_task_id").IsRequired().HasMaxLength(100).HasComment("Operation task authorized to start.");
        builder.Property(x => x.WorkOrderId).HasColumnName("work_order_id").IsRequired().HasMaxLength(100).HasComment("Work order containing the authorized operation.");
        builder.Property(x => x.OperationSequence).HasColumnName("operation_sequence").IsRequired().HasComment("Routing sequence captured at authorization time.");
        builder.Property(x => x.Reason).HasColumnName("reason").IsRequired().HasMaxLength(500).HasComment("Non-empty business reason for the authorized skip.");
        builder.Property(x => x.AuthorizedBy).HasColumnName("authorized_by").IsRequired().HasMaxLength(200).HasComment("Canonical principal from the trusted internal caller.");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired().HasMaxLength(200).HasComment("Request correlation identifier for traceability.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(512).HasComment("Caller intent key for replay convergence.");
        builder.Property(x => x.AuthorizedAtUtc).HasColumnName("authorized_at_utc").IsRequired().HasComment("UTC time when authorization and start succeeded.");
        builder.Property(x => x.ResultStatus).HasColumnName("result_status").IsRequired().HasMaxLength(30).HasComment("Operation task status returned by the combined command.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_operation_task_start_authorizations_scope_task_idempotency");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OperationTaskId, x.AuthorizedAtUtc, x.Id })
            .HasDatabaseName("ix_operation_task_start_authorizations_scope_task_timeline");
    }
}
