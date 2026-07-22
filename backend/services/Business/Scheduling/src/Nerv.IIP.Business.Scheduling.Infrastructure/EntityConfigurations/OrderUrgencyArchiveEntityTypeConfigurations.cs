using Nerv.IIP.Business.Scheduling.Infrastructure.Urgency;

namespace Nerv.IIP.Business.Scheduling.Infrastructure.EntityConfigurations;

public sealed class OrderUrgencyArchiveBatchEntityTypeConfiguration : IEntityTypeConfiguration<OrderUrgencyArchiveBatch>
{
    public void Configure(EntityTypeBuilder<OrderUrgencyArchiveBatch> builder)
    {
        builder.ToTable("order_urgency_archive_batches", table => table.HasComment("Durable archive, deletion, and failure evidence for urgency snapshot retention batches."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().HasComment("Archive batch audit row id.");
        Scope(builder);
        builder.Property(x => x.BatchId).HasColumnName("batch_id").HasMaxLength(64).IsRequired().HasComment("Stable source-row-generation-derived archive batch id.");
        builder.Property(x => x.SnapshotIdsJson).HasColumnName("snapshot_ids_json").HasColumnType("jsonb").IsRequired().HasComment("JSON array of Scheduling-owned source snapshot ids; produced and consumed by the retention worker with additive compatibility.");
        builder.Property(x => x.SnapshotCount).HasColumnName("snapshot_count").HasComment("Number of snapshots represented by the archive batch.");
        builder.Property(x => x.MinCalculatedAtUtc).HasColumnName("min_calculated_at_utc").HasComment("Oldest UTC calculation time in the batch.");
        builder.Property(x => x.MaxCalculatedAtUtc).HasColumnName("max_calculated_at_utc").HasComment("Newest UTC calculation time in the batch.");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired().HasComment("Retention lifecycle status: pending, archived, failed, source-deleted, or archive-deleted.");
        builder.Property(x => x.ObjectKey).HasColumnName("object_key").HasMaxLength(1024).HasComment("Internal FileStorage object key; never exposed publicly.");
        builder.Property(x => x.ObjectVersionId).HasColumnName("object_version_id").HasMaxLength(256).HasComment("Exact immutable object-storage version id verified by FileStorage.");
        builder.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).HasComment("Lowercase SHA-256 of the archived JSON bytes.");
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").HasComment("Verified archive object size in bytes.");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasComment("UTC timestamp when this batch was first attempted.");
        builder.Property(x => x.ArchivedAtUtc).HasColumnName("archived_at_utc").HasComment("UTC timestamp when exact-version read-back evidence completed.");
        builder.Property(x => x.SourceDeletedAtUtc).HasColumnName("source_deleted_at_utc").HasComment("UTC timestamp when authorized source-row deletion committed.");
        builder.Property(x => x.ArchiveDeletedAtUtc).HasColumnName("archive_deleted_at_utc").HasComment("UTC timestamp when authorized exact object-version deletion completed.");
        builder.Property(x => x.SourceDeletionAuthorizationReference).HasColumnName("source_deletion_authorization_reference").HasMaxLength(256).HasComment("Explicit authorization/change reference used for source-row deletion.");
        builder.Property(x => x.SourceDeletionActor).HasColumnName("source_deletion_actor").HasMaxLength(256).HasComment("Actor recorded for source-row deletion.");
        builder.Property(x => x.SourceDeletionReason).HasColumnName("source_deletion_reason").HasMaxLength(1000).HasComment("Reason recorded for source-row deletion.");
        builder.Property(x => x.ArchiveDeletionAuthorizationReference).HasColumnName("archive_deletion_authorization_reference").HasMaxLength(256).HasComment("Independent authorization/change reference used for exact archive-version deletion.");
        builder.Property(x => x.ArchiveDeletionActor).HasColumnName("archive_deletion_actor").HasMaxLength(256).HasComment("Actor recorded for exact archive-version deletion.");
        builder.Property(x => x.ArchiveDeletionReason).HasColumnName("archive_deletion_reason").HasMaxLength(1000).HasComment("Reason recorded for exact archive-version deletion.");
        builder.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128).HasComment("Stable failure classification for the latest attempt.");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000).HasComment("Sanitized latest failure detail.");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasComment("Number of archive attempts for this stable batch.");
        builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken().HasComment("Monotonic optimistic concurrency revision protecting lifecycle transitions.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.BatchId })
            .IsUnique()
            .HasDatabaseName("ix_urgency_archive_scope_batch");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status, x.MaxCalculatedAtUtc })
            .HasDatabaseName("ix_urgency_archive_scope_status_watermark");
    }

    internal static void Scope<T>(EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property<string>("OrganizationId").HasColumnName("organization_id").HasMaxLength(64).IsRequired().HasComment("Tenant organization id.");
        builder.Property<string>("EnvironmentId").HasColumnName("environment_id").HasMaxLength(64).IsRequired().HasComment("Business environment id.");
    }
}

