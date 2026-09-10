using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <summary>
    /// GitHub #3319：把来源单据**行**维度加进检验记录，并让 <c>ux_inspection_records_source_attempt</c>
    /// 承载它——改前同一工单两道工序做同一 SKU 检验会撞同一个 attempt 1，第二条被跨行复用成同一条结论。
    ///
    /// <para><b>存量行的处置：不回填，一律留 NULL。</b>存量行在新唯一键下的分组与改前**逐行等价**
    /// （迁移前该列不存在 ⇒ 所有存量行新列同为 NULL ⇒ 任何两条存量行都不可能靠新列区分；
    /// <c>NULLS NOT DISTINCT</c> 下 NULL = NULL），因此重建唯一键不可能撞键。</para>
    ///
    /// <para><b>这条保护的射程要说准。</b>它只保住「存量行之间」以及「存量行与后续**无来源行**写入之间」
    /// 的去重——改后只有直录录入命令（<c>CreateInspectionRecordCommand</c>）还写 NULL；
    /// 任务驱动的 7 个写入位点一律带来源行。所以**跨迁移的同一条真实检验链会断开**：
    /// 同一收货行重投、同一工序重开任务写出的新记录带来源行，与那条 NULL 存量行不同键，
    /// 会新开一条 attempt 1，出现两条并存的初检结论。这是本票行为变化的一部分，不是被保护住的情况。</para>
    ///
    /// <para><b>为什么不回填（真实理由）。</b>不是「拿不到存量分布」，也不是「没有回填路径」——
    /// <c>inspection_tasks.inspection_record_id</c> 存在，顺它 join 就能把任务侧两段身份搬到记录上，
    /// 这条路径与存量分布无关。真正的阻碍有三条，按决定性排序：
    /// <list type="number">
    /// <item><b>改写会把跨服务身份悬空。</b>周期检存量记录的来源单据身份（复合窗口身份）**已经出界**：
    /// MES 的 <c>quality_hold_contexts</c> 按 <c>ux_quality_hold_contexts_scope_source</c>
    /// （含 <c>source_document_id</c>）建了行。把记录侧改写成工单号之后，同一条链的后续事件会开出
    /// **第二行**而不是更新既有行，旧行的 hold 永远得不到释放。那是一次跨服务数据迁移。</item>
    /// <item><b>回填目标集合有歧义。</b>join 只对「有任务指回来」的记录成立；复检记录（attempt ≥ 2）
    /// 没有任务指向它，要沿 <c>reinspection_of_inspection_record_id</c> 递归回溯；而本缺陷本身就会造成
    /// 「多张任务复用同一条记录」的坏数据，那些行该按哪张任务回填没有唯一答案。</item>
    /// <item><b>NCR 复制了旧身份。</b><c>nonconformance_reports.source_document_id</c> 由
    /// <c>OpenFromInspection</c> 原样拷贝，只改记录不改 NCR 会让两者发散。</item>
    /// </list></para>
    ///
    /// <para><b>存量周期检行因此保持旧形状 <c>(复合窗口身份, NULL)</c>，而且它不是惰性数据：</b>
    /// 复检会把这个形状原样拷到新记录上并重新发布集成事件。跨服务身份还原因此必须继续读得懂它，
    /// 那条识别分支与它的**可执行退役条件**住在 <c>InspectionResultMesScope</c> 的类注释里。</para>
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
