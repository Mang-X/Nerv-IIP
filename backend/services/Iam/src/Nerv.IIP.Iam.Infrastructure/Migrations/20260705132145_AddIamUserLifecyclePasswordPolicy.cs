using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIamUserLifecyclePasswordPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AccountExpiresAtUtc",
                schema: "iam",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Optional UTC time after which the account can no longer authenticate.");

            migrationBuilder.AddColumn<bool>(
                name: "PasswordChangeRequired",
                schema: "iam",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the user must change password after login before normal use.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordChangedAtUtc",
                schema: "iam",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the current password hash was set.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasswordExpiresAtUtc",
                schema: "iam",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Optional UTC time after which login must force password change.");

            migrationBuilder.CreateTable(
                name: "user_password_history",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Password history row identifier."),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "User identifier that owns this historical password hash."),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Historical password hash retained for password history policy checks."),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when this historical password hash was superseded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_password_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_password_history_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "iam",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Historical IAM user password hashes used to prevent recent password reuse.");

            migrationBuilder.CreateIndex(
                name: "IX_users_AccountExpiresAtUtc",
                schema: "iam",
                table: "users",
                column: "AccountExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_user_password_history_UserId_CreatedAtUtc",
                schema: "iam",
                table: "user_password_history",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_password_history",
                schema: "iam");

            migrationBuilder.DropIndex(
                name: "IX_users_AccountExpiresAtUtc",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "AccountExpiresAtUtc",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PasswordChangeRequired",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAtUtc",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PasswordExpiresAtUtc",
                schema: "iam",
                table: "users");
        }
    }
}
