using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionTaskFirstArticleSourceType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "source_type",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Task source type: receiving, operation, final or first-article.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Task source type: receiving, operation or final.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "source_type",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Task source type: receiving, operation or final.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Task source type: receiving, operation, final or first-article.");
        }
    }
}
