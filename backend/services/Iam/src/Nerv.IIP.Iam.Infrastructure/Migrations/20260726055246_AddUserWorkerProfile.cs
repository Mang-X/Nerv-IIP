using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserWorkerProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DepartmentName",
                schema: "iam",
                table: "users",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Optional department display name shown in the worker directory.");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "iam",
                table: "users",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Optional worker display name shown in the worker directory.");

            migrationBuilder.AddColumn<string>(
                name: "EmployeeNo",
                schema: "iam",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Optional employee number shown in the worker directory.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepartmentName",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "EmployeeNo",
                schema: "iam",
                table: "users");
        }
    }
}
