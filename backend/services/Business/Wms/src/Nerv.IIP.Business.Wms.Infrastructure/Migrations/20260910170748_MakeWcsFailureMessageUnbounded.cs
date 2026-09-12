using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <summary>
    /// GitHub #3305：把 <c>wcs_tasks.failure_message</c> 从 <c>varchar(1000)</c> 改为无界 <c>text</c>。
    ///
    /// <para>它是外部 WCS 回传的**原始诊断报文**，长度不受本仓控制（厂商原始报文、多段拼接、异常堆栈）。
    /// 改前超长会在 <c>SaveChangesAsync</c> 抛 22001，而 <c>WcsTask.Fail</c> 那一行不在任何 <c>catch</c> 里，
    /// 外部 WCS 收到 500 后会照重试语义无限重投——用一个没有业务依据的人为上界，换掉一次自动闭环。
    /// 同表 <c>completion_payload_json</c> 本就无界，先例就在旁边。</para>
    ///
    /// <para><b>下游不因此溢出。</b>该值经 <c>wms.WcsTaskRetryExhausted</c> 以 <c>DiagnosticMessage</c>
    /// 流到 Notification，落进有界的 <c>summary</c> 列。截断责任落在 Notification 侧
    /// （<c>NotificationSummaryText</c> 从它自己的 EF 模型取承载列宽最小值后收口），
    /// 原文一字不少留在本列，operator 可下钻。</para>
    ///
    /// <para><b>不可回滚点。</b><c>Down()</c> 把列收窄回 <c>varchar(1000)</c>：
    /// 只要库里已经存在长度超过 1000 的诊断报文，<c>ALTER COLUMN</c> 必然 22001，回滚就地失败。
    /// 回滚前必须先人工决定这些报文怎么处置（截断留档、搬去别处、还是直接丢弃），
    /// <b>迁移本身不做这个决定，也刻意不替它截断</b>——静默改写外部系统的原始报文正是本票否掉的做法。</para>
    /// </summary>
    public partial class MakeWcsFailureMessageUnbounded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "failure_message",
                schema: "wms",
                table: "wcs_tasks",
                type: "text",
                nullable: true,
                comment: "WCS failure diagnostic message; unbounded raw text from the external WCS.",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComment: "WCS failure diagnostic message.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "failure_message",
                schema: "wms",
                table: "wcs_tasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "WCS failure diagnostic message.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "WCS failure diagnostic message; unbounded raw text from the external WCS.");
        }
    }
}
