using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkshopAndTeamMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "workshop_code",
                schema: "business_masterdata",
                table: "work_centers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional workshop code that groups the work center under a site.");

            migrationBuilder.AddColumn<string>(
                name: "workshop_code",
                schema: "business_masterdata",
                table: "production_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional workshop code that groups the production line under a site.");

            migrationBuilder.CreateTable(
                name: "team_members",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Team member aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the team membership."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the team membership is valid."),
                    team_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Team code that the IAM user belongs to."),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "IAM user id assigned to the team."),
                    is_leader = table.Column<bool>(type: "boolean", nullable: false, comment: "Flag indicating whether this member leads the team."),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false, comment: "Local business date when the membership starts."),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true, comment: "Optional local business date when the membership ends."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag for removed team memberships."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the team membership was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the team membership was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_members", x => x.id);
                },
                comment: "Business master data team membership facts that relate teams to IAM users.");

            migrationBuilder.CreateTable(
                name: "workshops",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Workshop aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the workshop."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the workshop is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique workshop code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Workshop display name."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Site or plant code that contains the workshop."),
                    manager_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional IAM user id for the workshop manager."),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Optional workshop description for operations and dashboard grouping."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the workshop from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the workshop was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the workshop was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workshops", x => x.id);
                },
                comment: "Business master data workshops used as organizational and area grouping under a site.");

            migrationBuilder.CreateIndex(
                name: "IX_work_centers_workshop_code_disabled",
                schema: "business_masterdata",
                table: "work_centers",
                columns: new[] { "workshop_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_workshop_code_disabled",
                schema: "business_masterdata",
                table: "production_lines",
                columns: new[] { "workshop_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_team_members_organization_id_environment_id_team_code_disab~",
                schema: "business_masterdata",
                table: "team_members",
                columns: new[] { "organization_id", "environment_id", "team_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_team_members_organization_id_environment_id_team_code_user_~",
                schema: "business_masterdata",
                table: "team_members",
                columns: new[] { "organization_id", "environment_id", "team_code", "user_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_members_organization_id_environment_id_user_id_disabled",
                schema: "business_masterdata",
                table: "team_members",
                columns: new[] { "organization_id", "environment_id", "user_id", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_workshops_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "workshops",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workshops_site_code_disabled",
                schema: "business_masterdata",
                table: "workshops",
                columns: new[] { "site_code", "disabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "team_members",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "workshops",
                schema: "business_masterdata");

            migrationBuilder.DropIndex(
                name: "IX_work_centers_workshop_code_disabled",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropIndex(
                name: "IX_production_lines_workshop_code_disabled",
                schema: "business_masterdata",
                table: "production_lines");

            migrationBuilder.DropColumn(
                name: "workshop_code",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "workshop_code",
                schema: "business_masterdata",
                table: "production_lines");
        }
    }
}
