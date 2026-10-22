using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalVoucherSourceDocumentColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_no",
                schema: "erp",
                table: "journal_vouchers",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Source document number whose meaning is decided by source_type; NULL only on rows written before the source columns existed.");

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                schema: "erp",
                table: "journal_vouchers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "Source document type code from JournalVoucherSourceType; NULL only on rows written before the source columns existed.");

            migrationBuilder.CreateIndex(
                name: "IX_journal_vouchers_organization_id_environment_id_source_type~",
                schema: "erp",
                table: "journal_vouchers",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_no" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_journal_vouchers_organization_id_environment_id_source_type~",
                schema: "erp",
                table: "journal_vouchers");

            migrationBuilder.DropColumn(
                name: "source_no",
                schema: "erp",
                table: "journal_vouchers");

            migrationBuilder.DropColumn(
                name: "source_type",
                schema: "erp",
                table: "journal_vouchers");
        }
    }
}
