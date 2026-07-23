using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderUrgencyArchiveMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_urgency_archive_batch_snapshots",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Archive batch membership row id."),
                    archive_batch_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning durable archive batch audit row id."),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Scheduling-owned source snapshot id reserved by this archive intent."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    sequence = table.Column<int>(type: "integer", nullable: false, comment: "Stable zero-based position in the archived payload.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_urgency_archive_batch_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_urgency_archive_batch_snapshots_order_urgency_archive~",
                        column: x => x.archive_batch_id,
                        principalSchema: "scheduling",
                        principalTable: "order_urgency_archive_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Indexed source snapshot membership for a durable urgency archive batch intent.");

            migrationBuilder.CreateIndex(
                name: "ix_urgency_archive_membership_batch_sequence",
                schema: "scheduling",
                table: "order_urgency_archive_batch_snapshots",
                columns: new[] { "archive_batch_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_urgency_archive_membership_scope_snapshot",
                schema: "scheduling",
                table: "order_urgency_archive_batch_snapshots",
                columns: new[] { "organization_id", "environment_id", "snapshot_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_urgency_archive_batch_snapshots",
                schema: "scheduling");
        }
    }
}
