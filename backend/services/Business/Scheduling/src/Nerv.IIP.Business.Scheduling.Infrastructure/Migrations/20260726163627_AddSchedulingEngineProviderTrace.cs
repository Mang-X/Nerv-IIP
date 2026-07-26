using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingEngineProviderTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "engine_input_fingerprint",
                schema: "scheduling",
                table: "schedule_problems",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Deterministic fingerprint of the exact effective scheduling engine input; null when unavailable for legacy snapshots.");

            migrationBuilder.AddColumn<string>(
                name: "engine_input_json",
                schema: "scheduling",
                table: "schedule_problems",
                type: "jsonb",
                nullable: true,
                comment: "Versioned normalized JSON effective engine input produced by Scheduling and consumed for exact replay; null for legacy snapshots and additive fields are backward compatible.");

            migrationBuilder.AlterColumn<string>(
                name: "algorithm_version",
                schema: "scheduling",
                table: "schedule_plans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "Scheduling engine version used to generate the plan; this remains the sole engine version column.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "APS lite algorithm version used to generate the plan.");

            migrationBuilder.AddColumn<string>(
                name: "constraint_sources_json",
                schema: "scheduling",
                table: "schedule_plans",
                type: "jsonb",
                nullable: false,
                defaultValueSql: """'{"schemaVersion":1,"status":"legacy-unavailable","sources":[]}'::jsonb""",
                comment: "Versioned deterministic JSON constraint source summaries produced by Scheduling and consumed by replay diagnostics; additive fields are backward compatible.");

            migrationBuilder.AddColumn<string>(
                name: "engine_id",
                schema: "scheduling",
                table: "schedule_plans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "finite-capacity",
                comment: "Stable scheduling engine adapter identifier.");

            migrationBuilder.AddColumn<string>(
                name: "replay_status",
                schema: "scheduling",
                table: "schedule_plans",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "legacy-unavailable",
                comment: "Replay availability status; legacy-unavailable explicitly marks plans without exact historical engine input.");

            migrationBuilder.AddColumn<string>(
                name: "rule_profile_id",
                schema: "scheduling",
                table: "schedule_plans",
                type: "character varying(96)",
                maxLength: 96,
                nullable: false,
                defaultValue: "adr-0014-default",
                comment: "Stable rule profile identifier applied before scheduling.");

            migrationBuilder.AddColumn<string>(
                name: "rule_profile_version",
                schema: "scheduling",
                table: "schedule_plans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "v1",
                comment: "Version of the applied rule profile.");

            migrationBuilder.AddColumn<string>(
                name: "rule_provider_id",
                schema: "scheduling",
                table: "schedule_plans",
                type: "character varying(96)",
                maxLength: 96,
                nullable: false,
                defaultValue: "built-in",
                comment: "Stable scheduling rule provider identifier.");

            migrationBuilder.AddColumn<int>(
                name: "trace_schema_version",
                schema: "scheduling",
                table: "schedule_plans",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "Schema version for persisted scheduling execution provenance.");

            migrationBuilder.Sql(
                """
                ALTER TABLE scheduling.schedule_plans
                    ALTER COLUMN constraint_sources_json DROP DEFAULT,
                    ALTER COLUMN engine_id DROP DEFAULT,
                    ALTER COLUMN replay_status DROP DEFAULT,
                    ALTER COLUMN rule_profile_id DROP DEFAULT,
                    ALTER COLUMN rule_profile_version DROP DEFAULT,
                    ALTER COLUMN rule_provider_id DROP DEFAULT,
                    ALTER COLUMN trace_schema_version DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "engine_input_fingerprint",
                schema: "scheduling",
                table: "schedule_problems");

            migrationBuilder.DropColumn(
                name: "engine_input_json",
                schema: "scheduling",
                table: "schedule_problems");

            migrationBuilder.DropColumn(
                name: "constraint_sources_json",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "engine_id",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "replay_status",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "rule_profile_id",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "rule_profile_version",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "rule_provider_id",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "trace_schema_version",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.AlterColumn<string>(
                name: "algorithm_version",
                schema: "scheduling",
                table: "schedule_plans",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "APS lite algorithm version used to generate the plan.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "Scheduling engine version used to generate the plan; this remains the sole engine version column.");
        }
    }
}
