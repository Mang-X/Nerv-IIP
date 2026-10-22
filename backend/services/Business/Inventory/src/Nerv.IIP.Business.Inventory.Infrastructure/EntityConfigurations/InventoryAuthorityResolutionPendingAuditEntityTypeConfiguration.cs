namespace Nerv.IIP.Business.Inventory.Infrastructure.EntityConfigurations;

public sealed class InventoryAuthorityResolutionPendingAuditEntityTypeConfiguration
    : IEntityTypeConfiguration<InventoryAuthorityResolutionPendingAudit>
{
    public void Configure(EntityTypeBuilder<InventoryAuthorityResolutionPendingAudit> builder)
    {
        builder.ToTable("authority_resolution_pending_audits", tableBuilder =>
        {
            tableBuilder.HasComment(
                "Inventory event-bound audit facts for unit-cost authority pending deliveries; one immutable fact per event id.");
            tableBuilder.HasCheckConstraint(
                "ck_authority_resolution_pending_audits_event_id",
                "length(event_id) > 0");
            tableBuilder.HasCheckConstraint(
                "ck_authority_resolution_pending_audits_idempotency_key",
                "length(idempotency_key) > 0");
            tableBuilder.HasCheckConstraint(
                "ck_authority_resolution_pending_audits_reason_code",
                "length(reason_code) > 0");
            tableBuilder.HasCheckConstraint(
                "ck_authority_resolution_pending_audits_status",
                "status = 'Pending'");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .HasComment("Guid v7 identity of the authority-pending audit fact.");
        builder.Property(x => x.EventId)
            .HasColumnName("event_id")
            .IsRequired()
            .HasMaxLength(256)
            .HasComment("Integration event id that was kept pending.");
        builder.Property(x => x.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .IsRequired()
            .HasMaxLength(128)
            .HasComment("Producer idempotency key bound to the pending event.");
        builder.Property(x => x.ReasonCode)
            .HasColumnName("reason_code")
            .IsRequired()
            .HasMaxLength(150)
            .HasComment("Formal unit-cost authority pending reason code.");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(30)
            .HasComment("Pending audit lifecycle status; only Pending is valid for this seam.");
        builder.Property(x => x.ObservedAtUtc)
            .HasColumnName("observed_at_utc")
            .IsRequired()
            .HasComment("UTC time when Inventory observed the authority pending result.");

        builder.HasIndex(x => x.EventId)
            .IsUnique()
            .HasDatabaseName("ux_authority_resolution_pending_audits_event_id");
    }
}
