using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIamPersistentAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "iam");

            migrationBuilder.CreateTable(
                name: "connector_host_credentials",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Connector Host credential identifier."),
                    ConnectorHostId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Unique Connector Host identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Credential organization identifier."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Credential environment identifier."),
                    SecretHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Connector Host secret hash."),
                    ValidFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Credential validity start time in UTC."),
                    ValidToUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Credential validity end time in UTC.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_host_credentials", x => x.Id);
                },
                comment: "IAM credentials that authenticate Connector Hosts.");

            migrationBuilder.CreateTable(
                name: "environments",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Environment identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Owning organization identifier."),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Environment display name."),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Environment lifecycle status."),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag."),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_environments", x => x.Id);
                },
                comment: "IAM environments owned by organizations for access scoping.");

            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Membership identifier."),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Member user identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Membership organization identifier."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Membership environment identifier.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberships", x => x.Id);
                },
                comment: "IAM user memberships scoped to organization and environment.");

            migrationBuilder.CreateTable(
                name: "organizations",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Organization identifier."),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Organization display name."),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Organization lifecycle status."),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag."),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.Id);
                },
                comment: "IAM organizations that scope tenants and their environments.");

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Role identifier."),
                    RoleName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Unique role name."),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag."),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                },
                comment: "IAM roles that group permission codes for assignment.");

            migrationBuilder.CreateTable(
                name: "seed_manifests",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Seed manifest identifier."),
                    SeedName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Seed data name."),
                    SeedVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Seed data version."),
                    OwnerService = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Service that owns the seed data."),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Seed application time in UTC.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seed_manifests", x => x.Id);
                },
                comment: "IAM seed manifests recording applied seed data versions.");

            migrationBuilder.CreateTable(
                name: "user_sessions",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "User session identifier."),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Authenticated user identifier."),
                    RefreshTokenHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Refresh token hash used to rotate sessions."),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Session issue time in UTC."),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Session expiration time in UTC."),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Session revocation time in UTC."),
                    RevokedReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, comment: "Reason the session was revoked."),
                    PermissionVersion = table.Column<int>(type: "integer", nullable: false, comment: "Permission version captured when the session was issued."),
                    ClientInfo = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, comment: "Client information supplied during session creation."),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Client IP address supplied during session creation.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.Id);
                },
                comment: "IAM refresh sessions issued to authenticated users.");

            migrationBuilder.CreateTable(
                name: "users",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "User identifier."),
                    LoginName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Unique login name used for authentication."),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Unique user email address."),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Password hash used for credential verification."),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the user can authenticate."),
                    SecurityStamp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Security stamp invalidating stale access tokens."),
                    PermissionVersion = table.Column<int>(type: "integer", nullable: false, comment: "Permission version embedded into issued credentials."),
                    LastLoginAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Last successful login time in UTC."),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false, comment: "Consecutive failed login count."),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag."),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                },
                comment: "IAM users that authenticate and receive scoped permissions.");

            migrationBuilder.CreateTable(
                name: "connector_host_credential_capabilities",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Connector Host credential capability identifier."),
                    ConnectorHostCredentialId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Owning Connector Host credential identifier."),
                    CapabilityCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Capability code granted to the Connector Host credential.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_host_credential_capabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_connector_host_credential_capabilities_connector_host_crede~",
                        column: x => x.ConnectorHostCredentialId,
                        principalSchema: "iam",
                        principalTable: "connector_host_credentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "IAM capability codes owned by Connector Host credentials.");

            migrationBuilder.CreateTable(
                name: "membership_roles",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Membership role assignment identifier."),
                    MembershipId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Owning membership identifier."),
                    RoleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Assigned role identifier.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_membership_roles_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalSchema: "iam",
                        principalTable: "memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "IAM role assignments owned by memberships.");

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Role permission identifier."),
                    RoleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Owning role identifier."),
                    PermissionCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Permission code granted by the role.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "iam",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "IAM permission codes owned by roles.");

            migrationBuilder.CreateIndex(
                name: "IX_connector_host_credential_capabilities_ConnectorHostCredent~",
                schema: "iam",
                table: "connector_host_credential_capabilities",
                columns: new[] { "ConnectorHostCredentialId", "CapabilityCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_connector_host_credentials_ConnectorHostId",
                schema: "iam",
                table: "connector_host_credentials",
                column: "ConnectorHostId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_environments_OrganizationId_Id",
                schema: "iam",
                table: "environments",
                columns: new[] { "OrganizationId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_membership_roles_MembershipId_RoleId",
                schema: "iam",
                table: "membership_roles",
                columns: new[] { "MembershipId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_memberships_UserId_OrganizationId_EnvironmentId",
                schema: "iam",
                table: "memberships",
                columns: new[] { "UserId", "OrganizationId", "EnvironmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_RoleId_PermissionCode",
                schema: "iam",
                table: "role_permissions",
                columns: new[] { "RoleId", "PermissionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roles_RoleName",
                schema: "iam",
                table: "roles",
                column: "RoleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seed_manifests_SeedName_SeedVersion",
                schema: "iam",
                table: "seed_manifests",
                columns: new[] { "SeedName", "SeedVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_RefreshTokenHash",
                schema: "iam",
                table: "user_sessions",
                column: "RefreshTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_UserId_RevokedAtUtc",
                schema: "iam",
                table: "user_sessions",
                columns: new[] { "UserId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                schema: "iam",
                table: "users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_LoginName",
                schema: "iam",
                table: "users",
                column: "LoginName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connector_host_credential_capabilities",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "environments",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "membership_roles",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "organizations",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "seed_manifests",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "user_sessions",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "users",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "connector_host_credentials",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "memberships",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "iam");
        }
    }
}
