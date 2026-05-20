using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleNormalizedName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedRoleName",
                schema: "iam",
                table: "roles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                comment: "Case-insensitive normalized role name.");

            migrationBuilder.Sql("UPDATE iam.roles SET \"NormalizedRoleName\" = upper(trim(\"RoleName\"));");

            migrationBuilder.DropIndex(
                name: "IX_roles_RoleName",
                schema: "iam",
                table: "roles");

            migrationBuilder.CreateIndex(
                name: "IX_roles_NormalizedRoleName",
                schema: "iam",
                table: "roles",
                column: "NormalizedRoleName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_roles_NormalizedRoleName",
                schema: "iam",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "NormalizedRoleName",
                schema: "iam",
                table: "roles");

            migrationBuilder.CreateIndex(
                name: "IX_roles_RoleName",
                schema: "iam",
                table: "roles",
                column: "RoleName",
                unique: true);
        }
    }
}
