using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessPartnerRolesAndTaxId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_business_partners_organization_id_environment_id_partner_ty~",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.AlterColumn<string>(
                name: "partner_type",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "Primary business partner role kept for backward-compatible list filters.",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "Business partner type such as supplier, customer or carrier.");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Business unique partner code within the organization and environment.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "Business unique partner code within the partner type.");

            migrationBuilder.AddColumn<string[]>(
                name: "partner_roles",
                schema: "business_masterdata",
                table: "business_partners",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0],
                comment: "All roles held by the business partner, such as supplier, customer or carrier.");

            migrationBuilder.Sql("""
                UPDATE business_masterdata.business_partners
                SET partner_roles = ARRAY[partner_type]
                WHERE cardinality(partner_roles) = 0;
                """);

            migrationBuilder.AddColumn<string>(
                name: "tax_id",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional tax registration id unique within the organization and environment.");

            migrationBuilder.CreateIndex(
                name: "IX_business_partners_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "business_partners",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_partners_organization_id_environment_id_tax_id",
                schema: "business_masterdata",
                table: "business_partners",
                columns: new[] { "organization_id", "environment_id", "tax_id" },
                unique: true,
                filter: "tax_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_business_partners_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.DropIndex(
                name: "IX_business_partners_organization_id_environment_id_tax_id",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "partner_roles",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "tax_id",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.AlterColumn<string>(
                name: "partner_type",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "Business partner type such as supplier, customer or carrier.",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "Primary business partner role kept for backward-compatible list filters.");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Business unique partner code within the partner type.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "Business unique partner code within the organization and environment.");

            migrationBuilder.CreateIndex(
                name: "IX_business_partners_organization_id_environment_id_partner_ty~",
                schema: "business_masterdata",
                table: "business_partners",
                columns: new[] { "organization_id", "environment_id", "partner_type", "code" },
                unique: true);
        }
    }
}
