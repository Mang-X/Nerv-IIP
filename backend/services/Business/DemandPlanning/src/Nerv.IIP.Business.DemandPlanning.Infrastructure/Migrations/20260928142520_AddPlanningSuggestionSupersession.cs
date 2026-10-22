using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningSuggestionSupersession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "superseded_by_run_id",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "uuid",
                nullable: true,
                comment: "Newer completed MRP run that replaced this open suggestion.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "superseded_by_run_id",
                schema: "demand_planning",
                table: "planning_suggestions");
        }
    }
}
