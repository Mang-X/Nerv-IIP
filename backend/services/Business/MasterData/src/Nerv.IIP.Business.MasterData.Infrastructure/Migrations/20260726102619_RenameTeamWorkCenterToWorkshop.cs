using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 班组由「绑工作中心」改为「绑车间」。这里刻意用 drop + add 而不是 RenameColumn：
    /// 旧列存的是工作中心码，与新列的车间码语义不同，原样搬过去会得到一列错数据。
    /// 前一版 <c>AddMasterDataWorkersAndTeamWorkCenter</c> 已进 main，故不改写它，改为本迁移向前修正。
    /// </summary>
    public partial class RenameTeamWorkCenterToWorkshop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "work_center_code",
                schema: "business_masterdata",
                table: "teams");

            migrationBuilder.AddColumn<string>(
                name: "workshop_code",
                schema: "business_masterdata",
                table: "teams",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional workshop code the team staffs; MES dispatch resolves candidates through work center -> workshop -> team.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "workshop_code",
                schema: "business_masterdata",
                table: "teams");

            migrationBuilder.AddColumn<string>(
                name: "work_center_code",
                schema: "business_masterdata",
                table: "teams",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional work center code the team staffs; drives MES dispatch candidate filtering.");
        }
    }
}
