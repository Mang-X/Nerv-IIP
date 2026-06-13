using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStandardOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "operation_code",
                schema: "product_engineering",
                table: "routing_operations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Standard operation code snapshot captured when the routing version was released.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "MasterData reference-data operation code.");

            migrationBuilder.CreateTable(
                name: "standard_operations",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Standard operation aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the standard operation is valid."),
                    operation_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Standard operation business code."),
                    operation_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Standard operation display name."),
                    default_work_center_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Default MasterData work center code used to prefill routing operation rows."),
                    standard_setup_minutes = table.Column<int>(type: "integer", nullable: false, comment: "Default setup duration in minutes before regular operation run time."),
                    standard_run_minutes = table.Column<int>(type: "integer", nullable: false, comment: "Default run duration in minutes for this standard operation."),
                    control_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Control key or control profile for reporting, quality or outsourcing behavior."),
                    requires_reporting = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether MES reporting is expected for this operation by default."),
                    requires_quality_inspection = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether quality inspection is expected for this operation by default."),
                    is_outsourced = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this operation is normally outsourced."),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Optional standard operation description."),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this standard operation can be selected for new routing authoring."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the standard operation was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the standard operation was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_standard_operations", x => x.id);
                },
                comment: "ProductEngineering standard operation master data with default work center, control flags and standard times.");

            migrationBuilder.CreateIndex(
                name: "IX_standard_operations_organization_id_environment_id_enabled",
                schema: "product_engineering",
                table: "standard_operations",
                columns: new[] { "organization_id", "environment_id", "enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_standard_operations_organization_id_environment_id_operatio~",
                schema: "product_engineering",
                table: "standard_operations",
                columns: new[] { "organization_id", "environment_id", "operation_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "standard_operations",
                schema: "product_engineering");

            migrationBuilder.AlterColumn<string>(
                name: "operation_code",
                schema: "product_engineering",
                table: "routing_operations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "MasterData reference-data operation code.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "Standard operation code snapshot captured when the routing version was released.");
        }
    }
}
