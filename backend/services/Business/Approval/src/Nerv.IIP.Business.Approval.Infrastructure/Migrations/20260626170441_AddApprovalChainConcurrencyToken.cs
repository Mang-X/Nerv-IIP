using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Approval.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalChainConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "row_version",
                schema: "business_approval",
                table: "approval_chains",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Optimistic concurrency token for approval chain decisions and runtime step changes.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "business_approval",
                table: "approval_chains");
        }
    }
}
