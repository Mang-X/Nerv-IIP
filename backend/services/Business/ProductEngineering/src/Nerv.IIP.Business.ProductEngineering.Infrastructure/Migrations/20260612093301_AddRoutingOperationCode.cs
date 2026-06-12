using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutingOperationCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "operation_name",
                schema: "product_engineering",
                table: "routing_operations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "Operation display name snapshot from the operation CodeSet.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "Operation display name.");

            migrationBuilder.AddColumn<string>(
                name: "operation_code",
                schema: "product_engineering",
                table: "routing_operations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MasterData reference-data operation code.");

            migrationBuilder.Sql(
                """
                UPDATE product_engineering.routing_operations
                SET operation_code = operation_name
                WHERE operation_code IS NULL OR operation_code = ''
                """);

            migrationBuilder.AlterColumn<string>(
                name: "operation_code",
                schema: "product_engineering",
                table: "routing_operations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "MasterData reference-data operation code.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "MasterData reference-data operation code.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "operation_code",
                schema: "product_engineering",
                table: "routing_operations");

            migrationBuilder.AlterColumn<string>(
                name: "operation_name",
                schema: "product_engineering",
                table: "routing_operations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "Operation display name.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "Operation display name snapshot from the operation CodeSet.");
        }
    }
}
