using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessPartnerAvailabilityProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "business_partner_availabilities",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "ERP business-partner availability projection identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Owning organization identifier."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Owning environment identifier."),
                    PartnerCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData business-partner code used by ERP orders."),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Latest partner status: active or disabled."),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time of the latest applied MasterData partner change."),
                    SourceEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Latest applied MasterData integration event identifier.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_partner_availabilities", x => x.Id);
                },
                comment: "Latest MasterData business-partner availability projected for ERP order gates.");

            migrationBuilder.CreateIndex(
                name: "ux_business_partner_availabilities_scope_code",
                schema: "erp",
                table: "business_partner_availabilities",
                columns: new[] { "OrganizationId", "EnvironmentId", "PartnerCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_partner_availabilities",
                schema: "erp");
        }
    }
}
