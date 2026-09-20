using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingProcessedEventInstanceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 旧模型自 20260626170713 起已有 (ConsumerName, IdempotencyKey) 唯一索引，因此历史库里同一
            // consumer/eventId 出现多行时，各行的 IdempotencyKey 必然不同：同一事件下曾产生过多个业务键。
            // 删除其中任一行都会让该业务键的 inbox 痕迹消失，事件重投时按 IdempotencyKey 查不到而再次
            // 产生 schedule invalidation，所以这里不做任何自动清理：发现该形状即 fail-closed 中止，把
            // 冲突行列在错误消息里（不用 DETAIL：Npgsql 默认把 DETAIL 脱敏），交由运维显式裁决后重跑。
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    conflicts text;
                BEGIN
                    SELECT string_agg(
                               format('%s/%s: %s', "ConsumerName", "EventId", keys),
                               '; ' ORDER BY "ConsumerName", "EventId")
                    INTO conflicts
                    FROM (
                        SELECT "ConsumerName",
                               "EventId",
                               string_agg(
                                   format('%s@%s', "IdempotencyKey",
                                          to_char("ProcessedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')),
                                   ', ' ORDER BY "ProcessedAtUtc", "Id") AS keys
                        FROM scheduling.processed_integration_events
                        GROUP BY "ConsumerName", "EventId"
                        HAVING COUNT(*) > 1
                    ) AS ambiguous;

                    IF conflicts IS NOT NULL THEN
                        RAISE EXCEPTION USING
                            MESSAGE = format(
                                'AddSchedulingProcessedEventInstanceIdentity aborted: the same ConsumerName/EventId was processed under different IdempotencyKey values; resolve each conflict explicitly before re-running. Conflicts (ConsumerName/EventId: IdempotencyKey@ProcessedAtUtc, ...): %s',
                                conflicts),
                            ERRCODE = 'integrity_constraint_violation';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_processed_integration_events_consumer_event_id",
                schema: "scheduling",
                table: "processed_integration_events",
                columns: new[] { "ConsumerName", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_processed_integration_events_consumer_event_id",
                schema: "scheduling",
                table: "processed_integration_events");
        }
    }
}
