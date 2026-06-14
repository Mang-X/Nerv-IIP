using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityReasonCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quality_reasons",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Quality reason aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the quality reason."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the quality reason is valid."),
                    reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique quality reason code."),
                    reason_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Quality reason display name."),
                    group_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Reason group name for catalog organization."),
                    severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Severity classification: minor, major or critical."),
                    default_disposition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional default NCR disposition aligned with supported NCR disposition types."),
                    enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Enabled flag that makes the reason selectable."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the quality reason was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the quality reason was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quality_reasons", x => x.id);
                },
                comment: "Quality reason code catalog with grouping, severity and default disposition.");

            migrationBuilder.CreateIndex(
                name: "IX_quality_reasons_enabled",
                schema: "quality",
                table: "quality_reasons",
                column: "enabled");

            migrationBuilder.CreateIndex(
                name: "IX_quality_reasons_organization_id_environment_id_group_name_e~",
                schema: "quality",
                table: "quality_reasons",
                columns: new[] { "organization_id", "environment_id", "group_name", "enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_quality_reasons_organization_id_environment_id_reason_code",
                schema: "quality",
                table: "quality_reasons",
                columns: new[] { "organization_id", "environment_id", "reason_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quality_reasons",
                schema: "quality");
        }
    }
}
