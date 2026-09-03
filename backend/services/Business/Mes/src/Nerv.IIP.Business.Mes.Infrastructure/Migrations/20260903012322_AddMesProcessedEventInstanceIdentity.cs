using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesProcessedEventInstanceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                schema: "mes",
                table: "processed_integration_events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                comment: "Deterministic cross-version business identity of the consumed fact, unique within a consumer alongside the EventId instance identity.",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldComment: "Deterministic BusinessMES idempotency key unique within a consumer.");

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                schema: "mes",
                table: "processed_integration_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                comment: "Source integration event identifier; unique per consumer as the event-instance identity alongside the IdempotencyKey business identity.",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldComment: "Source integration event identifier retained for traceability; idempotency uses IdempotencyKey.");

            // 该唯一索引曾被 20260624151023_UseIdempotencyKeyForProcessedIntegrationEvents 移除，合法现存库可能已有
            // 同 consumer / 同 EventId 的多行。同一条历史 migration 在移除它的同时建立了
            // (ConsumerName, IdempotencyKey) 唯一索引，因此这些多行的 IdempotencyKey 必然两两不同——
            // 「同 EventId 且同 IdempotencyKey 的真重复」在任何合法 schema 版本上都不可表示，不存在可自动清理的分支。
            // 按 docs/runbooks/database-release.md §6.3：同 EventId、不同 IdempotencyKey 是语义歧义，migration
            // fail-closed 中止并逐行列出冲突，由运维显式裁决；任何歧义组存在时整条 migration 回滚，既不删行也不建索引。
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    ambiguous_rows text;
                BEGIN
                    SELECT string_agg(
                               format('%s / %s / %s / %s',
                                      conflict."ConsumerName", conflict."EventId", conflict."IdempotencyKey",
                                      to_char(conflict."ProcessedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')),
                               E'\n' ORDER BY conflict."ConsumerName", conflict."EventId", conflict."ProcessedAtUtc", conflict."Id")
                      INTO ambiguous_rows
                    FROM mes.processed_integration_events AS conflict
                    WHERE EXISTS (
                        SELECT 1
                        FROM mes.processed_integration_events AS peer
                        WHERE peer."ConsumerName" = conflict."ConsumerName"
                          AND peer."EventId" = conflict."EventId"
                          AND peer."IdempotencyKey" <> conflict."IdempotencyKey");
                    IF ambiguous_rows IS NOT NULL THEN
                        -- 冲突清单放在 MESSAGE 而不是 DETAIL：Npgsql 默认把 DETAIL 脱敏为
                        -- "Detail redacted as it may contain sensitive data"，运维在 migrator 输出里将看不到要裁决的行。
                        RAISE EXCEPTION USING
                            ERRCODE = 'integrity_constraint_violation',
                            MESSAGE = 'AddMesProcessedEventInstanceIdentity aborted: mes.processed_integration_events has rows sharing ConsumerName + EventId with different IdempotencyKey values. Resolve them explicitly (see docs/runbooks/database-release.md §6.3) and re-run the migration. ConsumerName / EventId / IdempotencyKey / ProcessedAtUtc:' || E'\n' || ambiguous_rows;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_processed_integration_events_consumer_event_id",
                schema: "mes",
                table: "processed_integration_events",
                columns: new[] { "ConsumerName", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_processed_integration_events_consumer_event_id",
                schema: "mes",
                table: "processed_integration_events");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                schema: "mes",
                table: "processed_integration_events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                comment: "Deterministic BusinessMES idempotency key unique within a consumer.",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldComment: "Deterministic cross-version business identity of the consumed fact, unique within a consumer alongside the EventId instance identity.");

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                schema: "mes",
                table: "processed_integration_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                comment: "Source integration event identifier retained for traceability; idempotency uses IdempotencyKey.",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldComment: "Source integration event identifier; unique per consumer as the event-instance identity alongside the IdempotencyKey business identity.");
        }
    }
}
