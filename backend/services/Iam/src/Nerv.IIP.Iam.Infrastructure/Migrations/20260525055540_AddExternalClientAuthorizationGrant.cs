using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalClientAuthorizationGrant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authorization_grants",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Authorization grant identifier."),
                    PrincipalType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Principal type, for example external-client."),
                    PrincipalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Principal identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Grant organization scope."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Grant environment scope."),
                    PermissionCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Granted permission code."),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Grant validity start time in UTC."),
                    ValidToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Grant validity end time in UTC."),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Grant revocation time in UTC.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authorization_grants", x => x.Id);
                },
                comment: "IAM authorization grants for non-user principals and scoped access.");

            migrationBuilder.CreateTable(
                name: "external_clients",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "External client record identifier."),
                    ClientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Public client identifier used for client_credentials."),
                    DisplayName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "External client display name."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "External client organization scope."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "External client environment scope."),
                    SecretHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "External client secret hash."),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the external client can authenticate."),
                    PermissionVersion = table.Column<int>(type: "integer", nullable: false, comment: "External client permission version for token invalidation."),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "External client credential validity start time in UTC."),
                    ValidToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "External client credential validity end time in UTC.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_clients", x => x.Id);
                },
                comment: "IAM external clients that can use client_credentials tokens.");

            migrationBuilder.CreateIndex(
                name: "IX_authorization_grants_PrincipalType_PrincipalId_Organization~",
                schema: "iam",
                table: "authorization_grants",
                columns: new[] { "PrincipalType", "PrincipalId", "OrganizationId", "EnvironmentId", "PermissionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_clients_ClientId",
                schema: "iam",
                table: "external_clients",
                column: "ClientId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authorization_grants",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "external_clients",
                schema: "iam");
        }
    }
}
