using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Numbering;

internal sealed class NumberingCounterEntityTypeConfiguration : IEntityTypeConfiguration<NumberingCounter>
{
    public void Configure(EntityTypeBuilder<NumberingCounter> builder)
    {
        builder.ToTable("numbering_counters", table => table.HasComment("Service-local numbering counters scoped by organization, environment, document type, optional site and date segment."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasComment("Numbering counter surrogate identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization scope for the numbering counter.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment scope for the numbering counter.");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").IsRequired().HasMaxLength(100).HasComment("Business document type governed by this counter.");
        builder.Property(x => x.SiteCode).HasColumnName("site_code").IsRequired().HasMaxLength(100).HasComment("Optional site or plant scope; empty string means global within organization and environment.");
        builder.Property(x => x.DateSegment).HasColumnName("date_segment").IsRequired().HasMaxLength(16).HasComment("Date segment used by the numbering rule, formatted as yyyyMMdd for the current baseline.");
        builder.Property(x => x.Prefix).HasColumnName("prefix").IsRequired().HasMaxLength(32).HasComment("Document number prefix emitted before the date segment.");
        builder.Property(x => x.CurrentValue).HasColumnName("current_value").HasComment("Last allocated sequence value within the counter scope.");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().HasComment("Optimistic concurrency token incremented whenever the counter advances.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DocumentType, x.SiteCode, x.DateSegment }).IsUnique().HasDatabaseName("ux_numbering_counters_scope");
    }
}

internal sealed class NumberingIdempotencyKeyEntityTypeConfiguration : IEntityTypeConfiguration<NumberingIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<NumberingIdempotencyKey> builder)
    {
        builder.ToTable("numbering_idempotency_keys", table => table.HasComment("Service-local idempotency records that bind create request keys to allocated document numbers."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasComment("Numbering idempotency record surrogate identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization scope for the idempotency key.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment scope for the idempotency key.");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").IsRequired().HasMaxLength(100).HasComment("Business document type governed by the idempotency key.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(150).HasComment("Client supplied stable idempotency key for ordinary create requests.");
        builder.Property(x => x.Number).HasColumnName("number").IsRequired().HasMaxLength(128).HasComment("Allocated business document number returned for this idempotency key.");
        builder.Property(x => x.PayloadFingerprint).HasColumnName("payload_fingerprint").IsRequired().HasMaxLength(1000).HasComment("Canonical request payload fingerprint used to reject key reuse with different create data.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC timestamp when the idempotency key was first recorded.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.DocumentType, x.IdempotencyKey }).IsUnique().HasDatabaseName("ux_numbering_idempotency_keys_scope");
    }
}
