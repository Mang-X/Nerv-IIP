using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnterpriseIdentityScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_authorization_grants_PrincipalType_PrincipalId_Organization~",
                schema: "iam",
                table: "authorization_grants");

            migrationBuilder.AddColumn<string>(
                name: "AuthenticationMethod",
                schema: "iam",
                table: "user_sessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "password",
                comment: "Authentication method used to issue the session, for example password or oidc.");

            migrationBuilder.AddColumn<string>(
                name: "ExternalProvider",
                schema: "iam",
                table: "user_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "External identity provider name for SSO sessions.");

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubject",
                schema: "iam",
                table: "user_sessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "External provider subject bound to the SSO session.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MfaVerifiedAtUtc",
                schema: "iam",
                table: "user_sessions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when MFA was verified for the session.");

            migrationBuilder.AddColumn<string>(
                name: "ResourceId",
                schema: "iam",
                table: "authorization_grants",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "*",
                comment: "ABAC resource identifier scope. '*' grants every resource id under the resource type.");

            migrationBuilder.AddColumn<string>(
                name: "ResourceType",
                schema: "iam",
                table: "authorization_grants",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "*",
                comment: "ABAC resource type scope. '*' grants every resource type.");

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_ExternalProvider_ExternalSubject",
                schema: "iam",
                table: "user_sessions",
                columns: new[] { "ExternalProvider", "ExternalSubject" });

            migrationBuilder.CreateIndex(
                name: "IX_authorization_grants_PrincipalType_PrincipalId_Organization~",
                schema: "iam",
                table: "authorization_grants",
                columns: new[] { "PrincipalType", "PrincipalId", "OrganizationId", "EnvironmentId", "PermissionCode", "ResourceType", "ResourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_user_sessions_ExternalProvider_ExternalSubject",
                schema: "iam",
                table: "user_sessions");

            migrationBuilder.DropIndex(
                name: "IX_authorization_grants_PrincipalType_PrincipalId_Organization~",
                schema: "iam",
                table: "authorization_grants");

            migrationBuilder.DropColumn(
                name: "AuthenticationMethod",
                schema: "iam",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "ExternalProvider",
                schema: "iam",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "ExternalSubject",
                schema: "iam",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "MfaVerifiedAtUtc",
                schema: "iam",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                schema: "iam",
                table: "authorization_grants");

            migrationBuilder.DropColumn(
                name: "ResourceType",
                schema: "iam",
                table: "authorization_grants");

            migrationBuilder.CreateIndex(
                name: "IX_authorization_grants_PrincipalType_PrincipalId_Organization~",
                schema: "iam",
                table: "authorization_grants",
                columns: new[] { "PrincipalType", "PrincipalId", "OrganizationId", "EnvironmentId", "PermissionCode" },
                unique: true);
        }
    }
}
