using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <summary>
    /// GitHub #3319：把来源单据**行**维度加进检验记录，并让 <c>ux_inspection_records_source_attempt</c>
    /// 承载它——改前同一工单两道工序做同一 SKU 检验会撞同一个 attempt 1，第二条被跨行复用成同一条结论。
    ///
    /// <para><b>存量行的处置：不回填，一律留 NULL。</b>缺现场读数，本机 Aspire 卷只有可重造演示数据，
    /// 存量分布拿不到，所以策略必须与分布无关。留 NULL + <c>NULLS NOT DISTINCT</c> 的效果是：
    /// 存量行在新唯一键下的分组与改前**逐行等价**，因此重建唯一键不可能撞键，既有去重组也不会失去保护
    /// （PG 默认 NULL 互不相等，那才会让整组去重静默失效——新行永远挡不住）。</para>
    ///
    /// <para><b>周期检存量行保持旧形状。</b>改前周期检把复合来源行搬进了 <c>source_document_id</c>；
    /// 本次不改写它们，因为工单号并没有被编进那串复合行号，只能靠 join
    /// <c>periodic_inspection_runtime_contexts</c> 反查——那会让迁移依赖另一张表的存量形状，
    /// 且会改写已随集成事件发布出去的历史身份。存量周期检记录因此是不再被新代码产出的惰性形状。</para>
    ///
    /// <para><b>不可回滚点。</b><c>Down()</c> 能删列，但重建的旧唯一键更窄：改后靠来源行才区分开的记录
    /// （同工单不同工序、同收货单不同行）在旧键下是同一个键，重建时必然 23505。回滚前必须先人工决定
    /// 这些记录合并成哪一条，迁移本身不做这个决定。</para>
    /// </summary>
    public partial class AddInspectionRecordSourceDocumentLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_inspection_records_source_attempt",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.AddColumn<string>(
                name: "source_document_line_id",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true,
                comment: "Optional source document line, operation task id or stable periodic-operation window identity copied from the inspection task; null for directly recorded inspections without a source line.");

            migrationBuilder.CreateIndex(
                name: "ux_inspection_records_source_attempt",
                schema: "quality",
                table: "inspection_records",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_service", "source_document_id", "source_document_line_id", "sku_code", "attempt_number" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_inspection_records_source_attempt",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "source_document_line_id",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.CreateIndex(
                name: "ux_inspection_records_source_attempt",
                schema: "quality",
                table: "inspection_records",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_service", "source_document_id", "sku_code", "attempt_number" },
                unique: true);
        }
    }
}
