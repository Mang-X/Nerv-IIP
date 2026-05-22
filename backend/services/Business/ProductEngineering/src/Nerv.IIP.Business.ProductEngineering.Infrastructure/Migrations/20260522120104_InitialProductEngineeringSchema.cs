using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialProductEngineeringSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "product_engineering");

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "product_engineering",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Instance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastLockTime = table.Column<DateTime>(type: "TIMESTAMP", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_locks", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "cap_published_messages",
                schema: "product_engineering",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_published_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cap_received_messages",
                schema: "product_engineering",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_received_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "production_versions",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Production version aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the version is valid."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Finished or semi-finished SKU code."),
                    mbom_version_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Released manufacturing BOM version id."),
                    routing_version_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Released routing version id."),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false, comment: "First effective production date."),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true, comment: "Last effective production date, null for open-ended."),
                    lot_size_min = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Minimum lot size covered by this version."),
                    lot_size_max = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Maximum lot size covered by this version."),
                    priority = table.Column<int>(type: "integer", nullable: false, comment: "Selection priority among matching production versions."),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, comment: "Default version flag for the SKU and effective window."),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Production version lifecycle status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the version was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the version was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_versions", x => x.id);
                },
                comment: "ProductEngineering production versions binding released MBOM and routing versions for planning and MES work order creation.");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "product_engineering",
                table: "cap_published_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "product_engineering",
                table: "cap_published_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "product_engineering",
                table: "cap_received_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "product_engineering",
                table: "cap_received_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_production_versions_organization_id_environment_id_mbom_ver~",
                schema: "product_engineering",
                table: "production_versions",
                columns: new[] { "organization_id", "environment_id", "mbom_version_id", "routing_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_production_versions_organization_id_environment_id_sku_cod~1",
                schema: "product_engineering",
                table: "production_versions",
                columns: new[] { "organization_id", "environment_id", "sku_code", "is_default", "valid_from", "valid_to" });

            migrationBuilder.CreateIndex(
                name: "IX_production_versions_organization_id_environment_id_sku_code~",
                schema: "product_engineering",
                table: "production_versions",
                columns: new[] { "organization_id", "environment_id", "sku_code", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "production_versions",
                schema: "product_engineering");
        }
    }
}
