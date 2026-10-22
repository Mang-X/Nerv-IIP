using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesShiftHandoverAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shift_handover_attachments",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Shift handover attachment id."),
                    file_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "FileStorage file id; the handle a download grant is issued against."),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, comment: "File name captured at handover time."),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Content type captured at handover time."),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false, comment: "File size in bytes captured at handover time."),
                    shift_handover_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning shift handover aggregate id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_handover_attachments", x => x.id);
                    table.CheckConstraint("ck_shift_handover_attachments_size_bytes", "size_bytes >= 0");
                    table.ForeignKey(
                        name: "fk_shift_handover_attachments_handovers",
                        column: x => x.shift_handover_id,
                        principalSchema: "mes",
                        principalTable: "shift_handovers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "MES FileStorage attachments handed over with a shift; file name, content type and size are handover-time snapshots.");

            migrationBuilder.CreateIndex(
                name: "ix_shift_handover_attachments_handover",
                schema: "mes",
                table: "shift_handover_attachments",
                column: "shift_handover_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_handover_attachments",
                schema: "mes");
        }
    }
}
