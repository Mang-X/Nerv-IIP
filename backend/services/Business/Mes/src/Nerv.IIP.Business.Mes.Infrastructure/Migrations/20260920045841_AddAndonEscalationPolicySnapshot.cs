using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAndonEscalationPolicySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "escalation_timeout_seconds",
                schema: "mes",
                table: "andon_calls",
                type: "double precision",
                nullable: true,
                comment: "Unclaimed timeout in seconds selected at escalation; null for un-escalated or pre-policy-history calls.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "escalation_timeout_seconds",
                schema: "mes",
                table: "andon_calls");
        }
    }
}
