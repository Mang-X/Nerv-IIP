using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesAssignedUserName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assigned_user_name",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Display name snapshot of the assigned worker captured by MES dispatch.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assigned_user_name",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
