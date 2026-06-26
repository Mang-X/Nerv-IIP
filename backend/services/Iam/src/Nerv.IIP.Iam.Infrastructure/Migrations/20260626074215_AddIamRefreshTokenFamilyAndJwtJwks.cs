using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIamRefreshTokenFamilyAndJwtJwks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousSessionId",
                schema: "iam",
                table: "user_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Previous session identifier in the refresh token rotation lineage.");

            migrationBuilder.AddColumn<string>(
                name: "TokenFamilyId",
                schema: "iam",
                table: "user_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Refresh token family identifier used to detect replay and revoke the full lineage.");

            migrationBuilder.Sql("""
                UPDATE iam.user_sessions
                SET "TokenFamilyId" = "Id"
                WHERE "TokenFamilyId" IS NULL
                """);

            migrationBuilder.AlterColumn<string>(
                name: "TokenFamilyId",
                schema: "iam",
                table: "user_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "Refresh token family identifier used to detect replay and revoke the full lineage.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true,
                oldComment: "Refresh token family identifier used to detect replay and revoke the full lineage.");

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_PreviousSessionId",
                schema: "iam",
                table: "user_sessions",
                column: "PreviousSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_TokenFamilyId",
                schema: "iam",
                table: "user_sessions",
                column: "TokenFamilyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_sessions_PreviousSessionId",
                schema: "iam",
                table: "user_sessions");

            migrationBuilder.DropIndex(
                name: "IX_user_sessions_TokenFamilyId",
                schema: "iam",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "PreviousSessionId",
                schema: "iam",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "TokenFamilyId",
                schema: "iam",
                table: "user_sessions");
        }
    }
}
