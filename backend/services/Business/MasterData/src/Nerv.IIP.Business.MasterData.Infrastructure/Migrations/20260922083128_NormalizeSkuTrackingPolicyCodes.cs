using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <summary>
    /// 把存量 SKU 上由 <c>Sku.Create</c> 写入的码集外同义词归一到各自码集的“不管理”码。
    /// 结构未变，只有数据：这两列此前可以存下 batch-tracking-policy / serial-tracking-policy
    /// 字典里根本不存在的值，网关报工协调器按码集判定时会对这些 SKU 恒定拒绝。
    /// </summary>
    public partial class NormalizeSkuTrackingPolicyCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE business_masterdata.skus
                SET serial_tracking_policy = 'none'
                WHERE serial_tracking_policy = 'not-serialized';
                """);

            migrationBuilder.Sql(
                """
                UPDATE business_masterdata.skus
                SET batch_tracking_policy = 'none'
                WHERE batch_tracking_policy = 'not-tracked';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 只前滚：反向写回的是码集外的值，等于把缺陷装回库里。
        }
    }
}