public sealed class OrderUrgencyRetentionLeaseEntityTypeConfiguration : IEntityTypeConfiguration<OrderUrgencyRetentionLease>
{
    public void Configure(EntityTypeBuilder<OrderUrgencyRetentionLease> builder)
    {
        builder.ToTable("order_urgency_retention_leases", table => table.HasComment("Database-backed expiring leases preventing concurrent retention work per organization and environment."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().HasComment("Lease row id.");
        OrderUrgencyArchiveBatchEntityTypeConfiguration.Scope(builder);
        builder.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(256).IsRequired().HasComment("Worker instance currently holding the lease.");
        builder.Property(x => x.AcquiredAtUtc).HasColumnName("acquired_at_utc").HasComment("UTC timestamp when the current owner acquired the lease.");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").HasComment("UTC lease expiry; an expired lease may be taken over.");
        builder.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken().HasComment("Monotonic optimistic concurrency revision.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId })
            .IsUnique()
            .HasDatabaseName("ix_urgency_retention_lease_scope");
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("ix_urgency_retention_lease_expiry");
    }
}

public sealed class OrderUrgencyArchiveBatchSnapshotEntityTypeConfiguration : IEntityTypeConfiguration<OrderUrgencyArchiveBatchSnapshot>
{
    public void Configure(EntityTypeBuilder<OrderUrgencyArchiveBatchSnapshot> builder)
    {
        builder.ToTable(
            "order_urgency_archive_batch_snapshots",
            table => table.HasComment("Indexed source snapshot membership for a durable urgency archive batch intent."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().HasComment("Archive batch membership row id.");
        OrderUrgencyArchiveBatchEntityTypeConfiguration.Scope(builder);
        builder.Property(x => x.ArchiveBatchId).HasColumnName("archive_batch_id").HasComment("Owning durable archive batch audit row id.");
        builder.Property(x => x.SnapshotId).HasColumnName("snapshot_id").HasComment("Scheduling-owned source snapshot id reserved by this archive intent.");
        builder.Property(x => x.Sequence).HasColumnName("sequence").HasComment("Stable zero-based position in the archived payload.");
        builder.HasIndex(x => new { x.ArchiveBatchId, x.Sequence })
            .IsUnique()
            .HasDatabaseName("ix_urgency_archive_membership_batch_sequence");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.SnapshotId })
            .IsUnique()
            .HasDatabaseName("ix_urgency_archive_membership_scope_snapshot");
        builder.HasOne<OrderUrgencyArchiveBatch>()
            .WithMany()
            .HasForeignKey(x => x.ArchiveBatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrderUrgencyRestoreAuditEntityTypeConfiguration : IEntityTypeConfiguration<OrderUrgencyRestoreAudit>
{
    public void Configure(EntityTypeBuilder<OrderUrgencyRestoreAudit> builder)
    {
        builder.ToTable("order_urgency_restore_audits", table => table.HasComment("Append-only operator audit for exact-version urgency archive restores."));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever().HasComment("Restore audit row id.");
        OrderUrgencyArchiveBatchEntityTypeConfiguration.Scope(builder);
        builder.Property(x => x.BatchId).HasColumnName("batch_id").HasMaxLength(64).IsRequired().HasComment("Restored archive batch id.");
        builder.Property(x => x.ObjectVersionId).HasColumnName("object_version_id").HasMaxLength(256).IsRequired().HasComment("Exact object version read during restore.");
        builder.Property(x => x.Actor).HasColumnName("actor").HasMaxLength(256).IsRequired().HasComment("Authenticated operator reference.");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired().HasComment("Operator-provided restore reason.");
        builder.Property(x => x.RestoredSnapshotCount).HasColumnName("restored_snapshot_count").HasComment("Number of missing snapshots rehydrated.");
        builder.Property(x => x.RestoredAtUtc).HasColumnName("restored_at_utc").HasComment("UTC restore completion timestamp.");
        builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.BatchId, x.RestoredAtUtc })
            .HasDatabaseName("ix_urgency_restore_scope_batch_time");
    }
}
