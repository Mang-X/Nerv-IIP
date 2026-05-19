using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCaseInsensitiveUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_users_LoginName_Lower\" ON iam.users (lower(\"LoginName\"));");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_users_Email_Lower\" ON iam.users (lower(\"Email\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS iam.\"IX_users_Email_Lower\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS iam.\"IX_users_LoginName_Lower\";");
        }
    }
}
