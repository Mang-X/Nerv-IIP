using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIamAuthRefreshConsumeAndLoginLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_sessions_RefreshTokenHash",
                schema: "iam",
                table: "user_sessions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastFailedLoginAtUtc",
                schema: "iam",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Last failed login time in UTC.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutUntilUtc",
                schema: "iam",
                table: "users",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time until which password login is locked after consecutive failures.");

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_RefreshTokenHash",
                schema: "iam",
                table: "user_sessions",
                column: "RefreshTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_sessions_RefreshTokenHash",
                schema: "iam",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "LastFailedLoginAtUtc",
                schema: "iam",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LockoutUntilUtc",
                schema: "iam",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_RefreshTokenHash",
                schema: "iam",
                table: "user_sessions",
                column: "RefreshTokenHash");
        }
    }
}
