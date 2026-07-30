using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderUrgencyBucketLookupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_order_urgency_snapshot_scope_order_bucket",
                schema: "scheduling",
                table: "order_urgency_snapshots",
                columns: new[] { "organization_id", "environment_id", "order_id", "calculation_bucket_utc" });

            migrationBuilder.Sql(
                "COMMENT ON INDEX scheduling.ix_order_urgency_snapshot_scope_order_bucket IS 'Supports persisted per-order calculation-bucket lookups used to skip duplicate refreshes.';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_order_urgency_snapshot_scope_order_bucket",
                schema: "scheduling",
                table: "order_urgency_snapshots");
        }
    }
}
