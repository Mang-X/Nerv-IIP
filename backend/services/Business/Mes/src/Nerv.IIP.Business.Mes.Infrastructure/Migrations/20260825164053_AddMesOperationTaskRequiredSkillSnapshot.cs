using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesOperationTaskRequiredSkillSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "required_skill_code",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional MasterData skill code frozen from the published routing snapshot when the work order is converted.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "required_skill_code",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
