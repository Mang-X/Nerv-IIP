using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.FileStorage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableUploadCommitProtocol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "completed",
                schema: "filestorage",
                table: "upload_sessions",
                type: "boolean",
                nullable: false,
                comment: "Expand-window compatibility flag written by both the legacy and durable commit protocols.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Whether the upload session was completed.");

            migrationBuilder.AddColumn<string>(
                name: "commit_checksum",
                schema: "filestorage",
                table: "upload_sessions",
                type: "character varying(71)",
                maxLength: 71,
                nullable: true,
                comment: "Immutable expected canonical SHA-256 evidence; null when final storage must compute it.");

            migrationBuilder.AddColumn<string>(
                name: "commit_id",
                schema: "filestorage",
                table: "upload_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Immutable unique commit ownership identifier created by Tx1.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "committing_at_utc",
                schema: "filestorage",
                table: "upload_sessions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC timestamp when Tx1 durably moved the upload session to committing.");

            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                schema: "filestorage",
                table: "upload_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Application-managed optimistic concurrency version for upload state transitions.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "execution_lease_until_utc",
                schema: "filestorage",
                table: "upload_sessions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC expiration timestamp of the current storage execution lease.");

            migrationBuilder.AddColumn<string>(
                name: "execution_owner_id",
                schema: "filestorage",
                table: "upload_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Short-lived durable owner authorized to execute storage I/O for the commit intent.");

            migrationBuilder.AddColumn<string>(
                name: "last_recovery_error_code",
                schema: "filestorage",
                table: "upload_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Stable non-sensitive diagnostic code from the latest recovery attempt.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_recovery_at_utc",
                schema: "filestorage",
                table: "upload_sessions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC timestamp before which recovery must not retry this commit intent.");

            migrationBuilder.AddColumn<int>(
                name: "recovery_attempt_count",
                schema: "filestorage",
                table: "upload_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Storage recovery failure count for the immutable commit intent.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recovery_terminal_at_utc",
                schema: "filestorage",
                table: "upload_sessions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC timestamp when automatic recovery stopped after a permanent evidence failure.");

            migrationBuilder.AddColumn<string>(
                name: "state",
                schema: "filestorage",
                table: "upload_sessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "open",
                comment: "Durable upload lifecycle state: open, committing, or completed.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "storage_action_started_at_utc",
                schema: "filestorage",
                table: "upload_sessions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Durable UTC marker written before any storage action that may establish final bytes.");

            migrationBuilder.Sql(
                """
                UPDATE filestorage.upload_sessions AS session
                SET state = CASE
                        WHEN session.completed
                             AND session.completed_at_utc IS NOT NULL
                             AND EXISTS (
                                 SELECT 1
                                 FROM filestorage.stored_files AS file
                                 WHERE file.file_id = session.file_id
                                   AND file.object_key = session.object_key)
                            THEN 'completed'
                        WHEN session.completed THEN 'committing'
                        ELSE 'open'
                    END,
                    commit_id = CASE
                        WHEN session.completed THEN 'legacy_' || md5(session.upload_session_id)
                        ELSE NULL
                    END,
                    commit_checksum = CASE
                        WHEN session.completed AND session.checksum ~* '^sha256:[0-9a-f]{64}$'
                            THEN lower(session.checksum)
                        ELSE NULL
                    END,
                    committing_at_utc = CASE
                        WHEN session.completed THEN COALESCE(session.completed_at_utc, session.created_at_utc)
                        ELSE NULL
                    END,
                    storage_action_started_at_utc = CASE
                        WHEN session.completed THEN COALESCE(session.completed_at_utc, session.created_at_utc)
                        ELSE NULL
                    END,
                    completed_at_utc = CASE
                        WHEN session.completed
                             AND session.completed_at_utc IS NOT NULL
                             AND EXISTS (
                                 SELECT 1
                                 FROM filestorage.stored_files AS file
                                 WHERE file.file_id = session.file_id
                                   AND file.object_key = session.object_key)
                            THEN session.completed_at_utc
                        ELSE NULL
                    END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_commit_id",
                schema: "filestorage",
                table: "upload_sessions",
                column: "commit_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_upload_sessions_state_next_recovery_at_utc",
                schema: "filestorage",
                table: "upload_sessions",
                columns: new[] { "state", "next_recovery_at_utc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_upload_sessions_state_intent",
                schema: "filestorage",
                table: "upload_sessions",
                sql: "(completed AND state = 'open' AND commit_id IS NULL AND committing_at_utc IS NULL AND storage_action_started_at_utc IS NULL AND completed_at_utc IS NOT NULL AND recovery_attempt_count = 0 AND next_recovery_at_utc IS NULL AND last_recovery_error_code IS NULL AND recovery_terminal_at_utc IS NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL) OR (NOT completed AND state = 'open' AND commit_id IS NULL AND committing_at_utc IS NULL AND storage_action_started_at_utc IS NULL AND completed_at_utc IS NULL AND recovery_attempt_count = 0 AND next_recovery_at_utc IS NULL AND last_recovery_error_code IS NULL AND recovery_terminal_at_utc IS NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL) OR (state = 'committing' AND commit_id IS NOT NULL AND committing_at_utc IS NOT NULL AND completed_at_utc IS NULL AND recovery_attempt_count >= 0 AND (recovery_terminal_at_utc IS NULL OR (next_recovery_at_utc IS NULL AND last_recovery_error_code IS NOT NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL)) AND ((execution_owner_id IS NULL AND execution_lease_until_utc IS NULL) OR (execution_owner_id IS NOT NULL AND execution_lease_until_utc IS NOT NULL))) OR (completed AND state = 'completed' AND commit_id IS NOT NULL AND committing_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND recovery_attempt_count >= 0 AND next_recovery_at_utc IS NULL AND last_recovery_error_code IS NULL AND recovery_terminal_at_utc IS NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE filestorage.upload_sessions
                SET completed = completed OR state = 'completed';
                """);

            migrationBuilder.AlterColumn<bool>(
                name: "completed",
                schema: "filestorage",
                table: "upload_sessions",
                type: "boolean",
                nullable: false,
                comment: "Whether the upload session was completed.",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Expand-window compatibility flag written by both the legacy and durable commit protocols.");

            migrationBuilder.DropIndex(
                name: "IX_upload_sessions_commit_id",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropIndex(
                name: "IX_upload_sessions_state_next_recovery_at_utc",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_upload_sessions_state_intent",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "commit_checksum",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "commit_id",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "committing_at_utc",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "execution_lease_until_utc",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "execution_owner_id",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "last_recovery_error_code",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "next_recovery_at_utc",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "recovery_attempt_count",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "recovery_terminal_at_utc",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "filestorage",
                table: "upload_sessions");

            migrationBuilder.DropColumn(
                name: "storage_action_started_at_utc",
                schema: "filestorage",
                table: "upload_sessions");

        }
    }
}
