using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;

namespace Nerv.IIP.Business.Mes.Infrastructure.EntityConfigurations;

public sealed class QualityHoldTransitionEntityTypeConfiguration : IEntityTypeConfiguration<QualityHoldTransition>
{
    public void Configure(EntityTypeBuilder<QualityHoldTransition> builder)
    {
        builder.ToTable("quality_hold_transitions", table => table.HasComment("Append-only MES quality hold lifecycle transitions; current state remains in quality_hold_contexts."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseGuidVersion7ValueGenerator().HasComment("Stable quality hold transition identifier.");
        builder.Property(x => x.OrganizationId).HasColumnName("organization_id").IsRequired().HasMaxLength(100).HasComment("Organization scope for the transition.");
        builder.Property(x => x.EnvironmentId).HasColumnName("environment_id").IsRequired().HasMaxLength(100).HasComment("Environment scope for the transition.");
        builder.Property(x => x.SourceService).HasColumnName("source_service").IsRequired().HasMaxLength(100).HasComment("Service that owns the held source document.");
        builder.Property(x => x.SourceDocumentId).HasColumnName("source_document_id").IsRequired().HasMaxLength(100).HasComment("Stable MES source document identifier whose hold lifecycle changed.");
        builder.Property(x => x.HoldCycleId).HasColumnName("hold_cycle_id").IsRequired().HasMaxLength(200).HasComment("Stable identifier correlating an applied hold with its release in one lifecycle cycle.");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired().HasMaxLength(200).HasComment("Source command or integration-event correlation identifier for this transition.");
        builder.Property(x => x.EventKind).HasColumnName("event_kind").IsRequired().HasMaxLength(50).HasComment("Lifecycle event kind: hold-applied, inspection-released, or manual-force-released.");
        builder.Property(x => x.Actor).HasColumnName("actor").IsRequired().HasMaxLength(100).HasComment("Known actor supplied by the transition source; legacy values are not synthesized.");
        builder.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired().HasComment("UTC instant when the lifecycle transition occurred.");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).HasComment("Reason supplied by the source transition when available; unknown legacy values remain null.");
        builder.Property(x => x.SourceInspectionRecordId).HasColumnName("source_inspection_record_id").HasMaxLength(100).HasComment("Quality inspection record that caused the transition when applicable.");
        builder.Property(x => x.SourceInspectionDocumentId).HasColumnName("source_inspection_document_id").HasMaxLength(100).HasComment("Quality inspection plan or document associated with the transition when available.");
        builder.Property(x => x.Origin).HasColumnName("origin").IsRequired().HasMaxLength(20).HasComment("Transition origin: automatic or manual.");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(512).HasComment("Governed source idempotency key when supplied; unavailable legacy values remain null.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SourceDocumentId, x.OccurredAtUtc, x.Id })
            .HasDatabaseName("ix_quality_hold_transitions_scope_source_timeline");
        builder.HasIndex(x => new
            {
                x.OrganizationId,
                x.EnvironmentId,
                x.SourceService,
                x.SourceDocumentId,
                x.HoldCycleId,
                x.CorrelationId,
                x.EventKind,
            })
            .IsUnique()
            .HasDatabaseName("ux_quality_hold_transitions_scope_correlation_kind");
        builder.HasIndex(x => new
            {
                x.OrganizationId,
                x.EnvironmentId,
                x.SourceService,
                x.SourceDocumentId,
                x.HoldCycleId,
                x.IdempotencyKey,
                x.EventKind,
            })
            .IsUnique()
            .HasDatabaseName("ux_quality_hold_transitions_scope_idempotency_kind");
    }
}
