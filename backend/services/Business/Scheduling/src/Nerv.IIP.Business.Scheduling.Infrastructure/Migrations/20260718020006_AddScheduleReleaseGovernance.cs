using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleReleaseGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "schedule_plans",
                schema: "scheduling",
                comment: "BusinessScheduling generated, released, superseded, and revoked schedule plan headers.",
                oldComment: "BusinessScheduling generated and released schedule plan headers.");

            migrationBuilder.AddColumn<long>(
                name: "release_revision",
                schema: "scheduling",
                table: "schedule_plans",
                type: "bigint",
                nullable: true,
                comment: "Monotonic release revision within the organization and environment scope.");

            migrationBuilder.AddColumn<string>(
                name: "revocation_reason",
                schema: "scheduling",
                table: "schedule_plans",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "Released plan withdrawal reason: Superseded or Explicit.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "revoked_at_utc",
                schema: "scheduling",
                table: "schedule_plans",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC timestamp when the released plan was superseded or explicitly revoked.");

            migrationBuilder.AddColumn<string>(
                name: "superseded_by_plan_id",
                schema: "scheduling",
                table: "schedule_plans",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true,
                comment: "Successor schedule plan id for automatic supersession; null for explicit revoke.");

            migrationBuilder.Sql(
                """
                WITH ranked_releases AS (
                    SELECT
                        id,
                        ROW_NUMBER() OVER (
                            PARTITION BY organization_id, environment_id
                            ORDER BY released_at_utc ASC NULLS FIRST, generated_at_utc ASC, plan_id ASC) AS release_revision,
                        ROW_NUMBER() OVER (
                            PARTITION BY organization_id, environment_id
                            ORDER BY released_at_utc DESC NULLS LAST, generated_at_utc DESC, plan_id DESC) AS active_rank,
                        FIRST_VALUE(plan_id) OVER (
                            PARTITION BY organization_id, environment_id
                            ORDER BY released_at_utc DESC NULLS LAST, generated_at_utc DESC, plan_id DESC) AS active_plan_id,
                        FIRST_VALUE(COALESCE(released_at_utc, generated_at_utc)) OVER (
                            PARTITION BY organization_id, environment_id
                            ORDER BY released_at_utc DESC NULLS LAST, generated_at_utc DESC, plan_id DESC) AS active_at_utc
                    FROM scheduling.schedule_plans
                    WHERE status = 'Released'
                )
                UPDATE scheduling.schedule_plans AS plan
                SET
                    release_revision = ranked.release_revision,
                    status = CASE WHEN ranked.active_rank = 1 THEN 'Released' ELSE 'Superseded' END,
                    revoked_at_utc = CASE WHEN ranked.active_rank = 1 THEN NULL ELSE ranked.active_at_utc END,
                    revocation_reason = CASE WHEN ranked.active_rank = 1 THEN NULL ELSE 'Superseded' END,
                    superseded_by_plan_id = CASE WHEN ranked.active_rank = 1 THEN NULL ELSE ranked.active_plan_id END
                FROM ranked_releases AS ranked
                WHERE plan.id = ranked.id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_schedule_plans_organization_id_environment_id",
                schema: "scheduling",
                table: "schedule_plans",
                columns: new[] { "organization_id", "environment_id" },
                unique: true,
                filter: "status = 'Released'");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_plans_organization_id_environment_id_release_revis~",
                schema: "scheduling",
                table: "schedule_plans",
                columns: new[] { "organization_id", "environment_id", "release_revision" },
                unique: true,
                filter: "release_revision IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_schedule_plans_organization_id_environment_id",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropIndex(
                name: "IX_schedule_plans_organization_id_environment_id_release_revis~",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.Sql(
                """
                UPDATE scheduling.schedule_plans
                SET status = 'Released'
                WHERE status IN ('Superseded', 'Revoked');
                """);

            migrationBuilder.DropColumn(
                name: "release_revision",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "revocation_reason",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "revoked_at_utc",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "superseded_by_plan_id",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.AlterTable(
                name: "schedule_plans",
                schema: "scheduling",
                comment: "BusinessScheduling generated and released schedule plan headers.",
                oldComment: "BusinessScheduling generated, released, superseded, and revoked schedule plan headers.");
        }
    }
}
