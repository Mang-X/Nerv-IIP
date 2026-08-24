using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicInspectionTimeWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_periodic_inspection_runtime_scope_status_activity",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.AddColumn<long>(
                name: "last_generated_time_window_sequence",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Last atomically generated time-window sequence; zero before generation.");

            migrationBuilder.AddColumn<DateTime>(
                name: "next_time_window_at_utc",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Persisted UTC due time for the next ungenerated time window; null before activity or after closure.");

            migrationBuilder.AddColumn<DateTime>(
                name: "time_schedule_anchor_at_utc",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Frozen UTC first-production anchor after the first time window is generated.");

            migrationBuilder.Sql(
                """
                UPDATE quality.periodic_inspection_runtime_contexts
                SET next_time_window_at_utc = first_activity_at_utc
                    + (time_interval_hours::double precision * INTERVAL '1 hour')
                WHERE status = 'active'
                  AND first_activity_at_utc IS NOT NULL
                  AND time_interval_hours IS NOT NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "source_document_line_id",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true,
                comment: "Optional source document line, operation id or stable periodic-operation window identity.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "Optional source document line or operation id.");

            migrationBuilder.CreateIndex(
                name: "ix_periodic_inspection_runtime_scope_status_next_time",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                columns: new[] { "organization_id", "environment_id", "status", "next_time_window_at_utc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_periodic_inspection_runtime_time_watermark",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                sql: "(last_generated_time_window_sequence = 0 AND time_schedule_anchor_at_utc IS NULL) OR (last_generated_time_window_sequence > 0 AND time_schedule_anchor_at_utc IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_periodic_inspection_runtime_scope_status_next_time",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_periodic_inspection_runtime_time_watermark",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.DropColumn(
                name: "last_generated_time_window_sequence",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.DropColumn(
                name: "next_time_window_at_utc",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.DropColumn(
                name: "time_schedule_anchor_at_utc",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.AlterColumn<string>(
                name: "source_document_line_id",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional source document line or operation id.",
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250,
                oldNullable: true,
                oldComment: "Optional source document line, operation id or stable periodic-operation window identity.");

            migrationBuilder.CreateIndex(
                name: "ix_periodic_inspection_runtime_scope_status_activity",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                columns: new[] { "organization_id", "environment_id", "status", "first_activity_at_utc" });
        }
    }
}
