using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeRuleVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "code_rule_versions",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Code rule version record id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the code rule version."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the code rule version is valid."),
                    rule_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Stable code rule key governed by this version."),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Human-readable code rule name for this version."),
                    applies_to = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Resource or document type governed by this version."),
                    scope = table.Column<int>(type: "integer", nullable: false, comment: "Bit flags describing allocation scope dimensions for this version."),
                    segments = table.Column<string>(type: "jsonb", nullable: false, comment: "Ordered code rule segment definition JSON for this version."),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this version allows new allocations when effective."),
                    version = table.Column<int>(type: "integer", nullable: false, comment: "Monotonic version number within organization, environment and rule key."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Governance status for this version, such as active or scheduled."),
                    effective_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC instant when this version may become effective for new allocations."),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Operator or system principal that created this version."),
                    change_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Audited reason for the code rule configuration change."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when this version record was created.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_rule_versions", x => x.id);
                },
                comment: "Versioned audit records for business master data code rule configuration changes.");

            migrationBuilder.CreateIndex(
                name: "ix_code_rule_versions_effective",
                schema: "business_masterdata",
                table: "code_rule_versions",
                columns: new[] { "organization_id", "environment_id", "rule_key", "effective_from_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_code_rule_versions_scope_version",
                schema: "business_masterdata",
                table: "code_rule_versions",
                columns: new[] { "organization_id", "environment_id", "rule_key", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "code_rule_versions",
                schema: "business_masterdata");
        }
    }
}
