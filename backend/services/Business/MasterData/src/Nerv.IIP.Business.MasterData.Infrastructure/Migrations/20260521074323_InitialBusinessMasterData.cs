using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBusinessMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "business_masterdata");

            migrationBuilder.CreateTable(
                name: "business_partners",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Business partner aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the business partner."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the business partner is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique partner code within the partner type."),
                    partner_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "Business partner type such as supplier, customer or carrier."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Business partner display name."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the partner from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the business partner was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the business partner was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_partners", x => x.id);
                },
                comment: "Business master data partners such as suppliers, customers and carriers.");

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "business_masterdata",
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
                schema: "business_masterdata",
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
                schema: "business_masterdata",
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
                name: "departments",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Department aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the department."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the department is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique department code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Department display name."),
                    parent_department_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Parent department code for hierarchy navigation."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the department from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the department was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the department was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_departments", x => x.id);
                },
                comment: "Business master data organization departments used for ownership and staffing.");

            migrationBuilder.CreateTable(
                name: "device_assets",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Device asset aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the device asset."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the device asset is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique device asset code."),
                    model = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Device model or equipment type."),
                    line_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Production line code where the device asset is installed."),
                    work_center_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Work center code where the device asset is assigned."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the device asset from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the device asset was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the device asset was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_assets", x => x.id);
                },
                comment: "Business master data device assets assigned to production lines and work centers.");

            migrationBuilder.CreateTable(
                name: "personnel_skills",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Personnel skill aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the skill assignment."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the skill assignment is valid."),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "IAM user id assigned to the skill."),
                    skill_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Skill code assigned to the user."),
                    level = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "Skill proficiency level."),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false, comment: "First calendar date when the skill assignment is effective."),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: false, comment: "Last calendar date when the skill assignment is effective."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the skill assignment from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the skill assignment was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the skill assignment was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personnel_skills", x => x.id);
                },
                comment: "Business master data personnel skill assignments with validity dates.");

            migrationBuilder.CreateTable(
                name: "skus",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "SKU aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the SKU."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the SKU is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique SKU code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "SKU display name."),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Default inventory or production unit of measure."),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "SKU category for list filtering and planning."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the SKU from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the SKU was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the SKU was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skus", x => x.id);
                },
                comment: "Business master data stock keeping units used for material and product identification.");

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Team aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the team."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the team is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique team code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Team display name."),
                    department_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Department code that owns the team."),
                    shift_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Shift code normally staffed by the team."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the team from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the team was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the team was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.id);
                },
                comment: "Business master data work teams assigned to departments and shifts.");

            migrationBuilder.CreateTable(
                name: "work_calendars",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work calendar aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the work calendar."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the work calendar is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique work calendar code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Work calendar display name."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the work calendar from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the work calendar was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the work calendar was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_calendars", x => x.id);
                },
                comment: "Business master data work calendars defining recurring available working time.");

            migrationBuilder.CreateTable(
                name: "work_centers",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work center aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the work center."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the work center is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique work center code."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Work center display name."),
                    capacity_minutes_per_day = table.Column<int>(type: "integer", nullable: false, comment: "Nominal available capacity per day in minutes."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the work center from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the work center was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the work center was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_centers", x => x.id);
                },
                comment: "Business master data work centers used for capacity planning and execution routing.");

            migrationBuilder.CreateTable(
                name: "work_calendar_working_times",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work calendar working time row id."),
                    day_of_week = table.Column<int>(type: "integer", nullable: false, comment: "Day of week for the recurring working time."),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false, comment: "Local start time of the working window."),
                    ends_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false, comment: "Local end time of the working window."),
                    work_calendar_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning work calendar aggregate id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_calendar_working_times", x => x.id);
                    table.ForeignKey(
                        name: "FK_work_calendar_working_times_work_calendars_work_calendar_id",
                        column: x => x.work_calendar_id,
                        principalSchema: "business_masterdata",
                        principalTable: "work_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Recurring working time windows owned by a business master data work calendar.");

            migrationBuilder.CreateIndex(
                name: "IX_business_partners_organization_id_environment_id_partner_ty~",
                schema: "business_masterdata",
                table: "business_partners",
                columns: new[] { "organization_id", "environment_id", "partner_type", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_partners_partner_type_disabled",
                schema: "business_masterdata",
                table: "business_partners",
                columns: new[] { "partner_type", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "business_masterdata",
                table: "cap_published_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "business_masterdata",
                table: "cap_published_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "business_masterdata",
                table: "cap_received_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "business_masterdata",
                table: "cap_received_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_departments_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "departments",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_departments_parent_department_code_disabled",
                schema: "business_masterdata",
                table: "departments",
                columns: new[] { "parent_department_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_device_assets_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "device_assets",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_assets_work_center_code_disabled",
                schema: "business_masterdata",
                table: "device_assets",
                columns: new[] { "work_center_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_personnel_skills_organization_id_environment_id_user_id_ski~",
                schema: "business_masterdata",
                table: "personnel_skills",
                columns: new[] { "organization_id", "environment_id", "user_id", "skill_code", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_personnel_skills_skill_code_disabled",
                schema: "business_masterdata",
                table: "personnel_skills",
                columns: new[] { "skill_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_personnel_skills_user_id_disabled",
                schema: "business_masterdata",
                table: "personnel_skills",
                columns: new[] { "user_id", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_skus_category_disabled",
                schema: "business_masterdata",
                table: "skus",
                columns: new[] { "category", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_skus_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "skus",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_department_code_disabled",
                schema: "business_masterdata",
                table: "teams",
                columns: new[] { "department_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_teams_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "teams",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_calendar_working_times_work_calendar_id",
                schema: "business_masterdata",
                table: "work_calendar_working_times",
                column: "work_calendar_id");

            migrationBuilder.CreateIndex(
                name: "IX_work_calendars_disabled",
                schema: "business_masterdata",
                table: "work_calendars",
                column: "disabled");

            migrationBuilder.CreateIndex(
                name: "IX_work_calendars_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "work_calendars",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_centers_disabled",
                schema: "business_masterdata",
                table: "work_centers",
                column: "disabled");

            migrationBuilder.CreateIndex(
                name: "IX_work_centers_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "work_centers",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_partners",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "departments",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "device_assets",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "personnel_skills",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "skus",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "work_calendar_working_times",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "work_centers",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "work_calendars",
                schema: "business_masterdata");
        }
    }
}
