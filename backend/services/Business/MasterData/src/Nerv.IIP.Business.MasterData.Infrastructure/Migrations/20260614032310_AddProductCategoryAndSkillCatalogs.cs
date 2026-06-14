using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCategoryAndSkillCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_categories",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Product category aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the product category."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the product category is valid."),
                    category_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique product category code."),
                    category_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Product category display name."),
                    parent_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional parent category code in the same organization and environment."),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Optional product category description."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the product category from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the product category was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the product category was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_categories", x => x.id);
                },
                comment: "Business master data product category hierarchy.");

            migrationBuilder.CreateTable(
                name: "skills",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Skill catalog aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the skill."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the skill is valid."),
                    skill_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business unique skill code."),
                    skill_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Skill display name."),
                    group_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Skill group name for catalog organization."),
                    requires_certification = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this skill requires certification evidence."),
                    validity_months = table.Column<int>(type: "integer", nullable: true, comment: "Optional certification validity period in months."),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Optional skill description."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Disabled flag that hides the skill from active use."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the skill was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the skill was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skills", x => x.id);
                },
                comment: "Business master data skill catalog definitions.");

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_disabled",
                schema: "business_masterdata",
                table: "product_categories",
                column: "disabled");

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_organization_id_environment_id_category_~",
                schema: "business_masterdata",
                table: "product_categories",
                columns: new[] { "organization_id", "environment_id", "category_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_categories_organization_id_environment_id_parent_co~",
                schema: "business_masterdata",
                table: "product_categories",
                columns: new[] { "organization_id", "environment_id", "parent_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_skills_disabled",
                schema: "business_masterdata",
                table: "skills",
                column: "disabled");

            migrationBuilder.CreateIndex(
                name: "IX_skills_organization_id_environment_id_group_name_disabled",
                schema: "business_masterdata",
                table: "skills",
                columns: new[] { "organization_id", "environment_id", "group_name", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_skills_organization_id_environment_id_skill_code",
                schema: "business_masterdata",
                table: "skills",
                columns: new[] { "organization_id", "environment_id", "skill_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_categories",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "skills",
                schema: "business_masterdata");
        }
    }
}
