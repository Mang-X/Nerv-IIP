using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningSuggestionReleaseDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "release_date",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "date",
                nullable: true,
                comment: "MRP-calculated release date after lead-time offset.");

            migrationBuilder.Sql("""
                UPDATE demand_planning.planning_suggestions
                SET release_date = required_date
                WHERE release_date IS NULL;
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "release_date",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "date",
                nullable: false,
                comment: "MRP-calculated release date after lead-time offset.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "MRP-calculated release date after lead-time offset.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "release_date",
                schema: "demand_planning",
                table: "planning_suggestions");
        }
    }
}
