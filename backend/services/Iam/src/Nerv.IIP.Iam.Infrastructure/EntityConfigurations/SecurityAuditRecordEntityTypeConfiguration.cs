using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Iam.Domain.AggregatesModel.SecurityAuditAggregate;

namespace Nerv.IIP.Iam.Infrastructure.EntityConfigurations;

public sealed class SecurityAuditRecordEntityTypeConfiguration : IEntityTypeConfiguration<SecurityAuditRecord>
{
    public void Configure(EntityTypeBuilder<SecurityAuditRecord> builder)
    {
        builder.ToTable("security_audit_records", table => table.HasComment("IAM security audit records for authentication decisions, session revocation and authorization administration."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Id, x => new SecurityAuditRecordId(x))
            .ValueGeneratedNever()
            .HasMaxLength(64)
            .HasComment("Security audit record identifier.");
        builder.Property(x => x.OrganizationId).IsRequired().HasMaxLength(64).HasComment("Organization scope for the audited IAM security event.");
        builder.Property(x => x.EnvironmentId).IsRequired().HasMaxLength(64).HasComment("Environment scope for the audited IAM security event.");
        builder.Property(x => x.Action).IsRequired().HasMaxLength(128).HasComment("IAM security audit action.");
        builder.Property(x => x.Actor).IsRequired().HasMaxLength(128).HasComment("Actor that caused or attempted the security event.");
        builder.Property(x => x.TargetType).IsRequired().HasMaxLength(64).HasComment("Audited target type, for example user, session or role.");
        builder.Property(x => x.TargetId).IsRequired().HasMaxLength(160).HasComment("Audited target identifier.");
        builder.Property(x => x.Outcome).IsRequired().HasMaxLength(32).HasComment("Audit outcome, for example success or failure.");
        builder.Property(x => x.OccurredAtUtc).HasComment("Security audit occurrence time in UTC.");
        builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128).HasComment("Correlation identifier for the security event.");
        builder.Property(x => x.SourceIp).HasMaxLength(64).HasComment("Source IP address associated with the security event when available.");
        builder.Property(x => x.DetailsJson).IsRequired().HasColumnType("jsonb").HasComment("Structured JSON details for before and after values or decision diagnostics.");

        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.Action, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.TargetType, x.TargetId, x.OccurredAtUtc });
    }
}
