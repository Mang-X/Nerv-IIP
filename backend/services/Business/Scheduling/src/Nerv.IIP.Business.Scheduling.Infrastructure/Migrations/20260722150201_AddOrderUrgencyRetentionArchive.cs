using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderUrgencyRetentionArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_urgency_archive_batches",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Archive batch audit row id."),
                    batch_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Stable content-derived archive batch id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    snapshot_ids_json = table.Column<string>(type: "jsonb", nullable: false, comment: "JSON array of Scheduling-owned source snapshot ids; produced and consumed by the retention worker with additive compatibility."),
                    snapshot_count = table.Column<int>(type: "integer", nullable: false, comment: "Number of snapshots represented by the archive batch."),
                    min_calculated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Oldest UTC calculation time in the batch."),
                    max_calculated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Newest UTC calculation time in the batch."),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Retention lifecycle status: pending, archived, failed, source-deleted, or archive-deleted."),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true, comment: "Internal FileStorage object key; never exposed publicly."),
                    object_version_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Exact immutable object-storage version id verified by FileStorage."),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Lowercase SHA-256 of the archived JSON bytes."),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true, comment: "Verified archive object size in bytes."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when this batch was first attempted."),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when exact-version read-back evidence completed."),
                    source_deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when authorized source-row deletion committed."),
                    archive_deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when authorized exact object-version deletion completed."),
                    source_deletion_authorization_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Explicit authorization/change reference used for source-row deletion."),
                    source_deletion_actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Actor recorded for source-row deletion."),
                    source_deletion_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Reason recorded for source-row deletion."),
                    archive_deletion_authorization_reference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Independent authorization/change reference used for exact archive-version deletion."),
                    archive_deletion_actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Actor recorded for exact archive-version deletion."),
                    archive_deletion_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Reason recorded for exact archive-version deletion."),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Stable failure classification for the latest attempt."),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true, comment: "Sanitized latest failure detail."),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, comment: "Number of archive attempts for this stable batch.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_urgency_archive_batches", x => x.id);
                },
                comment: "Durable archive, deletion, and failure evidence for urgency snapshot retention batches.");

            migrationBuilder.CreateTable(
                name: "order_urgency_restore_audits",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Restore audit row id."),
                    batch_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Restored archive batch id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    object_version_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Exact object version read during restore."),
                    actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Authenticated operator reference."),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Operator-provided restore reason."),
                    restored_snapshot_count = table.Column<int>(type: "integer", nullable: false, comment: "Number of missing snapshots rehydrated."),
                    restored_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC restore completion timestamp.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_urgency_restore_audits", x => x.id);
                },
                comment: "Append-only operator audit for exact-version urgency archive restores.");

            migrationBuilder.CreateTable(
                name: "order_urgency_retention_leases",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Lease row id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    owner_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Worker instance currently holding the lease."),
                    acquired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the current owner acquired the lease."),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC lease expiry; an expired lease may be taken over."),
                    revision = table.Column<long>(type: "bigint", nullable: false, comment: "Monotonic optimistic concurrency revision.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_urgency_retention_leases", x => x.id);
                },
                comment: "Database-backed expiring leases preventing concurrent retention work per organization and environment.");

            migrationBuilder.CreateIndex(
                name: "ix_order_urgency_snapshot_retention_scan",
                schema: "scheduling",
                table: "order_urgency_snapshots",
                columns: new[] { "organization_id", "environment_id", "calculated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_urgency_archive_scope_batch",
                schema: "scheduling",
                table: "order_urgency_archive_batches",
                columns: new[] { "organization_id", "environment_id", "batch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_urgency_archive_scope_status_watermark",
                schema: "scheduling",
                table: "order_urgency_archive_batches",
                columns: new[] { "organization_id", "environment_id", "status", "max_calculated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_urgency_restore_scope_batch_time",
                schema: "scheduling",
                table: "order_urgency_restore_audits",
                columns: new[] { "organization_id", "environment_id", "batch_id", "restored_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_urgency_retention_lease_expiry",
                schema: "scheduling",
                table: "order_urgency_retention_leases",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_urgency_retention_lease_scope",
                schema: "scheduling",
                table: "order_urgency_retention_leases",
                columns: new[] { "organization_id", "environment_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_urgency_archive_batches",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "order_urgency_restore_audits",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "order_urgency_retention_leases",
                schema: "scheduling");

            migrationBuilder.DropIndex(
                name: "ix_order_urgency_snapshot_retention_scan",
                schema: "scheduling",
                table: "order_urgency_snapshots");
        }
    }
}
