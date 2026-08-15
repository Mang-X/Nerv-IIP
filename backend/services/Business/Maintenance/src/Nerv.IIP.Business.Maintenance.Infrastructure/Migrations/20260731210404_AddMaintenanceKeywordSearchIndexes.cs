using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceKeywordSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql("""
                CREATE INDEX ix_maintenance_work_orders_search_device_trgm
                    ON maintenance.maintenance_work_orders USING gin (lower(device_asset_id) gin_trgm_ops);
                CREATE INDEX ix_maintenance_work_orders_search_alarm_trgm
                    ON maintenance.maintenance_work_orders USING gin (lower(source_alarm_id) gin_trgm_ops);
                CREATE INDEX ix_maintenance_work_orders_search_reference_trgm
                    ON maintenance.maintenance_work_orders USING gin (lower(source_reference_id) gin_trgm_ops);
                CREATE INDEX ix_maintenance_work_orders_search_technician_trgm
                    ON maintenance.maintenance_work_orders USING gin (lower(assigned_technician_user_id) gin_trgm_ops);
                CREATE INDEX ix_maintenance_work_orders_search_team_trgm
                    ON maintenance.maintenance_work_orders USING gin (lower(assigned_team_id) gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS maintenance.ix_maintenance_work_orders_search_device_trgm;
                DROP INDEX IF EXISTS maintenance.ix_maintenance_work_orders_search_alarm_trgm;
                DROP INDEX IF EXISTS maintenance.ix_maintenance_work_orders_search_reference_trgm;
                DROP INDEX IF EXISTS maintenance.ix_maintenance_work_orders_search_technician_trgm;
                DROP INDEX IF EXISTS maintenance.ix_maintenance_work_orders_search_team_trgm;
                """);
        }
    }
}
