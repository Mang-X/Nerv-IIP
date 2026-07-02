using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "code_rules",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Code rule aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the code rule."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the code rule is valid."),
                    rule_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Stable code rule key used by application create commands."),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Human-readable code rule name."),
                    applies_to = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Resource or document type governed by the code rule."),
                    scope = table.Column<int>(type: "integer", nullable: false, comment: "Bit flags describing the allocation scope dimensions."),
                    segments = table.Column<string>(type: "jsonb", nullable: false, comment: "Ordered code rule segment definition JSON."),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this code rule is active for new allocations."),
                    version = table.Column<int>(type: "integer", nullable: false, comment: "Code rule definition version."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the code rule was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the code rule was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_rules", x => x.id);
                },
                comment: "Business master data code generation rules available to the coding engine.");

            migrationBuilder.CreateIndex(
                name: "ux_code_rules_scope",
                schema: "business_masterdata",
                table: "code_rules",
                columns: new[] { "organization_id", "environment_id", "rule_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "code_rules",
                schema: "business_masterdata");
        }
    }
}
