using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNcrClosureAuditFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "close_reason",
                schema: "quality",
                table: "nonconformance_reports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Required audited reason captured when the NCR is closed.");

            migrationBuilder.AddColumn<string>(
                name: "closed_by_actor",
                schema: "quality",
                table: "nonconformance_reports",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Trusted authenticated or system actor that closed the NCR.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "close_reason",
                schema: "quality",
                table: "nonconformance_reports");

            migrationBuilder.DropColumn(
                name: "closed_by_actor",
                schema: "quality",
                table: "nonconformance_reports");
        }
    }
}
