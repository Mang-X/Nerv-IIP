using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Coding;

internal sealed class CodeCounterEntityTypeConfiguration : IEntityTypeConfiguration<CodeCounter>
{
    public void Configure(EntityTypeBuilder<CodeCounter> builder)
    {
        builder.ToTable("code_counters", table => table.HasComment("Service-local code counters scoped by organization, environment, rule key, optional site and reset bucket."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasComment("Code counter surrogate identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization scope for the code counter.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment scope for the code counter.");
        builder.Property(x => x.RuleKey).HasColumnName("rule_key").IsRequired().HasMaxLength(100).HasComment("Code rule key governed by this counter.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("Optional site or plant scope; empty string means global within organization and environment.");
        builder.Property(x => x.ResetKey).HasColumnName("reset_key").IsRequired().HasMaxLength(16).HasComment("Sequence reset bucket derived from the active code rule.");
        builder.Property(x => x.CurrentValue).HasColumnName("current_value").HasComment("Last allocated sequence value within the counter scope.");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().HasComment("Optimistic concurrency token incremented whenever the counter advances.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RuleKey, x.SiteCode, x.ResetKey }).IsUnique().HasDatabaseName("ux_code_counters_scope");
    }
}

internal sealed class CodeIdempotencyKeyEntityTypeConfiguration : IEntityTypeConfiguration<CodeIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<CodeIdempotencyKey> builder)
    {
        builder.ToTable("code_idempotency_keys", table => table.HasComment("Service-local idempotency records that bind create request keys to allocated codes."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasComment("Code idempotency record surrogate identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization scope for the idempotency key.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment scope for the idempotency key.");
        builder.Property(x => x.RuleKey).HasColumnName("rule_key").IsRequired().HasMaxLength(100).HasComment("Code rule key governed by the idempotency key.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(150).HasComment("Client supplied stable idempotency key for ordinary create requests.");
        builder.Property(x => x.Code).HasColumnName("code").IsRequired().HasMaxLength(128).HasComment("Allocated business code returned for this idempotency key.");
        builder.Property(x => x.PayloadFingerprint).HasColumnName("payload_fingerprint").IsRequired().HasMaxLength(1000).HasComment("Canonical request payload fingerprint used to reject key reuse with different create data.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC timestamp when the idempotency key was first recorded.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.RuleKey, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_code_idempotency_keys_scope");
    }
}