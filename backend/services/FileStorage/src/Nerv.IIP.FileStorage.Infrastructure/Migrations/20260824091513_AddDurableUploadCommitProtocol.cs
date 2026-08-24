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
            migrationBuilder.AddColumn<string>(
                name: "commit_checksum",
                schema: "filestorage",
                table: "upload_sessions",
                type: "character varying(71)",
                maxLength: 71,
                nullable: true,
                comment: "不可变的预期规范 SHA-256 证据；最终存储需要自行计算时为空。");

            migrationBuilder.AddColumn<string>(
                name: "commit_id",
                schema: "filestorage",
                table: "upload_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "首次持久提交 Tx1 时创建的不可变唯一所有权标识。");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "committing_at_utc",
                schema: "filestorage",
                table: "upload_sessions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Tx1 将上传会话持久转换为 committing 状态时的 UTC 时间戳。");

            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                schema: "filestorage",
                table: "upload_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "应用程序管理的上传状态转换乐观并发版本。");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "execution_lease_until_utc",
                schema: "filestorage",
                table: "upload_sessions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "当前存储执行租约的 UTC 到期时间。");

            migrationBuilder.AddColumn<string>(
                name: "execution_owner_id",
                schema: "filestorage",
                table: "upload_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "获准为提交意图执行存储 I/O 的短期持久所有者。");

            migrationBuilder.AddColumn<string>(
                name: "last_recovery_error_code",
                schema: "filestorage",
                table: "upload_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "最近一次恢复尝试产生的稳定非敏感诊断码。");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_recovery_at_utc",
                schema: "filestorage",
                table: "upload_sessions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "恢复工作进程不得在此 UTC 时间戳之前重试此提交意图。");

            migrationBuilder.AddColumn<int>(
                name: "recovery_attempt_count",
                schema: "filestorage",
                table: "upload_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "此不可变提交意图的存储恢复失败次数。");

            migrationBuilder.AddColumn<string>(
                name: "state",
                schema: "filestorage",
                table: "upload_sessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "open",
                comment: "持久上传生命周期状态：open、committing 或 completed。");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "storage_action_started_at_utc",
                schema: "filestorage",
                table: "upload_sessions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "任何可能建立最终字节的存储操作开始前写入的 UTC 持久标记。");

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

            migrationBuilder.DropColumn(
                name: "completed",
                schema: "filestorage",
                table: "upload_sessions");

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
                sql: "(state = 'open' AND commit_id IS NULL AND committing_at_utc IS NULL AND storage_action_started_at_utc IS NULL AND completed_at_utc IS NULL AND recovery_attempt_count = 0 AND next_recovery_at_utc IS NULL AND last_recovery_error_code IS NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL) OR (state = 'committing' AND commit_id IS NOT NULL AND committing_at_utc IS NOT NULL AND completed_at_utc IS NULL AND recovery_attempt_count >= 0 AND ((execution_owner_id IS NULL AND execution_lease_until_utc IS NULL) OR (execution_owner_id IS NOT NULL AND execution_lease_until_utc IS NOT NULL))) OR (state = 'completed' AND commit_id IS NOT NULL AND committing_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND recovery_attempt_count >= 0 AND next_recovery_at_utc IS NULL AND last_recovery_error_code IS NULL AND execution_owner_id IS NULL AND execution_lease_until_utc IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "completed",
                schema: "filestorage",
                table: "upload_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "上传会话是否已完成。");

            migrationBuilder.Sql(
                """
                UPDATE filestorage.upload_sessions
                SET completed = state = 'completed';
                """);

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
