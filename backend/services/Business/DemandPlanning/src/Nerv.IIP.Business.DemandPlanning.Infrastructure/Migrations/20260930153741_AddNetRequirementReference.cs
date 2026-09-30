using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNetRequirementReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "net_requirement_reference",
                schema: "demand_planning",
                table: "planning_suggestions",
                type: "uuid",
                nullable: true,
                comment: "Immutable netting event reference shared by lot-sized batches; null for legacy or non-netted suggestions.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "net_requirement_reference",
                schema: "demand_planning",
                table: "planning_suggestions");
        }
    }
}
