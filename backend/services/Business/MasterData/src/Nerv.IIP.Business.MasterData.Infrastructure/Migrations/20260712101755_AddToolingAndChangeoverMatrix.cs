using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddToolingAndChangeoverMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "changeover_matrix_entries",
                schema: "business_masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Changeover matrix entry identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Organization scope."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Environment scope."),
                    WorkCenterCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Work-center code."),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Source dimension: SKU or ProductCategory."),
                    SourceCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Source SKU or product-category code."),
                    ToSkuCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Target SKU code."),
                    SetupMinutes = table.Column<int>(type: "integer", nullable: false, comment: "Setup duration in minutes."),
                    Active = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this matrix entry is active."),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "Creation timestamp in UTC."),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "Last update timestamp in UTC.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_changeover_matrix_entries", x => x.Id);
                },
                comment: "Authoritative setup duration and tooling requirements by work center and SKU transition.");

            migrationBuilder.CreateTable(
                name: "tooling_assets",
                schema: "business_masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Tooling asset identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Organization scope."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Environment scope."),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Coding-engine allocated tooling code."),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Tooling display name."),
                    ToolingType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tooling type code."),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Lifecycle status: Available, Maintenance, or Retired."),
                    MaintenanceLifeCount = table.Column<long>(type: "bigint", nullable: true, comment: "Usage count at which maintenance becomes due."),
                    UsageCount = table.Column<long>(type: "bigint", nullable: false, comment: "Accumulated governed usage count."),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "Creation timestamp in UTC."),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "Last update timestamp in UTC.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tooling_assets", x => x.Id);
                },
                comment: "Tooling and mould master assets governed by MasterData.");

            migrationBuilder.CreateTable(
                name: "changeover_required_tooling",
                schema: "business_masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Required-tooling row identifier."),
                    ToolingCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Required tooling code."),
                    ChangeoverMatrixEntryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_changeover_required_tooling", x => x.Id);
                    table.ForeignKey(
                        name: "FK_changeover_required_tooling_changeover_matrix_entries_Chang~",
                        column: x => x.ChangeoverMatrixEntryId,
                        principalSchema: "business_masterdata",
                        principalTable: "changeover_matrix_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Tooling assets required by a changeover matrix entry.");

            migrationBuilder.CreateTable(
                name: "tooling_applicability",
                schema: "business_masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Applicability row identifier."),
                    WorkCenterCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Applicable work-center code."),
                    SkuCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Applicable SKU code."),
                    ToolingAssetId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tooling_applicability", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tooling_applicability_tooling_assets_ToolingAssetId",
                        column: x => x.ToolingAssetId,
                        principalSchema: "business_masterdata",
                        principalTable: "tooling_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Work-center and SKU applicability of a tooling asset.");

            migrationBuilder.CreateIndex(
                name: "IX_changeover_matrix_entries_OrganizationId_EnvironmentId_Work~",
                schema: "business_masterdata",
                table: "changeover_matrix_entries",
                columns: new[] { "OrganizationId", "EnvironmentId", "WorkCenterCode", "SourceType", "SourceCode", "ToSkuCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_changeover_required_tooling_ChangeoverMatrixEntryId_Tooling~",
                schema: "business_masterdata",
                table: "changeover_required_tooling",
                columns: new[] { "ChangeoverMatrixEntryId", "ToolingCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tooling_applicability_ToolingAssetId_WorkCenterCode_SkuCode",
                schema: "business_masterdata",
                table: "tooling_applicability",
                columns: new[] { "ToolingAssetId", "WorkCenterCode", "SkuCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tooling_assets_OrganizationId_EnvironmentId_Code",
                schema: "business_masterdata",
                table: "tooling_assets",
                columns: new[] { "OrganizationId", "EnvironmentId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "changeover_required_tooling",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "tooling_applicability",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "changeover_matrix_entries",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "tooling_assets",
                schema: "business_masterdata");
        }
    }
}
