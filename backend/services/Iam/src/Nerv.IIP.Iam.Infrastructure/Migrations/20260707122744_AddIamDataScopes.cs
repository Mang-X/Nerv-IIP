using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIamDataScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "membership_data_scopes",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Membership data scope identifier."),
                    MembershipId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Owning membership identifier."),
                    ScopeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Data scope type: site, workshop or production-line."),
                    ScopeCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "MasterData code for the data scope.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_data_scopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_membership_data_scopes_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalSchema: "iam",
                        principalTable: "memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "IAM data scope bindings owned by user memberships.");

            migrationBuilder.CreateTable(
                name: "role_data_scopes",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Role data scope identifier."),
                    RoleId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Owning role identifier."),
                    ScopeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Data scope type: site, workshop or production-line."),
                    ScopeCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "MasterData code for the data scope.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_data_scopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_data_scopes_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "iam",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "IAM data scope bindings owned by roles.");

            migrationBuilder.CreateIndex(
                name: "IX_membership_data_scopes_MembershipId_ScopeType_ScopeCode",
                schema: "iam",
                table: "membership_data_scopes",
                columns: new[] { "MembershipId", "ScopeType", "ScopeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_data_scopes_RoleId_ScopeType_ScopeCode",
                schema: "iam",
                table: "role_data_scopes",
                columns: new[] { "RoleId", "ScopeType", "ScopeCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "membership_data_scopes",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "role_data_scopes",
                schema: "iam");
        }
    }
}
