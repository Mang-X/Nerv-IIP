using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchedulingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "scheduling",
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
                schema: "scheduling",
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
                schema: "scheduling",
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
                name: "schedule_plans",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Schedule plan aggregate row id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    plan_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Public schedule plan id."),
                    problem_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Public scheduling problem id used to generate this plan."),
                    problem_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Deterministic fingerprint of the scheduling problem input."),
                    algorithm_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "APS lite algorithm version used to generate the plan."),
                    contract_version = table.Column<int>(type: "integer", nullable: false, comment: "Schedule plan contract version."),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Persisted plan lifecycle status."),
                    generated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the plan was generated."),
                    released_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the plan was released.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_plans", x => x.id);
                },
                comment: "BusinessScheduling generated and released schedule plan headers.");

            migrationBuilder.CreateTable(
                name: "schedule_problems",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Scheduling problem snapshot row id."),
                    problem_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Public scheduling problem id."),
                    contract_version = table.Column<int>(type: "integer", nullable: false, comment: "Scheduling problem contract version."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    problem_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Deterministic fingerprint of the scheduling problem input."),
                    horizon_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Scheduling horizon start timestamp in UTC."),
                    horizon_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Scheduling horizon end timestamp in UTC."),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the problem snapshot was captured.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_problems", x => x.id);
                },
                comment: "BusinessScheduling normalized scheduling problem snapshots.");

            migrationBuilder.CreateTable(
                name: "schedule_plan_assignments",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Schedule plan assignment row id."),
                    schedule_plan_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning schedule plan aggregate id."),
                    assignment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Public assignment id."),
                    work_order_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Public work order reference."),
                    operation_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Public operation reference."),
                    operation_sequence = table.Column<int>(type: "integer", nullable: false, comment: "Operation sequence within the work order route."),
                    resource_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Assigned resource id."),
                    work_center_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Assigned work center id."),
                    start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Assignment start timestamp in UTC."),
                    end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Assignment end timestamp in UTC."),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this assignment came from a locked input."),
                    explanation_code = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Scheduling explanation code.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_plan_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_schedule_plan_assignments_schedule_plans_schedule_plan_id",
                        column: x => x.schedule_plan_id,
                        principalSchema: "scheduling",
                        principalTable: "schedule_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "BusinessScheduling operation assignments in a schedule plan.");

            migrationBuilder.CreateTable(
                name: "schedule_plan_conflicts",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Schedule plan conflict row id."),
                    schedule_plan_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning schedule plan aggregate id."),
                    conflict_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Public conflict id."),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Conflict reason code."),
                    severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Conflict severity."),
                    work_order_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Affected work order reference."),
                    operation_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Affected operation reference."),
                    resource_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Affected resource reference."),
                    message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Human-readable conflict explanation.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_plan_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "FK_schedule_plan_conflicts_schedule_plans_schedule_plan_id",
                        column: x => x.schedule_plan_id,
                        principalSchema: "scheduling",
                        principalTable: "schedule_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "BusinessScheduling conflicts detected while generating a schedule plan.");

            migrationBuilder.CreateTable(
                name: "schedule_plan_resource_loads",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Schedule plan resource load row id."),
                    schedule_plan_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning schedule plan aggregate id."),
                    resource_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Resource id for the load window."),
                    window_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Load window start timestamp in UTC."),
                    window_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Load window end timestamp in UTC."),
                    assigned_minutes = table.Column<int>(type: "integer", nullable: false, comment: "Assigned production minutes in the window."),
                    available_minutes = table.Column<int>(type: "integer", nullable: false, comment: "Available capacity minutes in the window."),
                    utilization = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Assigned minutes divided by available minutes.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_plan_resource_loads", x => x.id);
                    table.ForeignKey(
                        name: "FK_schedule_plan_resource_loads_schedule_plans_schedule_plan_id",
                        column: x => x.schedule_plan_id,
                        principalSchema: "scheduling",
                        principalTable: "schedule_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "BusinessScheduling resource load windows for a schedule plan.");

            migrationBuilder.CreateTable(
                name: "schedule_plan_unscheduled_operations",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Schedule plan unscheduled operation row id."),
                    schedule_plan_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning schedule plan aggregate id."),
                    work_order_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Public work order reference."),
                    operation_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Public operation reference."),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Reason code explaining why the operation was not scheduled."),
                    message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Human-readable unscheduled operation explanation.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_plan_unscheduled_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_schedule_plan_unscheduled_operations_schedule_plans_schedul~",
                        column: x => x.schedule_plan_id,
                        principalSchema: "scheduling",
                        principalTable: "schedule_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "BusinessScheduling operations that could not be assigned inside the plan horizon.");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "scheduling",
                table: "cap_published_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "scheduling",
                table: "cap_published_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "scheduling",
                table: "cap_received_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "scheduling",
                table: "cap_received_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_schedule_plan_assignments_schedule_plan_id_assignment_id",
                schema: "scheduling",
                table: "schedule_plan_assignments",
                columns: new[] { "schedule_plan_id", "assignment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schedule_plan_conflicts_schedule_plan_id",
                schema: "scheduling",
                table: "schedule_plan_conflicts",
                column: "schedule_plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_plan_resource_loads_schedule_plan_id",
                schema: "scheduling",
                table: "schedule_plan_resource_loads",
                column: "schedule_plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_plan_unscheduled_operations_schedule_plan_id",
                schema: "scheduling",
                table: "schedule_plan_unscheduled_operations",
                column: "schedule_plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_plans_plan_id",
                schema: "scheduling",
                table: "schedule_plans",
                column: "plan_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schedule_problems_organization_id_environment_id_problem_id",
                schema: "scheduling",
                table: "schedule_problems",
                columns: new[] { "organization_id", "environment_id", "problem_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "schedule_plan_assignments",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "schedule_plan_conflicts",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "schedule_plan_resource_loads",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "schedule_plan_unscheduled_operations",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "schedule_problems",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "schedule_plans",
                schema: "scheduling");
        }
    }
}
