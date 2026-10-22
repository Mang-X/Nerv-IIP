using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Erp.Infrastructure.Migrations
{
    /// <summary>
    /// #3278 / S5：把 S2 建的来源两列检索索引升级为**唯一**索引，由它承接原先由
    /// <c>voucher_no</c> 唯一索引承担的幂等语义。<c>voucher_no</c> 那条唯一索引本次**不动**
    /// （凭证号改短号是 S6/S7）。
    ///
    /// <c>filter</c> 的**条件**只提来源两列非空（owner 2026-09-14 A1 裁定：存量不回填、可重造）。
    /// ⚠️ 别把它读成「存量 NULL 行靠这条 filter 才落得进去」——放行它们的是 PostgreSQL 默认的
    /// <c>NULLS DISTINCT</c>，去掉 filter 那些行照样插得进去（已实测）。filter 的作用是把
    /// 「存量 NULL 行不参与唯一性」写成索引定义里读得出来的事实，而不是依赖 provider 默认值。
    /// 谓词**全形**由 <c>ErpCostAccountingPostgresAcceptanceTests.ExpectedSourceIndexPredicate</c> 钉住：
    /// 在它后面追加任何一条豁免 conjunct 都会转红。
    /// <c>Down()</c> 把索引还原成非唯一且无过滤，与 S2 落地后的形状一致——本仓判例是
    /// <c>Down()</c> 写错不会有任何红，所以它由
    /// <c>ErpCostAccountingPostgresAcceptanceTests</c> 里的 Up → Down → 回读那一格真跑一次。
    /// </summary>
    public partial class AddJournalVoucherSourceDocumentUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_journal_vouchers_organization_id_environment_id_source_type~",
                schema: "erp",
                table: "journal_vouchers");

            migrationBuilder.CreateIndex(
                name: "IX_journal_vouchers_organization_id_environment_id_source_type~",
                schema: "erp",
                table: "journal_vouchers",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_no" },
                unique: true,
                filter: "source_type IS NOT NULL AND source_no IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_journal_vouchers_organization_id_environment_id_source_type~",
                schema: "erp",
                table: "journal_vouchers");

            migrationBuilder.CreateIndex(
                name: "IX_journal_vouchers_organization_id_environment_id_source_type~",
                schema: "erp",
                table: "journal_vouchers",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_no" });
        }
    }
}
