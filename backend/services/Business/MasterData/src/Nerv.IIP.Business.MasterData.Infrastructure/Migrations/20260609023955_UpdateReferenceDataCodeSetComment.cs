using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateReferenceDataCodeSetComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "code_set",
                schema: "business_masterdata",
                table: "reference_data_codes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Reserved reference code set name such as material-type, storage-condition, quality-reason or compliance-tag.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "Reference code set name such as material-form, asset-class or storage-condition.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "code_set",
                schema: "business_masterdata",
                table: "reference_data_codes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Reference code set name such as material-form, asset-class or storage-condition.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "Reserved reference code set name such as material-type, storage-condition, quality-reason or compliance-tag.");
        }
    }
}
