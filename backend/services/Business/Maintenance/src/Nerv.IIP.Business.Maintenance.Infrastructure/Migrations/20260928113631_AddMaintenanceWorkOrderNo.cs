using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceWorkOrderNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #3852：维修工单正式单号。先加可空列，给存量工单补号，再收紧为非空并建唯一索引。
            migrationBuilder.AddColumn<string>(
                name: "work_order_no",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Formal maintenance work order number allocated by the maintenance-work-order code rule; unique per organization and environment.");

            // 存量补号：
            // 1. 设定集演示数据已在 source_reference_id 上带 MWO- 单号（MWO-2026-####、MWO-DEMO-001），原样沿用
            //    （同组织环境内重复时只给最早一张，其余走第 2 步）；
            // 2. 其余工单按开单日（UTC）与编码规则同形补号 MWO-yyyyMMdd-NNNNNN，日内按开单时间、ID 排序；
            // 3. 把各日计数器推到已用的最大序号，之后新分配的单号不会与补出的号相撞。
            migrationBuilder.Sql("""
                WITH seeded AS (
                    SELECT id,
                           source_reference_id AS work_order_no,
                           row_number() OVER (
                               PARTITION BY organization_id, environment_id, source_reference_id
                               ORDER BY opened_at_utc, id) AS rn
                    FROM maintenance.maintenance_work_orders
                    WHERE source_reference_id LIKE 'MWO-%'
                      AND char_length(source_reference_id) <= 50
                )
                UPDATE maintenance.maintenance_work_orders AS w
                SET work_order_no = seeded.work_order_no
                FROM seeded
                WHERE w.id = seeded.id AND seeded.rn = 1;

                WITH numbered AS (
                    SELECT id,
                           to_char(opened_at_utc AT TIME ZONE 'UTC', 'YYYYMMDD') AS opened_day,
                           row_number() OVER (
                               PARTITION BY organization_id, environment_id, to_char(opened_at_utc AT TIME ZONE 'UTC', 'YYYYMMDD')
                               ORDER BY opened_at_utc, id) AS seq
                    FROM maintenance.maintenance_work_orders
                    WHERE work_order_no IS NULL
                )
                UPDATE maintenance.maintenance_work_orders AS w
                SET work_order_no = 'MWO-' || numbered.opened_day || '-' || lpad(numbered.seq::text, 6, '0')
                FROM numbered
                WHERE w.id = numbered.id;

                INSERT INTO maintenance.code_counters
                    (organization_id, environment_id, rule_key, site_code, reset_key, current_value, version)
                SELECT organization_id,
                       environment_id,
                       'maintenance-work-order',
                       '',
                       substr(work_order_no, 5, 8),
                       max(substr(work_order_no, 14)::bigint),
                       0
                FROM maintenance.maintenance_work_orders
                WHERE work_order_no ~ '^MWO-[0-9]{8}-[0-9]{6}$'
                GROUP BY organization_id, environment_id, substr(work_order_no, 5, 8)
                ON CONFLICT (organization_id, environment_id, rule_key, site_code, reset_key)
                DO UPDATE SET current_value = GREATEST(maintenance.code_counters.current_value, EXCLUDED.current_value);
                """);

            migrationBuilder.AlterColumn<string>(
                name: "work_order_no",
                schema: "maintenance",
                table: "maintenance_work_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Formal maintenance work order number allocated by the maintenance-work-order code rule; unique per organization and environment.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "Formal maintenance work order number allocated by the maintenance-work-order code rule; unique per organization and environment.");

            migrationBuilder.CreateIndex(
                name: "ux_maintenance_work_orders_work_order_no",
                schema: "maintenance",
                table: "maintenance_work_orders",
                columns: new[] { "organization_id", "environment_id", "work_order_no" },
                unique: true);

            // 关键字按单号子串检索，与其余五个关键字列一样走 pg_trgm 表达式索引（扩展已由 AddMaintenanceKeywordSearchIndexes 建好）。
            migrationBuilder.Sql("""
                CREATE INDEX ix_maintenance_work_orders_search_work_order_no_trgm
                    ON maintenance.maintenance_work_orders USING gin (lower(work_order_no) gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS maintenance.ix_maintenance_work_orders_search_work_order_no_trgm;");

            migrationBuilder.DropIndex(
                name: "ux_maintenance_work_orders_work_order_no",
                schema: "maintenance",
                table: "maintenance_work_orders");

            migrationBuilder.DropColumn(
                name: "work_order_no",
                schema: "maintenance",
                table: "maintenance_work_orders");
        }
    }
}
