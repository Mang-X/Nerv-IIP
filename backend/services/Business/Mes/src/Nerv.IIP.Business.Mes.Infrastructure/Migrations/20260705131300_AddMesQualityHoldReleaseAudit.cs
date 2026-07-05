using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesQualityHoldReleaseAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "held_at_utc",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the Quality hold was activated.");

            migrationBuilder.AddColumn<string>(
                name: "held_by",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality actor or system source that activated the hold.");

            migrationBuilder.AddColumn<string>(
                name: "held_inspection_record_id",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality inspection record id that originally activated the current or historical hold.");

            migrationBuilder.AddColumn<string>(
                name: "hold_reason",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Reason captured when the Quality hold was activated.");

            migrationBuilder.AddColumn<string>(
                name: "release_inspection_record_id",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality inspection record id that released the hold when release came from inspection results.");

            migrationBuilder.AddColumn<string>(
                name: "release_reason",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Reason recorded when the Quality hold was released.");

            migrationBuilder.AddColumn<string>(
                name: "release_source",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Release source such as quality inspection event type or manual-force-release.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "released_at_utc",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the Quality hold was released.");

            migrationBuilder.AddColumn<string>(
                name: "released_by",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Quality actor, system source or supervisor that released the hold.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "held_at_utc",
                schema: "mes",
                table: "quality_hold_contexts");

            migrationBuilder.DropColumn(
                name: "held_by",
                schema: "mes",
                table: "quality_hold_contexts");

            migrationBuilder.DropColumn(
                name: "held_inspection_record_id",
                schema: "mes",
                table: "quality_hold_contexts");

            migrationBuilder.DropColumn(
                name: "hold_reason",
                schema: "mes",
                table: "quality_hold_contexts");

            migrationBuilder.DropColumn(
                name: "release_inspection_record_id",
                schema: "mes",
                table: "quality_hold_contexts");

            migrationBuilder.DropColumn(
                name: "release_reason",
                schema: "mes",
                table: "quality_hold_contexts");

            migrationBuilder.DropColumn(
                name: "release_source",
                schema: "mes",
                table: "quality_hold_contexts");

            migrationBuilder.DropColumn(
                name: "released_at_utc",
                schema: "mes",
                table: "quality_hold_contexts");

            migrationBuilder.DropColumn(
                name: "released_by",
                schema: "mes",
                table: "quality_hold_contexts");
        }
    }
}
