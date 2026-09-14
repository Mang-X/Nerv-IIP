using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesProductionReportIntentFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "report_intent_fingerprint",
                schema: "mes",
                table: "production_reports",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "Optional opaque caller intent fingerprint used to recover the exact production report receipt.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "report_intent_fingerprint",
                schema: "mes",
                table: "production_reports");
        }
    }
}
