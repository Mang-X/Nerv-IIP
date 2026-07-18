using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesSkuAvailabilityProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mes_sku_availabilities",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "MES SKU availability projection identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Owning organization identifier."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Owning environment identifier."),
                    SkuCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code used by MES work orders."),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Latest consumed SKU status; this slice records disabled facts."),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time of the latest applied MasterData SKU availability change."),
                    DisabledReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "MasterData reason for disabling the SKU."),
                    SourceEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Latest applied MasterData integration event identifier.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mes_sku_availabilities", x => x.Id);
                },
                comment: "Latest MasterData SKU availability consumed by BusinessMES new-work-order gates.");

            migrationBuilder.CreateIndex(
                name: "ux_mes_sku_availabilities_scope_code",
                schema: "mes",
                table: "mes_sku_availabilities",
                columns: new[] { "OrganizationId", "EnvironmentId", "SkuCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mes_sku_availabilities",
                schema: "mes");
        }
    }
}
