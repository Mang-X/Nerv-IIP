using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityCapaAutomationAndVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "close_approval_chain_id",
                schema: "quality",
                table: "corrective_actions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional BusinessApproval chain id that approved CAPA closure.");

            migrationBuilder.AddColumn<Guid>(
                name: "effectiveness_inspection_record_id",
                schema: "quality",
                table: "corrective_actions",
                type: "uuid",
                nullable: true,
                comment: "Passed Quality inspection record that verifies CAPA effectiveness.");

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_close_approval_chain",
                schema: "quality",
                table: "corrective_actions",
                columns: new[] { "organization_id", "environment_id", "close_approval_chain_id" });

            migrationBuilder.CreateIndex(
                name: "ix_corrective_actions_effectiveness_inspection",
                schema: "quality",
                table: "corrective_actions",
                columns: new[] { "organization_id", "environment_id", "effectiveness_inspection_record_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_corrective_actions_close_approval_chain",
                schema: "quality",
                table: "corrective_actions");

            migrationBuilder.DropIndex(
                name: "ix_corrective_actions_effectiveness_inspection",
                schema: "quality",
                table: "corrective_actions");

            migrationBuilder.DropColumn(
                name: "close_approval_chain_id",
                schema: "quality",
                table: "corrective_actions");

            migrationBuilder.DropColumn(
                name: "effectiveness_inspection_record_id",
                schema: "quality",
                table: "corrective_actions");
        }
    }
}
