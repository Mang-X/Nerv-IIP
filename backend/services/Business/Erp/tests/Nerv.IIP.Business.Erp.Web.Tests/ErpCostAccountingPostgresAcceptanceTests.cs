using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.MachineOverheadReconciliationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkCenterMachineOverheadRateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Seed;
using Nerv.IIP.Business.Erp.Web.Application.Validation;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection("ERP PostgreSQL acceptance")]
public sealed class ErpCostAccountingPostgresAcceptanceTests
{
    /// <summary>
    /// #3229：<c>voucher_no</c> 列宽 100，而改前所有派生凭证号都是「前缀 + 100 宽上游单号（+ 上游 id）」。
    /// 这条用例在**同一张真表**上先复现改前形状的 22001，再证明新构造入口的顶格产出真能落库。
    /// EF InMemory 看不见列宽也看不见唯一索引，所以这两个读数只有在真 Postgres 上才成立。
    ///
    /// ⚠️ <b>#3278 / S7 登记：本用例的一部分前提已被抽掉，但它仍在跑、仍是绿的</b>。
    /// 本用例喂的「顶格输入」是从**生产调用点**枚举出来的；
    /// S7 把消费侧 5 个建凭证位点改成分配器短号后，
    /// <c>WorkOrderCapitalization</c> / <c>WorkOrderCostAdjustment</c> / <c>GoodsReceiptIrAccrual</c> /
    /// <c>PurchaseReturn</c> / <c>CreditNote</c> 五个族已经**没有任何生产调用点**在走
    /// <c>ErpVoucherNoPolicy.Compose</c>，下面那些以它们为族的输入不再是生产输入。
    /// 仍有生产调用点的只剩 <c>AccountPayable</c> / <c>AccountReceivable</c> / <c>CostCandidate</c>（归 S6）。
    /// 本票**不删**这些断言（退役属 S8），只登记。同形登记见
    /// <c>ErpVoucherNoLengthContractTests</c> 的 <c>Saturated_production_inputs_stay_within_the_column_width</c>——
    /// 那一类是 S7 与 S6 的共用面，本票不碰。
    /// </summary>
    [ErpCostPostgresFact(Timeout = 60_000)]
    public async Task PostgreSQL_saturated_derived_voucher_numbers_persist_where_the_pre_change_shape_overflows()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var postingDate = new DateOnly(2026, 9, 9);
        var workOrderId = new string('W', 100);
        var movementId = Guid.NewGuid().ToString();
        var adjustmentSourceId = new string('S', 100);
        var payableNo = new string('P', 100);

        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setup.Database.MigrateAsync();
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            setup.GLAccounts.Add(GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1405-WIP", "Work in process", GLAccountType.Asset));
            setup.GLAccounts.Add(GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1406-FINISHED-GOODS", "Finished goods inventory", GLAccountType.Asset));
            await setup.SaveChangesAsync();
        }

        // ① 缺陷复现：改前的构造式在这张真表上就是 22001，不是推断。
        await using (var overflow = new ApplicationDbContext(options, new NoopMediator()))
        {
            var preChangeVoucherNo = $"JV-WOC-{workOrderId}-{movementId}";
            Assert.Equal(144, preChangeVoucherNo.Length);
            overflow.JournalVouchers.Add(BalancedVoucher(preChangeVoucherNo, postingDate));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => overflow.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, postgres.SqlState);
        }

        // ② 新构造入口的顶格产出必须真的落得进去。
        var saturated = new[]
        {
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, workOrderId, movementId),
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCostAdjustment, workOrderId, adjustmentSourceId),
            ErpVoucherNoPolicy.Compose(VoucherFamily.AccountPayable, payableNo),
        };
        await using (var write = new ApplicationDbContext(options, new NoopMediator()))
        {
            foreach (var voucherNo in saturated)
            {
                write.JournalVouchers.Add(BalancedVoucher(voucherNo, postingDate));
            }

            await write.SaveChangesAsync();
        }

        await using (var verify = new ApplicationDbContext(options, new NoopMediator()))
        {
            var persisted = await verify.JournalVouchers
                .Where(x => x.OrganizationId == VoucherOrganizationId && x.EnvironmentId == VoucherEnvironmentId)
                .Select(x => x.VoucherNo)
                .ToListAsync();
            Assert.Equal(saturated.Order(StringComparer.Ordinal), persisted.Order(StringComparer.Ordinal));
        }

        // ③ 同一来源仍然稳定地得到同一个凭证号，所以第二次落库撞的是那条唯一索引，而不是悄悄记出第二张凭证。
        await using (var replay = new ApplicationDbContext(options, new NoopMediator()))
        {
            replay.JournalVouchers.Add(BalancedVoucher(
                ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, workOrderId, movementId),
                postingDate));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => replay.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Contains("voucher_no", postgres.ConstraintName, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// #3229 的风险点：有界化不得把原本不同的凭证塌成同号。
    /// 这条用例把三对「改前会塌 / 现在必须分开」的来源一起插进带唯一索引的真表——塌了就是 23505。
    /// </summary>
    [ErpCostPostgresFact(Timeout = 60_000)]
    public async Task PostgreSQL_distinct_sources_never_collapse_onto_one_voucher_number()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var postingDate = new DateOnly(2026, 9, 9);
        var head = new string('X', 60);
        var tail = new string('Y', 60);
        var saturatedWorkOrderId = new string('W', 100);
        var saturatedSourceId = new string('S', 100);

        var distinctSources = new[]
        {
            // 摘要式之间：只有段划分不同。**只删长度前缀不会塌**（U+001F 分隔符单独已足够消歧，实测不红）；
            // 真正塌成同号需要摘要输入**退化成朴素连字符拼接**（分隔符换成 - 且去掉长度前缀）。
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, head + "-" + tail, "Z"),
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, head, tail + "-Z"),
            // 跨族：同样两段，族不同。
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, saturatedWorkOrderId, saturatedSourceId),
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCostAdjustment, saturatedWorkOrderId, saturatedSourceId),
            // 改前 JV-WOC- 是 JV-WOC-ADJ- 的前缀，这两行改前是同一个凭证号。
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, "ADJ-WO-0001", "RPT-0001"),
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCostAdjustment, "WO-0001", "RPT-0001"),
        };
        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setup.Database.MigrateAsync();
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            setup.GLAccounts.Add(GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1405-WIP", "Work in process", GLAccountType.Asset));
            setup.GLAccounts.Add(GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1406-FINISHED-GOODS", "Finished goods inventory", GLAccountType.Asset));
            await setup.SaveChangesAsync();
        }

        await using (var write = new ApplicationDbContext(options, new NoopMediator()))
        {
            foreach (var voucherNo in distinctSources)
            {
                write.JournalVouchers.Add(BalancedVoucher(voucherNo, postingDate));
            }

            await write.SaveChangesAsync();
        }

        await using (var verify = new ApplicationDbContext(options, new NoopMediator()))
        {
            var persisted = await verify.JournalVouchers
                .Where(x => x.OrganizationId == VoucherOrganizationId && x.EnvironmentId == VoucherEnvironmentId)
                .Select(x => x.VoucherNo)
                .ToListAsync();
            Assert.Equal(distinctSources.Length, persisted.Count);
            // 顺序刻意如此：**先落库**（塌号在这里就是 23505），再在读回结果上核对互异。
            // 这条内存断言若放在写库之前会短路，唯一索引那一层就永远不被检验。
            Assert.Equal(distinctSources.Length, persisted.Distinct(StringComparer.Ordinal).Count());
            Assert.All(persisted, voucherNo => Assert.True(
                voucherNo.Length <= ErpVoucherNoPolicy.ColumnMaxLength,
                $"凭证号 {voucherNo} 长度 {voucherNo.Length} 超出列宽。"));
        }
    }

    private const string VoucherOrganizationId = "org-voucher-no";

    private const string VoucherEnvironmentId = "env-voucher-no";

    private static JournalVoucher BalancedVoucher(string voucherNo, DateOnly postingDate)
        => JournalVoucher.Post(
            VoucherOrganizationId,
            VoucherEnvironmentId,
            voucherNo,
            postingDate,
            [
                new JournalVoucherLineDraft("1406-FINISHED-GOODS", 10m, 0m, "debit leg"),
                new JournalVoucherLineDraft("1405-WIP", 0m, 10m, "credit leg"),
            ],
            JournalVoucherSourceType.Manual,
            voucherNo);

    /// <summary>
    /// #3278 / S2：来源两列在**真表**上的形状与往返。
    ///
    /// 这一格证明三件 EF InMemory 证不了的事：
    /// ① 迁移 <c>Up()</c> 真的把两列加成**可空**——用一条 NULL 存量行直接插进去验，
    ///    而不是读 <c>information_schema</c> 的 <c>is_nullable</c> 自证；
    /// ② 写进去的值原样读得回来（往返，不是只看 SQL 没报错）；
    /// ③ 迁移 <c>Down()</c> 真的跑得动并真的把两列去掉——本仓判例：
    ///    <c>Down()</c> 写错时**不会有任何红**，除非真跑一次。
    /// </summary>
    [ErpCostPostgresFact(Timeout = 120_000)]
    public async Task PostgreSQL_source_document_columns_round_trip_and_survive_up_down_up()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        const string sourceColumnMigration = "20260914134346_AddJournalVoucherSourceDocumentColumns";
        const string previousMigration = "20260909031821_FreezePurchaseReceiptUnitPrice";
        var postingDate = new DateOnly(2026, 9, 14);

        await using var db = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        await db.Database.OpenConnectionAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(ErpFacts.Schema);

        db.GLAccounts.AddRange(
            GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1405-WIP", "Work in process", GLAccountType.Asset),
            GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1406-FINISHED-GOODS", "Finished goods inventory", GLAccountType.Asset));
        db.JournalVouchers.Add(JournalVoucher.Post(
            VoucherOrganizationId,
            VoucherEnvironmentId,
            "JV-SRC-0001",
            postingDate,
            [
                new JournalVoucherLineDraft("1406-FINISHED-GOODS", 10m, 0m, "debit leg"),
                new JournalVoucherLineDraft("1405-WIP", 0m, 10m, "credit leg"),
            ],
            JournalVoucherSourceType.PaymentExecution,
            "APPAY-0001"));
        await db.SaveChangesAsync();

        // ① 往返：写进去的两列原样读得回来。
        db.ChangeTracker.Clear();
        var persisted = await db.JournalVouchers.SingleAsync(x => x.VoucherNo == "JV-SRC-0001");
        Assert.Equal("APPAY", persisted.SourceType);
        Assert.Equal("APPAY-0001", persisted.SourceNo);

        // ② 可空：一条来源列为 NULL 的存量形态行必须插得进去。列若被建成 NOT NULL，这里就是 23502。
        await using (var legacy = new NpgsqlCommand($"""
            INSERT INTO {quotedSchema}.journal_vouchers
                (id, organization_id, environment_id, voucher_no, posting_date, posted_at_utc, source_type, source_no)
            VALUES
                (@id, @org, @env, 'JV-LEGACY-0001', @postingDate, @postedAt, NULL, NULL)
            """, (NpgsqlConnection)db.Database.GetDbConnection()))
        {
            legacy.Parameters.AddWithValue("id", Guid.CreateVersion7());
            legacy.Parameters.AddWithValue("org", VoucherOrganizationId);
            legacy.Parameters.AddWithValue("env", VoucherEnvironmentId);
            legacy.Parameters.AddWithValue("postingDate", postingDate);
            legacy.Parameters.AddWithValue("postedAt", DateTime.UtcNow);
            await legacy.ExecuteNonQueryAsync();
        }

        db.ChangeTracker.Clear();
        var legacyRow = await db.JournalVouchers.SingleAsync(x => x.VoucherNo == "JV-LEGACY-0001");
        Assert.Null(legacyRow.SourceType);
        Assert.Null(legacyRow.SourceNo);

        // ③ Down() 真跑：回落到上一版后两列必须消失，再 Up() 必须回来。
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previousMigration);
        Assert.Equal([], await JournalVoucherSourceColumnsAsync(db, quotedSchema));

        await migrator.MigrateAsync(sourceColumnMigration);
        Assert.Equal(["source_no", "source_type"], await JournalVoucherSourceColumnsAsync(db, quotedSchema));
    }

    /// <summary>
    /// #3278 / S2：17 个建凭证位点里有 2 处在 <c>WorldHistorySeedService</c>。
    /// 这一格在真库上跑一次 seed，然后断言 <c>journal_vouchers</c> **整表**没有来源列空值——
    /// 漏填那 2 处中的任意一处，这里的空值计数就不为 0。
    ///
    /// <b>值域边界</b>：本格覆盖的是 seed 的 2 处，不是全部 17 处。
    /// 「17 处一个不漏」由 <c>JournalVoucher.Post</c> 的不可省略参数在**编译期**保证
    /// （见 <c>JournalVoucherSourceContractTests</c>），不由本格保证。
    /// </summary>
    [ErpCostPostgresFact(Timeout = 180_000)]
    public async Task PostgreSQL_world_history_seed_leaves_no_voucher_without_a_source_document()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();

        await new WorldHistorySeedService(db).SeedAsync("org-001", "env-dev", new DateOnly(2026, 7, 26), 0.02d);

        await db.Database.OpenConnectionAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(ErpFacts.Schema);
        await using var counts = new NpgsqlCommand($"""
            SELECT count(*) AS total,
                   count(*) FILTER (WHERE source_type IS NULL OR source_no IS NULL) AS missing,
                   count(DISTINCT source_type) AS distinct_types
            FROM {quotedSchema}.journal_vouchers
            """, (NpgsqlConnection)db.Database.GetDbConnection());
        await using var reader = await counts.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var total = reader.GetInt64(0);
        var missing = reader.GetInt64(1);
        var distinctTypes = reader.GetInt64(2);

        Assert.True(total > 0, "seed 没有写出任何凭证，这一格就没有量到东西。");
        Assert.Equal(0L, missing);
        // 收入凭证与收款凭证必须落成两个不同的来源类型；两处都填成同一个占位串时这里就是 1。
        Assert.Equal(2L, distinctTypes);
    }

    private static async Task<IReadOnlyList<string>> JournalVoucherSourceColumnsAsync(ApplicationDbContext db, string quotedSchema)
    {
        await using var command = new NpgsqlCommand("""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = 'journal_vouchers' AND column_name IN ('source_type', 'source_no')
            ORDER BY column_name
            """, (NpgsqlConnection)db.Database.GetDbConnection());
        command.Parameters.AddWithValue("schema", quotedSchema.Trim('"'));
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    /// <summary>
    /// #3278 / S5 ①：来源两列上的**唯一索引**本身。
    ///
    /// EF InMemory 既看不见唯一索引也看不见 partial filter，所以这一格的四个读数只有在真 PostgreSQL 上成立。
    /// 每一步都**先落库再断言**：把互异性写成内存断言会短路，索引那一层就永远不被检验。
    ///
    /// <b>为什么第二行刻意换一个凭证号</b>：<c>voucher_no</c> 那条唯一索引本票**不动**，
    /// 两条索引同时被违反时读不出是哪条在挡。换号后只剩来源索引能红，23505 的 <c>ConstraintName</c>
    /// 才是**这条**索引在承重的证据，而不是旧索引顺带兜住的。
    ///
    /// <b>值域边界</b>：本格只证索引的判别力，不证生产位点用的是哪个键——那由
    /// <see cref="PostgreSQL_dedup_sites_key_on_the_source_document_even_when_the_voucher_number_differs"/> 承担。
    /// </summary>
    [ErpCostPostgresFact(Timeout = 120_000)]
    public async Task PostgreSQL_source_document_unique_index_blocks_a_second_row_for_the_same_source()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var postingDate = new DateOnly(2026, 9, 15);

        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setup.Database.MigrateAsync();
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            setup.GLAccounts.Add(GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1405-WIP", "Work in process", GLAccountType.Asset));
            setup.GLAccounts.Add(GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1406-FINISHED-GOODS", "Finished goods inventory", GLAccountType.Asset));
            await setup.SaveChangesAsync();
        }

        // ① 第一张：来源 (GRIR, RCV-S5-001)。
        await using (var first = new ApplicationDbContext(options, new NoopMediator()))
        {
            first.JournalVouchers.Add(SourceKeyedVoucher("JV-S5-A", JournalVoucherSourceType.GoodsReceiptIrAccrual, "RCV-S5-001", postingDate));
            await first.SaveChangesAsync();
        }

        // ② 同一来源 + **不同凭证号** ⇒ 只可能撞来源索引。改前（按 voucher_no 定幂等）这一行是落得进去的，
        //    也就是「对同一张收货单再记一张凭证」。
        await using (var duplicateSource = new ApplicationDbContext(options, new NoopMediator()))
        {
            duplicateSource.JournalVouchers.Add(SourceKeyedVoucher("JV-S5-B", JournalVoucherSourceType.GoodsReceiptIrAccrual, "RCV-S5-001", postingDate));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicateSource.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Contains("source_type", postgres.ConstraintName, StringComparison.Ordinal);
            Assert.DoesNotContain("voucher_no", postgres.ConstraintName, StringComparison.Ordinal);
        }

        // ③ 两条互异方向各一行：换类型、换单号都必须放行。少了这两行，「唯一索引」与「整表只许一行」读数相同。
        await using (var distinct = new ApplicationDbContext(options, new NoopMediator()))
        {
            distinct.JournalVouchers.Add(SourceKeyedVoucher("JV-S5-C", JournalVoucherSourceType.PurchaseReturn, "RCV-S5-001", postingDate));
            distinct.JournalVouchers.Add(SourceKeyedVoucher("JV-S5-D", JournalVoucherSourceType.GoodsReceiptIrAccrual, "RCV-S5-002", postingDate));
            await distinct.SaveChangesAsync();
        }

        // ④ 存量形态：来源两列为 NULL 的行**可以有多条**。partial filter 若写错（或列被改成 NOT NULL），
        //    第二条 NULL 行会 23505 / 23502。owner A1 裁定不回填存量，所以这条放行是硬要求。
        await using (var legacy = new ApplicationDbContext(options, new NoopMediator()))
        {
            await legacy.Database.OpenConnectionAsync();
            var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(ErpFacts.Schema);
            foreach (var voucherNo in new[] { "JV-S5-LEGACY-1", "JV-S5-LEGACY-2" })
            {
                await using var insert = new NpgsqlCommand($"""
                    INSERT INTO {quotedSchema}.journal_vouchers
                        (id, organization_id, environment_id, voucher_no, posting_date, posted_at_utc, source_type, source_no)
                    VALUES
                        (@id, @org, @env, @voucherNo, @postingDate, @postedAt, NULL, NULL)
                    """, (NpgsqlConnection)legacy.Database.GetDbConnection());
                insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
                insert.Parameters.AddWithValue("org", VoucherOrganizationId);
                insert.Parameters.AddWithValue("env", VoucherEnvironmentId);
                insert.Parameters.AddWithValue("voucherNo", voucherNo);
                insert.Parameters.AddWithValue("postingDate", postingDate);
                insert.Parameters.AddWithValue("postedAt", DateTime.UtcNow);
                await insert.ExecuteNonQueryAsync();
            }
        }

        await using (var verify = new ApplicationDbContext(options, new NoopMediator()))
        {
            var persisted = await verify.JournalVouchers
                .Where(x => x.OrganizationId == VoucherOrganizationId && x.EnvironmentId == VoucherEnvironmentId)
                .Select(x => x.VoucherNo)
                .ToListAsync();
            Assert.Equal(
                new[] { "JV-S5-A", "JV-S5-C", "JV-S5-D", "JV-S5-LEGACY-1", "JV-S5-LEGACY-2" },
                persisted.Order(StringComparer.Ordinal).ToArray());
        }

        // ⑤ 索引定义本身：唯一 + partial。读 pg_index 而不是读 EF 模型——EF 模型是被测方自己的说法。
        await using (var introspect = new ApplicationDbContext(options, new NoopMediator()))
        {
            await introspect.Database.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("""
                SELECT i.indisunique, pg_get_expr(i.indpred, i.indrelid)
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indexrelid
                JOIN pg_class t ON t.oid = i.indrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = @schema AND t.relname = 'journal_vouchers'
                  AND pg_get_indexdef(i.indexrelid) LIKE '%source_type%'
                """, (NpgsqlConnection)introspect.Database.GetDbConnection());
            command.Parameters.AddWithValue("schema", ErpFacts.Schema);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "来源两列上没有任何索引。");
            Assert.True(reader.GetBoolean(0), "来源两列的索引不是唯一索引。");
            Assert.False(await reader.IsDBNullAsync(1), "来源两列的唯一索引没有 partial 过滤。");
            // ⭐ 全等，不是 Contains：子串判据放行「在后面追加一条豁免 conjunct」这类变异。
            Assert.Equal(ExpectedSourceIndexPredicate, reader.GetString(1), StringComparer.Ordinal);
            Assert.False(await reader.ReadAsync(), "来源两列上出现了不止一条索引。");
        }
    }

    /// <summary>
    /// #3278 / S5 ②：5 个查重位点**认来源单据、不认凭证号**，并且重放只记一张。
    ///
    /// 每一格的夹具都刻意让「凭证号」与「查重键」分道扬镳——库里先有一张
    /// **分配器短号形状**（<c>JV-yyyyMMdd-NNNNNN</c>，即 S6 落地后的形状）但来源身份正确的凭证。
    /// 改前的 <c>x.VoucherNo == …</c> 谓词在这种行上**匹配不上**，于是会再记一张；
    /// 改后按来源两列定位才命中。⇒ 把任一位点改回按凭证号定位，对应那一格就转红。
    ///
    /// <b>为什么不是 InMemory</b>：这一格的两条读数（重放只记一张 / 换号后仍认得出）同时依赖
    /// 查重谓词与那条唯一索引；InMemory 看不见索引，谓词写错时它只会多一行不会报错。
    ///
    /// <b>值域边界</b>：本格走的是 5 个**查重**位点，不是 17 个建凭证位点。
    /// </summary>
    [ErpCostPostgresFact(Timeout = 180_000)]
    public async Task PostgreSQL_dedup_sites_key_on_the_source_document_even_when_the_voucher_number_differs()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        const string org = "org-001";
        const string env = "env-dev";
        var postingDate = new DateOnly(2026, 9, 15);

        // 走生产装配（AddErpPostgreSqlPersistence + ErpCodingService），分配器才是 EF 持久化那一套：
        // 无参 new ErpCodingService() 用的是**进程内**分配器，每个 handler 实例各有一份，
        // 第二次调用拿不到第一次写下的幂等键 ⇒ IsIdempotentReplay 恒 false，位点 ②/④ 的重放分支根本走不到。
        await using var provider = CreateErpPersistenceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        foreach (var (code, name, type) in new (string, string, GLAccountType)[]
        {
            ("1401", "Inventory", GLAccountType.Asset),
            ("1122", "Accounts receivable", GLAccountType.Asset),
            ("1123", "Supplier prepayment", GLAccountType.Asset),
            ("2202", "Accounts payable", GLAccountType.Liability),
            ("5001", "Direct payable expense", GLAccountType.Expense),
            ("6001", "Sales returns", GLAccountType.Expense),
            ("6603", "Realized exchange loss", GLAccountType.Expense),
            ("6604", "Realized exchange gain", GLAccountType.Expense),
            ("GR-IR", "Goods receipt invoice receipt", GLAccountType.Liability),
            ("BANK-001", "Bank", GLAccountType.Asset),
        })
        {
            db.GLAccounts.Add(GLAccount.Create(org, env, code, name, type));
        }

        await db.SaveChangesAsync();

        // ── 位点 ①：GR/IR 计提消费者（PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual）
        await ErpFinanceSourceDocumentFixtures.SeedPurchaseReceiptAsync(db, "RCV-S5-DEDUP", "SUP-001", org, env);
        // ⭐ #3278 / S6：序列段取 99xxxx。S6 之后本用例里的建单命令**自己**会向同一条
        // `journal-voucher` 规则取号（`JV-{当天}-000001` 起数），预置号若也写 000001 就会撞
        // `(org, env, voucher_no)` 唯一索引——那是夹具冲突，不是被测行为。
        // 99xxxx 在**当天**按序列避开（计数器从 1 起数，跑不到 99 万），在**别的日期**按日期段避开，
        // 两个方向都不依赖「今天是哪天」⇒ 不是定时炸弹。形状仍是 S6 的短号形状。
        db.JournalVouchers.Add(SourceKeyedVoucher(
            "JV-20260915-990001", JournalVoucherSourceType.GoodsReceiptIrAccrual, "RCV-S5-DEDUP", postingDate, org, env));
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(db, deadLetters, ErpTestCoding.For(db)).HandleAsync(
            GoodsReceiptRecordedEvent("evt-s5-grir-1", "RCV-S5-DEDUP", org, env), CancellationToken.None);
        await db.SaveChangesAsync();
        // 换一个 EventId 再投一次：同一个 EventId 会被消费者 inbox 短路，走不到查重那一行。
        await new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(db, deadLetters, ErpTestCoding.For(db)).HandleAsync(
            GoodsReceiptRecordedEvent("evt-s5-grir-2", "RCV-S5-DEDUP", org, env), CancellationToken.None);
        await db.SaveChangesAsync();
        await AssertSingleVoucherForSourceAsync(db, JournalVoucherSourceType.GoodsReceiptIrAccrual, "RCV-S5-DEDUP", "JV-20260915-990001");
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));

        // ── 位点 ②：RegisterAccountPayablePayment（批准即执行，分配器重放路径）
        await ErpFinanceSourceDocumentFixtures.SeedSupplierInvoiceAsync(db, "INV-S5-PAY", "SUP-001", org, env);
        await new CreateAccountPayableCommandHandler(db, coding).Handle(
            new CreateAccountPayableCommand(org, env, "AP-S5-PAY", "INV-S5-PAY", "SUP-001", 100m, "CNY", postingDate, postingDate.AddDays(30), "NET30"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        var registerPayment = new RegisterAccountPayablePaymentCommand(org, env, "AP-S5-PAY", 40m, postingDate, "BANK-001", "idem-s5-ap-pay");
        await new RegisterAccountPayablePaymentCommandHandler(db, coding).Handle(registerPayment, CancellationToken.None);
        await db.SaveChangesAsync();
        var paymentExecutionNo = (await db.PaymentExecutions.AsNoTracking().SingleAsync(x => x.SupplierCode == "SUP-001")).PaymentExecutionNo;
        // ⭐ #3278 / S6 抽掉了这里原本手工制造的前提：改前凭证号**就是**付款执行单号，
        // 所以 S5 要先把凭证号手工改名，才能检验查重谓词认的是来源不是凭证号（那个夹具方法已随本次改动删除）。
        // S6 之后生产代码自己取分配器短号，那条巧合不复存在 —— 改名也执行不下去
        //（库里已经没有以付款执行单号为凭证号的行，那条改名的行数断言必红）。
        // 于是这里改成**断言这条分离由生产行为提供**：谁把 S6 的取号改回复用付款执行单号，本条立刻红。
        db.ChangeTracker.Clear();
        var registeredPaymentVoucherNo = (await db.JournalVouchers.AsNoTracking().SingleAsync(x =>
            x.SourceType == JournalVoucherSourceType.PaymentExecution.Code && x.SourceNo == paymentExecutionNo)).VoucherNo;
        Assert.NotEqual(paymentExecutionNo, registeredPaymentVoucherNo);
        await new RegisterAccountPayablePaymentCommandHandler(db, coding).Handle(registerPayment, CancellationToken.None);
        await db.SaveChangesAsync();
        await AssertSingleVoucherForSourceAsync(db, JournalVoucherSourceType.PaymentExecution, paymentExecutionNo, registeredPaymentVoucherNo);

        // ── 位点 ③：ExecutePaymentExecution（先批准后执行）
        await ErpFinanceSourceDocumentFixtures.SeedSupplierInvoiceAsync(db, "INV-S5-EXEC", "SUP-002", org, env);
        await new CreateAccountPayableCommandHandler(db, coding).Handle(
            new CreateAccountPayableCommand(org, env, "AP-S5-EXEC", "INV-S5-EXEC", "SUP-002", 100m, "CNY", postingDate, postingDate.AddDays(30), "NET30"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        var approvedNo = await new ApprovePaymentExecutionCommandHandler(db, coding).Handle(
            new ApprovePaymentExecutionCommand(org, env, "AP-S5-EXEC", 30m, postingDate, "BANK-001", "idem-s5-ap-approve"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        db.JournalVouchers.Add(SourceKeyedVoucher(
            "JV-20260915-990003", JournalVoucherSourceType.PaymentExecution, approvedNo, postingDate, org, env));
        await db.SaveChangesAsync();
        await new ExecutePaymentExecutionCommandHandler(db, coding).Handle(
            new ExecutePaymentExecutionCommand(org, env, approvedNo, "u-finance"), CancellationToken.None);
        await db.SaveChangesAsync();
        await AssertSingleVoucherForSourceAsync(db, JournalVoucherSourceType.PaymentExecution, approvedNo, "JV-20260915-990003");

        // ── 位点 ④：RegisterAccountReceivableCollection（登记即匹配，分配器重放路径）
        await ErpFinanceSourceDocumentFixtures.SeedDeliveryOrderAsync(db, "DO-S5-COLLECT", "CUS-001", org, env);
        await new CreateAccountReceivableCommandHandler(db, coding).Handle(
            new CreateAccountReceivableCommand(org, env, "AR-S5-COLLECT", "DO-S5-COLLECT", "CUS-001", 80m, "CNY", postingDate, postingDate.AddDays(14), "NET14"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        var registerCollection = new RegisterAccountReceivableCollectionCommand(org, env, "AR-S5-COLLECT", 20m, postingDate, "BANK-001", "idem-s5-ar-collect");
        await new RegisterAccountReceivableCollectionCommandHandler(db, coding).Handle(registerCollection, CancellationToken.None);
        await db.SaveChangesAsync();
        var collectionReceiptNo = (await db.CashReceipts.AsNoTracking().SingleAsync()).CashReceiptNo;
        // 理由同位点 ②：S6 之后「凭证号 == 收款单号」这条巧合由生产代码拆掉，不再手工改名。
        db.ChangeTracker.Clear();
        var registeredCollectionVoucherNo = (await db.JournalVouchers.AsNoTracking().SingleAsync(x =>
            x.SourceType == JournalVoucherSourceType.CashReceipt.Code && x.SourceNo == collectionReceiptNo)).VoucherNo;
        Assert.NotEqual(collectionReceiptNo, registeredCollectionVoucherNo);
        await new RegisterAccountReceivableCollectionCommandHandler(db, coding).Handle(registerCollection, CancellationToken.None);
        await db.SaveChangesAsync();
        await AssertSingleVoucherForSourceAsync(db, JournalVoucherSourceType.CashReceipt, collectionReceiptNo, registeredCollectionVoucherNo);

        // ── 位点 ⑤：MatchCashReceipt（先登记后匹配）
        await ErpFinanceSourceDocumentFixtures.SeedDeliveryOrderAsync(db, "DO-S5-MATCH", "CUS-002", org, env);
        await new CreateAccountReceivableCommandHandler(db, coding).Handle(
            new CreateAccountReceivableCommand(org, env, "AR-S5-MATCH", "DO-S5-MATCH", "CUS-002", 80m, "CNY", postingDate, postingDate.AddDays(14), "NET14"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        var registeredReceiptNo = await new RegisterCashReceiptCommandHandler(db, coding).Handle(
            new RegisterCashReceiptCommand(org, env, "AR-S5-MATCH", 25m, postingDate, "BANK-001", "idem-s5-ar-register"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        db.JournalVouchers.Add(SourceKeyedVoucher(
            "JV-20260915-990005", JournalVoucherSourceType.CashReceipt, registeredReceiptNo, postingDate, org, env));
        await db.SaveChangesAsync();
        await new MatchCashReceiptCommandHandler(db, coding).Handle(
            new MatchCashReceiptCommand(org, env, registeredReceiptNo), CancellationToken.None);
        await db.SaveChangesAsync();
        await AssertSingleVoucherForSourceAsync(db, JournalVoucherSourceType.CashReceipt, registeredReceiptNo, "JV-20260915-990005");

        // ⭐ 哨兵格：五格若因为「整表压根没多出任何凭证」而全绿（例如夹具根本没走到生产路径），
        // 这里就读不到那些**本来就该新增**的凭证。夹具建了 2 张应付 + 2 张应收，各自在建单时写一张凭证
        // （AP×2、AR×2），加上五个位点各预置 1 张（GRIR、APPAY×2、ARCOL×2）⇒ 恰好 9 张、族分布唯一。
        // 这一格与上面五格的鉴别方向相反：上面测「不该多出来的没多出来」，这里测「该有的真写出来了」。
        db.ChangeTracker.Clear();
        var allSources = await db.JournalVouchers.AsNoTracking()
            .Where(x => x.OrganizationId == org && x.EnvironmentId == env)
            .Select(x => x.SourceType)
            .ToListAsync();
        Assert.Equal(
            new[] { "AP", "AP", "APPAY", "APPAY", "AR", "AR", "ARCOL", "ARCOL", "GRIR" },
            allSources.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// #3278 / S5 ③：建唯一索引这条迁移的 <c>Up()</c> / <c>Down()</c> **真跑一次**。
    ///
    /// 本仓判例：迁移 <c>Down()</c> 写错**不会有任何红**——模型快照只描述 <c>Up()</c> 之后的形状，
    /// 没有任何门禁会去跑回滚。所以这一格把 Up → Down → （撞重复）→ Up 全部真执行。
    ///
    /// 顺带实测母票 §A1/§A2 点名的那条风险：**表里已有重复来源行时，建唯一索引的迁移会失败**。
    /// 这一格证明它在干净库上不撞（②、③ 两步都成功），而在真有重复时**明确报错而不是静默吞掉**。
    /// </summary>
    [ErpCostPostgresFact(Timeout = 120_000)]
    public async Task PostgreSQL_source_unique_index_migration_up_and_down_run_for_real()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        const string uniqueIndexMigration = "20260915033607_AddJournalVoucherSourceDocumentUniqueIndex";
        const string sourceColumnMigration = "20260914134346_AddJournalVoucherSourceDocumentColumns";
        var postingDate = new DateOnly(2026, 9, 15);

        await using var db = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        db.GLAccounts.AddRange(
            GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1405-WIP", "Work in process", GLAccountType.Asset),
            GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1406-FINISHED-GOODS", "Finished goods inventory", GLAccountType.Asset));
        await db.SaveChangesAsync();
        await db.Database.OpenConnectionAsync();

        // ① Up() 之后：唯一 + partial。
        var afterUp = await JournalVoucherSourceIndexShapeAsync(db);
        Assert.True(afterUp.IsUnique);
        Assert.Equal(ExpectedSourceIndexPredicate, afterUp.Predicate, StringComparer.Ordinal);

        // ② Down()：回落到 S2 那一版，索引必须还在、且必须不再唯一也不再带过滤。
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(sourceColumnMigration);
        var afterDown = await JournalVoucherSourceIndexShapeAsync(db);
        Assert.False(afterDown.IsUnique);
        Assert.Null(afterDown.Predicate);

        // ③ 「不再唯一」用**写库**证，不只读 pg_index：同一来源两行必须都落得进去。
        db.ChangeTracker.Clear();
        db.JournalVouchers.Add(SourceKeyedVoucher("JV-S5-DOWN-1", JournalVoucherSourceType.GoodsReceiptIrAccrual, "RCV-S5-DOWN", postingDate));
        db.JournalVouchers.Add(SourceKeyedVoucher("JV-S5-DOWN-2", JournalVoucherSourceType.GoodsReceiptIrAccrual, "RCV-S5-DOWN", postingDate));
        await db.SaveChangesAsync();

        // ④ 带着这两行重新 Up()：建唯一索引必须**明确失败**（23505），不能静默跳过。
        var collision = await Assert.ThrowsAnyAsync<PostgresException>(() => migrator.MigrateAsync(uniqueIndexMigration));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, collision.SqlState);
        Assert.False((await JournalVoucherSourceIndexShapeAsync(db)).IsUnique);

        // ⑤ 干净库（去掉重复行）上 Up() 必须成功并重新装上唯一 + partial。
        db.ChangeTracker.Clear();
        db.JournalVouchers.RemoveRange(await db.JournalVouchers.Where(x => x.SourceNo == "RCV-S5-DOWN").ToListAsync());
        await db.SaveChangesAsync();
        await migrator.MigrateAsync(uniqueIndexMigration);
        var afterSecondUp = await JournalVoucherSourceIndexShapeAsync(db);
        Assert.True(afterSecondUp.IsUnique);
        Assert.Equal(ExpectedSourceIndexPredicate, afterSecondUp.Predicate, StringComparer.Ordinal);
    }

    /// <summary>
    /// #3278 / S5 来源两列唯一索引的 partial 谓词，**PostgreSQL deparse 之后的全形**。
    ///
    /// ⭐ <b>为什么钉全形而不是钉子串</b>（复审返修）：此前两处都写成
    /// <c>Assert.Contains("source_type")</c>。实测把四处 filter 定义同步改成
    /// <c>"… AND source_type &lt;&gt; 'APPAY'"</c>——一条让整个付款执行族退出幂等约束的索引——
    /// **30 格全部存活**，而 <c>pg_index</c> 读出的谓词确实已变、两行重复 APPAY 也确实
    /// <c>INSERT 0 2</c> 落了库。子串判据只证明「谓词提到了这两列」，证不了「谓词没别的东西」。
    ///
    /// ⚠️ <b>这个常量钉的是 PostgreSQL <c>ruleutils</c> 的 deparse 输出，⛔ 不是我们写进迁移的原文。</b>
    /// 同一条变异串在两侧读出的 Actual 并不相同，可见 deparse 确实在改写：
    /// <list type="bullet">
    /// <item>EF 模型侧（<c>JournalVoucherSourceContractTests</c>）：<c>… AND source_type &lt;&gt; 'APPAY'</c>——原样；</item>
    /// <item>真库侧（本常量）：<c>… AND ((source_type)::text &lt;&gt; 'APPAY'::text)</c>——PG 补了外层括号**和 <c>::text</c> 显式转换</item>
    /// </list>
    /// ⇒ 两串字面不同是**归一化的真实产物**，不是哪一侧被将就了；两侧各钉各的表示，别互相抄。
    ///
    /// <b>失效方向</b>：判据从 <c>Contains</c> 换成**全等**之后，「PG 怎么 deparse」就从无所谓变成了承重的。
    /// 日后 PostgreSQL 大版本若改括号 / 空格 / 转换的写法，这条全等会变成**环境相关的假红**。
    /// ⭐ 但这个方向是 <b>fail-closed</b>——它会在跑的时候大声炸，⛔ 不会静默放过一条被加了豁免的索引；
    /// 且 lane 已把镜像钉死在 <c>postgres:18</c>。⇒ **接受这个方向**，⛔ 不加容错、⛔ 不做归一化后比较
    /// （做了就等于把「谓词没别的东西」这条判据又还回给模糊匹配）。
    /// </summary>
    private const string ExpectedSourceIndexPredicate = "((source_type IS NOT NULL) AND (source_no IS NOT NULL))";

    /// <summary>
    /// 读 <c>pg_index</c> 而不是读 EF 模型——EF 模型是被测方自己的说法，回滚后它根本不会变。
    /// 断言「来源两列上有且只有一条索引」，否则 Down() 漏删时这里会读到第一条而不报错。
    /// </summary>
    private static async Task<(bool IsUnique, string? Predicate)> JournalVoucherSourceIndexShapeAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT i.indisunique, pg_get_expr(i.indpred, i.indrelid)
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indexrelid
            JOIN pg_class t ON t.oid = i.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = @schema AND t.relname = 'journal_vouchers'
              AND pg_get_indexdef(i.indexrelid) LIKE '%source_type%'
            """, (NpgsqlConnection)db.Database.GetDbConnection());
        command.Parameters.AddWithValue("schema", ErpFacts.Schema);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "来源两列上没有任何索引。");
        var shape = (reader.GetBoolean(0), await reader.IsDBNullAsync(1) ? null : reader.GetString(1));
        Assert.False(await reader.ReadAsync(), "来源两列上出现了不止一条索引。");
        return shape;
    }

    /// <summary>
    /// #3278 / S5 ④：<c>WOCADJ</c> 族在唯一键下的**收窄方向**，按 owner A2 的「这一格必须先量」落成可执行事实。
    ///
    /// 今天的凭证号是 <c>JV-WOCADJ-{workOrderId}-{sourceId}</c>，换键后是 <c>(WOCADJ, sourceId)</c>——
    /// **少了 <c>workOrderId</c> 一段**。所以两条读数方向相反，必须各钉一格：
    /// <list type="number">
    /// <item>同一工单、不同来源标识 ⇒ 两张凭证都落得进去（没有把正常业务挡住）；</item>
    /// <item>不同工单、**同一**来源标识 ⇒ 23505。这是**收窄**，是刻意的，不是缺陷。</item>
    /// </list>
    ///
    /// <b>为什么收窄可接受（开工前实读测量，扫描面与失效方向见下）</b>：
    /// <c>sourceId</c> 的生产侧取值共 7 处，全部来自「在 (org, env) 内唯一、且只归属一个工单」的单据标识——
    /// <c>ProductionReport.ReportNo</c>、<c>PendingMaterialCost.MovementId</c>、
    /// <c>StockMovementPostedPayload.InventoryMovementId</c>，以及工序结算的
    /// <c>{OperationTaskId}-r{rev}</c> / <c>…-void</c> / <c>machine-…</c> 四种串。
    /// ⭐ 工序任务那四项的承重依据在**生产者侧的物理约束**：MES
    /// <c>OperationTaskEntityTypeConfiguration.cs:77-78</c> 的
    /// <c>ak_operation_tasks_scope_task = HasAlternateKey(OrganizationId, EnvironmentId, OperationTaskIdValue)</c>
    /// **不含 <c>WorkOrderId</c>**。（Erp 侧 <c>GetOrCreateStateAsync(org, env, OperationTaskId)</c>
    /// 不带工单号只证明「Erp 把它当键用」，弱一档，⛔ 不作承重理由。）
    /// ⇒ 同一个 <c>sourceId</c> 不可能落在两个工单上，收窄在今天取不到值。
    ///
    /// <b>扫描面</b>：<c>git grep -n "PostLateAdjustmentAsync" -- backend</c> 去掉 <c>obj/</c> 与 <c>tests/</c>，
    /// 得 5 个调用点（其中 2 个是 <c>PostLateAdjustmentIfCapitalizedAsync</c> 包装，各自又有 2 个调用点）
    /// ⇒ 7 个 <c>sourceId</c> 表达式。
    ///
    /// <b>两条失效方向，都不会让本格转红</b>——本格断言的是**收窄存在**，不是收窄安全：
    /// ① 日后**新增**一个传「工单内才唯一」标识（裸工序号、裸行号）的调用点，收窄会变成真的塌号；
    /// ② ⭐ 这 7 个表达式共用同一个 <c>source_type = WOCADJ</c>，却来自**三个互不相干的命名空间**
    ///    （<c>RPT-*</c> 报工号 / GUID 库存移动号 / <c>{taskId}-r{n}</c> 修订串）。
    ///    每个命名空间**内部**的唯一性各有硬约束，⛔ 但**跨命名空间的互斥没有任何东西保证**，
    ///    今天只靠三种串的形状天然不重叠。
    /// 两条都只能靠改动时重做这次测量。
    /// </summary>
    [ErpCostPostgresFact(Timeout = 120_000)]
    public async Task PostgreSQL_work_order_cost_adjustment_key_drops_the_work_order_segment()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var occurredAtUtc = DateTimeOffset.Parse("2026-09-15T04:00:00Z");

        await using var db = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        // 预建两个科目：PostLateAdjustmentAsync 会按**库里**的科目补建，同一 UoW 内连调两次会重复 Add。
        db.GLAccounts.AddRange(
            GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "1405-WIP", "Work in process", GLAccountType.Asset),
            GLAccount.Create(VoucherOrganizationId, VoucherEnvironmentId, "5101-PRODUCTION-VARIANCE", "Production cost variance", GLAccountType.Expense));
        var first = WorkOrderCost.Open(VoucherOrganizationId, VoucherEnvironmentId, "WO-S5-A", "FG-S5");
        var second = WorkOrderCost.Open(VoucherOrganizationId, VoucherEnvironmentId, "WO-S5-B", "FG-S5");
        foreach (var cost in new[] { first, second })
        {
            cost.RecordLabor($"RPT-{cost.WorkOrderId}", "WC-S5", 2m, 50m, "CNY", false, occurredAtUtc.AddDays(-1));
            db.WorkOrderCosts.Add(cost);
        }

        await db.SaveChangesAsync();

        // ① 同一工单、两个不同来源标识 ⇒ 两张凭证。
        await CostVariancePosting.PostLateAdjustmentAsync(db, ErpTestCoding.For(db), first, -10m, "MOV-S5-0001", occurredAtUtc, CancellationToken.None);
        await CostVariancePosting.PostLateAdjustmentAsync(db, ErpTestCoding.For(db), first, -20m, "MOV-S5-0002", occurredAtUtc, CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.JournalVouchers.CountAsync(x => x.SourceType == JournalVoucherSourceType.WorkOrderCostAdjustment.Code));

        // ② 不同工单、同一来源标识 ⇒ 23505。
        //
        // ⚠️ #3278 / S7 改了这一步的机理，而不是结论。S5 当时的推论是
        // 「凭证号 JV-WOCADJ-{WO-S5-B}-MOV-S5-0001 与已有那张不同，所以撞的只可能是来源索引」——
        // S7 把凭证号改成分配器短号后这条前提失效：分配器的幂等键就是 (source_type, source_no)，
        // 所以同一个 MOV-S5-0001 拿回的是**同一个凭证号**，两条唯一索引都会被犯。
        // ⭐ 这是变强不是变弱（多一道防线），但它把「究竟哪条索引在承重」变成了 PostgreSQL 的检查顺序。
        // 因此拆成两格：②a 钉住 S7 新结果（同来源 ⇒ 同号），
        //          ②b 绕开分配器、手给一个全新凭证号，把 S5 要证的「收窄在来源索引上」单独量出来。
        var existingAdjustmentNo = await db.JournalVouchers
            .Where(x => x.SourceType == JournalVoucherSourceType.WorkOrderCostAdjustment.Code && x.SourceNo == "MOV-S5-0001")
            .Select(x => x.VoucherNo)
            .SingleAsync();

        // ②a：同一来源标识在另一个工单上再记一次 ⇒ 分配器回放同一个号 ⇒ 23505。
        await CostVariancePosting.PostLateAdjustmentAsync(db, ErpTestCoding.For(db), second, -30m, "MOV-S5-0001", occurredAtUtc, CancellationToken.None);
        Assert.Equal(
            existingAdjustmentNo,
            db.ChangeTracker.Entries<JournalVoucher>().Single(x => x.State == EntityState.Added).Entity.VoucherNo);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        db.ChangeTracker.Clear();

        // ②b：绕开分配器直接建一张「新凭证号 + 旧来源标识 + 另一个工单」的凭证。
        //     凭证号唯一索引这次撞不上，所以报出来的只能是来源索引——
        //     这就是「(WOCADJ, sourceId) 少了 workOrderId 一段」的直接读数。
        db.JournalVouchers.Add(SourceKeyedVoucher(
            "JV-20260915-090001",
            JournalVoucherSourceType.WorkOrderCostAdjustment,
            "MOV-S5-0001",
            DateOnly.FromDateTime(occurredAtUtc.UtcDateTime)));
        var sourceError = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var sourcePostgres = Assert.IsType<PostgresException>(sourceError.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, sourcePostgres.SqlState);
        Assert.Contains("source_type", sourcePostgres.ConstraintName, StringComparison.Ordinal);
        Assert.DoesNotContain("voucher_no", sourcePostgres.ConstraintName, StringComparison.Ordinal);
    }

    /// <summary>
    /// #3278 / S7：工单成本资本化凭证（位点 ④）的凭证号来自分配器，且在**真库 + CAP 重投**下只记一张。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三件 EF InMemory 证不了的事：
    /// ① 分配器走的是 <c>EfCoreCodeStore</c>，幂等键与计数器**真落库**（<c>code_idempotency_keys</c> /
    ///    <c>code_counters</c>），所以「同一来源再要一次号拿回同一个码」是跨 DbContext 实例成立的；
    /// ② <c>(organization_id, environment_id, voucher_no)</c> 与来源两列两条唯一索引都真的在，
    ///    重投若真写出第二张凭证会 23505 而不是静默多一行；
    /// ③ 分配器产出的 18 字符短号真的落得进 <c>voucher_no</c>。
    /// </para>
    /// <para>
    /// <b>重投形态</b>：用**同一个 EventId** 再投一次——这才是 CAP 重投的形状。
    /// ⚠️ 本位点（<c>StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost</c> 的资本化分支）
    /// ⛔ 没有来源两列的查重早退，挡住重投的是 <c>ErpProcessedIntegrationEventInbox</c>；
    /// 换一个 EventId 再投会走到写入并撞唯一索引，那是**换了一件事**，不是本格要量的。
    /// </para>
    /// </remarks>
    [ErpCostPostgresFact(Timeout = 120_000)]
    public async Task PostgreSQL_capitalization_voucher_number_comes_from_the_allocator_and_survives_cap_redelivery()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        const string workOrderId = "WO-S7-PG";
        const string movementId = "MOVE-S7-PG";
        var postedAtUtc = DateTimeOffset.Parse("2026-09-15T05:00:00Z");

        // 本格走 CostingIntegrationEventUnitOfWork.SaveEntitiesAsync 会**派发领域事件**（其他真库用例只调
        // SaveChangesAsync，不派发），而凭证发布的 converter handler 链上挂着一排发布侧依赖。
        // 本格量的是凭证号与重投，不是外发链路，所以把 mediator 换成 no-op——
        // 与本类其余直接构造 ApplicationDbContext 的用例同一个口径。
        await using var provider = CreateErpPersistenceProvider(
            services => services.AddSingleton<IMediator>(new NoopMediator()));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();

        var cost = WorkOrderCost.Open(VoucherOrganizationId, VoucherEnvironmentId, workOrderId, "FG-S7");
        cost.RecordLabor("RPT-S7-PG", "WC-S7", 1m, 80m, "CNY", false, postedAtUtc.AddHours(-1));
        cost.Complete(4m, 1, 0, postedAtUtc.AddMinutes(-30));
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var movement = new StockMovementPostedIntegrationEvent(
            "evt-s7-pg-capitalization", InventoryIntegrationEventTypes.StockMovementPosted, 1, postedAtUtc,
            InventoryIntegrationEventSources.BusinessInventory, workOrderId, workOrderId,
            VoucherOrganizationId, VoucherEnvironmentId, "inventory", "idem-s7-pg-capitalization",
            new StockMovementPostedPayload(
                movementId, "inbound", InventoryIntegrationEventSources.BusinessMes, "FGR-S7-PG", workOrderId,
                $"mes:finished-goods-receipt:{movementId}", "FG-S7", "ea", "production", "fg-store", null, null,
                "unrestricted", "organization", VoucherOrganizationId, 4m, postedAtUtc, 20m, 80m));
        var handler = new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(db, deadLetters, db, coding);

        await handler.HandleAsync(movement, CancellationToken.None);
        // CAP 重投：同一个 EventId 原样再来一次。
        await handler.HandleAsync(movement, CancellationToken.None);

        db.ChangeTracker.Clear();
        var voucher = Assert.Single(await db.JournalVouchers.AsNoTracking()
            .Where(x => x.SourceType == JournalVoucherSourceType.WorkOrderCapitalization.Code && x.SourceNo == movementId)
            .ToListAsync());
        Assert.Matches(@"^JV-\d{8}-\d{6}$", voucher.VoucherNo);
        Assert.NotEqual($"JV-WOC-{workOrderId}-{movementId}", voucher.VoucherNo);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));

        // 幂等键真落了库，且再要一次号拿回同一个码——这条只有 store-backed 分配器才成立。
        var idempotencyKey = ConsumerJournalVoucherNumber.IdempotencyKeyOf(
            JournalVoucherSourceType.WorkOrderCapitalization, movementId);
        var record = Assert.Single(await db.CodeIdempotencyKeys.AsNoTracking()
            .Where(x => x.RuleKey == ConsumerJournalVoucherNumber.RuleKey && x.IdempotencyKey == idempotencyKey)
            .ToListAsync());
        Assert.Equal(voucher.VoucherNo, record.Code);

        using var replayScope = provider.CreateScope();
        var replayCoding = replayScope.ServiceProvider.GetRequiredService<ErpCodingService>();
        var replay = await ConsumerJournalVoucherNumber.TryAllocateAsync(
            replayCoding, VoucherOrganizationId, VoucherEnvironmentId,
            JournalVoucherSourceType.WorkOrderCapitalization, movementId, CancellationToken.None);
        Assert.Equal(voucher.VoucherNo, replay.Code);
    }

    private static ServiceProvider CreateErpPersistenceProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(PostJournalVoucherCommand).Assembly));
        services.AddErpPostgreSqlPersistence(ErpPostgresLaneDatabase.ConnectionString);
        services.AddScoped<ErpCodingService>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }


    private static async Task AssertSingleVoucherForSourceAsync(
        ApplicationDbContext db,
        JournalVoucherSourceType sourceType,
        string sourceNo,
        string expectedVoucherNo)
    {
        db.ChangeTracker.Clear();
        var matches = await db.JournalVouchers.AsNoTracking()
            .Where(x => x.SourceType == sourceType.Code && x.SourceNo == sourceNo)
            .Select(x => x.VoucherNo)
            .ToListAsync();
        Assert.Equal([expectedVoucherNo], matches);
    }

    private static PurchaseReceiptRecordedIntegrationEvent GoodsReceiptRecordedEvent(
        string eventId,
        string purchaseReceiptNo,
        string organizationId,
        string environmentId)
        => new(
            eventId,
            ErpIntegrationEventTypes.PurchaseReceiptRecorded,
            ErpIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-09-15T02:00:00Z"),
            ErpIntegrationEventSources.BusinessErp,
            $"corr-{eventId}",
            $"cause-{eventId}",
            organizationId,
            environmentId,
            "system:business-erp",
            $"idem-{eventId}",
            new PurchaseReceiptRecordedPayload(
                Guid.CreateVersion7().ToString(),
                purchaseReceiptNo,
                $"PO-SRC-{purchaseReceiptNo}",
                "SUP-001",
                "SITE-001",
                "accepted"));

    private static JournalVoucher SourceKeyedVoucher(
        string voucherNo,
        JournalVoucherSourceType sourceType,
        string sourceNo,
        DateOnly postingDate,
        string organizationId = VoucherOrganizationId,
        string environmentId = VoucherEnvironmentId)
        => JournalVoucher.Post(
            organizationId,
            environmentId,
            voucherNo,
            postingDate,
            organizationId == VoucherOrganizationId
                ?
                [
                    new JournalVoucherLineDraft("1406-FINISHED-GOODS", 10m, 0m, "debit leg"),
                    new JournalVoucherLineDraft("1405-WIP", 0m, 10m, "credit leg"),
                ]
                :
                [
                    new JournalVoucherLineDraft("1401", 10m, 0m, "debit leg"),
                    new JournalVoucherLineDraft("GR-IR", 0m, 10m, "credit leg"),
                ],
            sourceType,
            sourceNo);

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_rework_origin_arriving_after_cost_events_stays_isolated_and_queryable()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var reportedAtUtc = DateTimeOffset.Parse("2026-08-30T03:30:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            setupDb.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
                "org-pg", "env-pg", "WC-PG", 50m, "CNY",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), null, 1,
                "system:test", "governed PostgreSQL rate", DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
            var sourceCost = WorkOrderCost.Open("org-pg", "env-pg", "WO-SOURCE-PG", "FG-PG");
            sourceCost.RecordLabor("RPT-SOURCE-PG", "WC-PG", 1m, 25m, "CNY", false, reportedAtUtc.AddDays(-1));
            setupDb.WorkOrderCosts.Add(sourceCost);
            await setupDb.SaveChangesAsync();
        }

        var report = new ProductionReportRecordedIntegrationEvent(
            "evt-rework-report-pg", MesIntegrationEventTypes.ProductionReportRecorded,
            MesIntegrationEventVersions.V1, reportedAtUtc, MesIntegrationEventSources.BusinessMes,
            "RPT-RW-PG", "WO-RW-PG", "org-pg", "env-pg", "operator:test", "rework-report-pg",
            new ProductionReportRecordedPayload(
                "RPT-RW-PG", "WO-RW-PG", "OP-RW-PG", "WC-PG", null,
                2m, 0m, 0m, "ea", 1m, reportedAtUtc, false, MaterialMovementCount: 0));
        await using (var reportDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
                    reportDb, deadLetters, reportDb, new PostgreSqlWorkOrderCostMutationLock(reportDb), ErpTestCoding.For(reportDb))
                .HandleAsync(report, CancellationToken.None);
        }

        var created = new ReworkWorkOrderCreatedIntegrationEvent(
            "evt-rework-created-pg", MesIntegrationEventTypes.ReworkWorkOrderCreated,
            MesIntegrationEventVersions.V1, reportedAtUtc.AddMinutes(1),
            MesIntegrationEventSources.BusinessMes, "corr-rework-pg", "cause-ncr-pg",
            "org-pg", "env-pg", "system:business-mes", "rework-created-pg",
            new ReworkWorkOrderCreatedPayload(
                "ncr-pg", "NCR-PG", "WO-RW-PG", "WO-SOURCE-PG", "OP-SOURCE-PG",
                "FG-PG", 2m, "LOT-PG", null, reportedAtUtc.AddMinutes(1)));
        await using (var attributionDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            var handler = new ReworkWorkOrderCreatedIntegrationEventHandlerForAttributeCost(
                attributionDb, attributionDb, new PostgreSqlWorkOrderCostMutationLock(attributionDb), deadLetters);
            await handler.HandleAsync(created, CancellationToken.None);
            await handler.HandleAsync(created, CancellationToken.None);
        }

        var completed = new WorkOrderCompletedIntegrationEvent(
            "evt-rework-completed-pg", MesIntegrationEventTypes.WorkOrderCompleted,
            MesIntegrationEventVersions.V1, reportedAtUtc.AddMinutes(2),
            MesIntegrationEventSources.BusinessMes, "WO-RW-PG", "WO-RW-PG",
            "org-pg", "env-pg", "system:mes", "rework-completed-pg",
            new WorkOrderCompletedPayload(
                "WO-RW-PG", "FG-PG", 2m, 2m, 0m, reportedAtUtc.AddMinutes(2), 1, 0));
        await using (var completionDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await new WorkOrderCompletedIntegrationEventHandlerForCapitalizeCost(
                    completionDb, deadLetters, completionDb)
                .HandleAsync(completed, CancellationToken.None);
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(assertDb);
        var source = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-SOURCE-PG");
        var rework = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-RW-PG");
        Assert.Equal(25m, source.TotalAccumulatedCost);
        Assert.False(source.IsRework);
        Assert.Equal(100m, rework.LaborCost);
        Assert.True(rework.CapitalizationPublished);
        Assert.Equal("ncr-pg", rework.SourceNcrId);
        Assert.Equal("WO-SOURCE-PG", rework.SourceWorkOrderId);
        Assert.Equal("FG-PG", rework.SkuCode);
        Assert.Single(await assertDb.ProcessedIntegrationEvents
            .Where(x => x.ConsumerName == ReworkWorkOrderCreatedIntegrationEventHandlerForAttributeCost.ConsumerName)
            .ToArrayAsync());

        var byNcr = await new ListWorkOrderCostsQueryHandler(assertDb).Handle(
            new ListWorkOrderCostsQuery("org-pg", "env-pg", SourceNcrId: "ncr-pg"),
            CancellationToken.None);
        Assert.Equal("rework", Assert.Single(byNcr.Items).CostKind);
        Assert.Equal(100m, byNcr.ReworkCostTotal);
        Assert.Equal(0m, byNcr.OrdinaryCostTotal);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_machine_overhead_reads_translate_and_isolate_work_order_period_and_scope()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var completedAtUtc = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.MigrateAsync();
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);

        var period = AccountingPeriod.Open(
            "org-read", "env-read", "2026-08", new(2026, 8, 1), new(2026, 8, 31));
        var rate = WorkCenterMachineOverheadRate.DefineApplicable(
            "org-read", "env-read", "WC-READ", "2026-08",
            30_000m, 10_000m, 1_000m, "CNY", 4,
            "system:test", "approved read contract rate", completedAtUtc.AddMonths(-1));
        var otherRate = WorkCenterMachineOverheadRate.DefineApplicable(
            "org-read", "env-other", "WC-READ", "2026-08",
            30_000m, 10_000m, 1_000m, "CNY", 1,
            "system:test", "scope distractor", completedAtUtc.AddMonths(-1));
        var otherOrganizationPeriod = AccountingPeriod.Open(
            "org-other", "env-read", "2026-08", new(2026, 8, 1), new(2026, 8, 31));
        var otherOrganizationRate = WorkCenterMachineOverheadRate.DefineApplicable(
            "org-other", "env-read", "WC-READ", "2026-08",
            30_000m, 10_000m, 1_000m, "CNY", 1,
            "system:test", "organization distractor", completedAtUtc.AddMonths(-1));
        var otherPeriod = AccountingPeriod.Open(
            "org-read", "env-read", "2026-07", new(2026, 7, 1), new(2026, 7, 31));
        var otherPeriodRate = WorkCenterMachineOverheadRate.DefineApplicable(
            "org-read", "env-read", "WC-READ", "2026-07",
            30_000m, 10_000m, 1_000m, "CNY", 1,
            "system:test", "period distractor", completedAtUtc.AddMonths(-2));
        var otherWorkOrderRate = WorkCenterMachineOverheadRate.DefineApplicable(
            "org-read", "env-read", "WC-OTHER-WO", "2026-08",
            30_000m, 10_000m, 1_000m, "CNY", 1,
            "system:test", "work order distractor", completedAtUtc.AddMonths(-1));
        db.AddRange(period, rate, otherRate, otherOrganizationPeriod, otherOrganizationRate,
            otherPeriod, otherPeriodRate, otherWorkOrderRate);
        await db.SaveChangesAsync();

        var settlement = OperationMachineOverheadSettlement.CreateApplied(
            "org-read", "env-read", "WO-SAME", "OP-READ", "WC-READ", 3,
            completedAtUtc, "DEVICE-READ", 2 * TimeSpan.TicksPerHour,
            "single-device-active-minus-explicit-pause-v1", rate.Id, "2026-08", 4,
            "CNY", 30m, 10m, "evt-read", new string('a', 64));
        var state = OperationMachineOverheadSettlementState.Open("org-read", "env-read", "OP-READ");
        state.ApplySettlement(3);
        var cost = WorkOrderCost.Open("org-read", "env-read", "WO-SAME", "FG-READ");
        cost.RecordMachineOverhead(settlement);
        var reconciliation = WorkCenterMachineOverheadReconciliation.Record(
            "org-read", "env-read", "WC-READ", "2026-08", rate.Id, 4, "CNY",
            100m, 40m, 2 * TimeSpan.TicksPerHour, 60m, 20m, 80m,
            0, AbnormalDowntimeDisposition.None, 1, "user:accountant",
            "ledger:2026-08", "month-end actual pool", completedAtUtc.AddHours(1));

        var otherSettlement = OperationMachineOverheadSettlement.CreateApplied(
            "org-read", "env-other", "WO-SAME", "OP-OTHER", "WC-READ", 1,
            completedAtUtc, "DEVICE-OTHER", 9 * TimeSpan.TicksPerHour,
            "single-device-active-minus-explicit-pause-v1", otherRate.Id, "2026-08", 1,
            "CNY", 30m, 10m, "evt-other", new string('b', 64));
        var otherState = OperationMachineOverheadSettlementState.Open("org-read", "env-other", "OP-OTHER");
        otherState.ApplySettlement(1);
        var otherCost = WorkOrderCost.Open("org-read", "env-other", "WO-SAME", "FG-OTHER");
        otherCost.RecordMachineOverhead(otherSettlement);
        var otherEnvironmentReconciliation = WorkCenterMachineOverheadReconciliation.Record(
            "org-read", "env-other", "WC-READ", "2026-08", otherRate.Id, 1, "CNY",
            900m, 300m, 9 * TimeSpan.TicksPerHour, 270m, 90m, 360m,
            0, AbnormalDowntimeDisposition.None, 1, "user:accountant",
            "ledger:other-environment", "environment distractor", completedAtUtc.AddHours(1));

        var otherOrganizationSettlement = OperationMachineOverheadSettlement.CreateApplied(
            "org-other", "env-read", "WO-SAME", "OP-OTHER-ORG", "WC-READ", 1,
            completedAtUtc, "DEVICE-OTHER-ORG", 7 * TimeSpan.TicksPerHour,
            "single-device-active-minus-explicit-pause-v1", otherOrganizationRate.Id, "2026-08", 1,
            "CNY", 30m, 10m, "evt-other-org", new string('c', 64));
        var otherOrganizationState = OperationMachineOverheadSettlementState.Open(
            "org-other", "env-read", "OP-OTHER-ORG");
        otherOrganizationState.ApplySettlement(1);
        var otherOrganizationCost = WorkOrderCost.Open("org-other", "env-read", "WO-SAME", "FG-OTHER-ORG");
        otherOrganizationCost.RecordMachineOverhead(otherOrganizationSettlement);
        var otherOrganizationReconciliation = WorkCenterMachineOverheadReconciliation.Record(
            "org-other", "env-read", "WC-READ", "2026-08", otherOrganizationRate.Id, 1, "CNY",
            210m, 70m, 7 * TimeSpan.TicksPerHour, 210m, 70m, 280m,
            0, AbnormalDowntimeDisposition.None, 1, "user:accountant",
            "ledger:other-org", "organization distractor", completedAtUtc.AddHours(1));

        var otherPeriodSettlement = OperationMachineOverheadSettlement.CreateApplied(
            "org-read", "env-read", "WO-OTHER-PERIOD", "OP-OTHER-PERIOD", "WC-READ", 1,
            completedAtUtc.AddMonths(-1), "DEVICE-OTHER-PERIOD", 6 * TimeSpan.TicksPerHour,
            "single-device-active-minus-explicit-pause-v1", otherPeriodRate.Id, "2026-07", 1,
            "CNY", 30m, 10m, "evt-other-period", new string('d', 64));
        var otherPeriodState = OperationMachineOverheadSettlementState.Open(
            "org-read", "env-read", "OP-OTHER-PERIOD");
        otherPeriodState.ApplySettlement(1);
        var otherPeriodCost = WorkOrderCost.Open("org-read", "env-read", "WO-OTHER-PERIOD", "FG-OTHER-PERIOD");
        otherPeriodCost.RecordMachineOverhead(otherPeriodSettlement);
        var otherPeriodReconciliation = WorkCenterMachineOverheadReconciliation.Record(
            "org-read", "env-read", "WC-READ", "2026-07", otherPeriodRate.Id, 1, "CNY",
            180m, 60m, 6 * TimeSpan.TicksPerHour, 180m, 60m, 240m,
            0, AbnormalDowntimeDisposition.None, 1, "user:accountant",
            "ledger:other-period", "period distractor", completedAtUtc.AddHours(1));

        var otherWorkOrderSettlement = OperationMachineOverheadSettlement.CreateApplied(
            "org-read", "env-read", "WO-OTHER", "OP-OTHER-WO", "WC-OTHER-WO", 1,
            completedAtUtc, "DEVICE-OTHER-WO", 5 * TimeSpan.TicksPerHour,
            "single-device-active-minus-explicit-pause-v1", otherWorkOrderRate.Id, "2026-08", 1,
            "CNY", 30m, 10m, "evt-other-wo", new string('e', 64));
        var otherWorkOrderState = OperationMachineOverheadSettlementState.Open(
            "org-read", "env-read", "OP-OTHER-WO");
        otherWorkOrderState.ApplySettlement(1);
        var otherWorkOrderCost = WorkOrderCost.Open("org-read", "env-read", "WO-OTHER", "FG-OTHER-WO");
        otherWorkOrderCost.RecordMachineOverhead(otherWorkOrderSettlement);
        var otherWorkOrderReconciliation = WorkCenterMachineOverheadReconciliation.Record(
            "org-read", "env-read", "WC-OTHER-WO", "2026-08", otherWorkOrderRate.Id, 1, "CNY",
            150m, 50m, 5 * TimeSpan.TicksPerHour, 150m, 50m, 200m,
            0, AbnormalDowntimeDisposition.None, 1, "user:accountant",
            "ledger:other-work-order", "work order distractor", completedAtUtc.AddHours(1));
        db.AddRange(settlement, state, cost, reconciliation,
            otherSettlement, otherState, otherCost, otherEnvironmentReconciliation,
            otherOrganizationSettlement, otherOrganizationState, otherOrganizationCost,
            otherOrganizationReconciliation, otherPeriodSettlement, otherPeriodState,
            otherPeriodCost, otherPeriodReconciliation, otherWorkOrderSettlement,
            otherWorkOrderState, otherWorkOrderCost, otherWorkOrderReconciliation);
        await db.SaveChangesAsync();

        var workOrder = await new GetWorkOrderCostVarianceQueryHandler(db).Handle(
            new("org-read", "env-read", "WO-SAME"), CancellationToken.None);
        Assert.Equal(MachineOverheadReadStatus.Available, workOrder.MachineCostStatus);
        Assert.Equal(2m, workOrder.ActualMachineHours);
        Assert.Equal(60m, workOrder.AppliedFixedMachineOverhead);
        Assert.Equal(20m, workOrder.AppliedVariableMachineOverhead);
        Assert.Equal(80m, workOrder.AppliedMachineOverheadTotal);
        var operation = Assert.Single(workOrder.MachineOverheadOperations);
        Assert.Equal("OP-READ", operation.OperationTaskId);
        Assert.Equal("WC-READ", operation.WorkCenterId);
        Assert.Equal("DEVICE-READ", operation.DeviceAssetId);
        Assert.Equal("single-device-active-minus-explicit-pause-v1", operation.MachineTimeBasisCode);
        Assert.Equal("evt-read", operation.SourceEventId);
        Assert.Equal("CNY", workOrder.MachineCurrencyCode);

        var periodRead = await new ListWorkCenterMachineOverheadReconciliationsQueryHandler(db).Handle(
            new("org-read", "env-read", "2026-08", "WC-READ"), CancellationToken.None);
        Assert.Equal("open", periodRead.AccountingPeriodStatus);
        Assert.Equal(MachineOverheadReadStatus.Available, periodRead.ReconciliationStatus);
        var item = Assert.Single(periodRead.Items);
        Assert.Equal(reconciliation.Id.ToString(), item.Id);
        Assert.NotEqual(otherEnvironmentReconciliation.Id.ToString(), item.Id);
        Assert.Equal(100m, item.ActualFixedOverheadAmount);
        Assert.Equal(60m, item.AppliedFixedAmount);
        Assert.Equal(40m, item.UnderOverAppliedFixedAmount);
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_closed_period_stays_replayable_then_reopen_posts_machine_overhead_exactly_once()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-31T15:00:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var settled = MachineSettled(
            "evt-machine-closed", "org-machine-closed", "env-machine-closed",
            "WO-MACHINE-CLOSED", "OP-MACHINE-CLOSED", "WC-MACHINE-CLOSED",
            completedAtUtc, TimeSpan.TicksPerHour);
        var voided = MachineVoided("evt-machine-closed-void", settled, completedAtUtc.AddHours(2));

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            var period = AccountingPeriod.Open(
                "org-machine-closed", "env-machine-closed", "2026-08",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
            period.Close("auditor:test", "month end close");
            setupDb.AccountingPeriods.Add(period);
            setupDb.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
                "org-machine-closed", "env-machine-closed", "WC-MACHINE-CLOSED", "2026-08",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "approved machine overhead rate", completedAtUtc.AddDays(-30)));
            await setupDb.SaveChangesAsync();
        }

        await using (var closedDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(closedDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }

        await using (var closedAssertDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            Assert.Empty(await closedAssertDb.ProcessedIntegrationEvents.ToListAsync());
            Assert.Empty(await closedAssertDb.OperationMachineOverheadSettlements.ToListAsync());
            Assert.Empty(await closedAssertDb.OperationMachineOverheadSettlementStates.ToListAsync());
            Assert.Empty(await closedAssertDb.WorkOrderCosts.ToListAsync());
            Assert.Empty(await closedAssertDb.Set<WorkOrderCostDetail>().ToListAsync());
        }
        Assert.Equal("closed-accounting-period", Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None)).FailureCode);

        await using (var reopenDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            var period = await reopenDb.AccountingPeriods.SingleAsync();
            period.Reopen("auditor:test", "approved late machine settlement");
            await reopenDb.SaveChangesAsync();
        }

        await using (var replayDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(replayDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }
        await using (var duplicateDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(duplicateDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }

        await using (var assertDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            Assert.Single(await assertDb.ProcessedIntegrationEvents.Where(x => x.EventId == settled.EventId).ToListAsync());
            var snapshot = await assertDb.OperationMachineOverheadSettlements.SingleAsync();
            Assert.Equal("2026-08", snapshot.AccountingPeriodCode);
            Assert.Equal(1, snapshot.RateRevision);
            Assert.Equal(40m, snapshot.Amount);
            Assert.Equal(1, (await assertDb.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);
            var cost = await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
            Assert.Equal(40m, cost.MachineOverheadCost);
            Assert.Single(cost.Details, x => x.MachineOverheadBasis == MachineOverheadCostBasis.ActualOperation);
        }

        await using (var closeAfterSettlementDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            var cost = await closeAfterSettlementDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
            cost.RecordUncostedReport("RPT-MACHINE-CLOSED", false, completedAtUtc.AddMinutes(10));
            cost.Complete(10m, 1, 0, completedAtUtc.AddMinutes(20));
            cost.Capitalize("MOVE-MACHINE-CLOSED", 10m, 4m, completedAtUtc.AddMinutes(30));
            cost.RecordWipClearance(40m);
            (await closeAfterSettlementDb.AccountingPeriods.SingleAsync())
                .Close("auditor:test", "close after machine settlement");
            await closeAfterSettlementDb.SaveChangesAsync();
        }

        await using (var closedVoidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(closedVoidDb, deadLetters).HandleAsync(voided, CancellationToken.None);
        }

        await using (var closedVoidAssertDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            Assert.Empty(await closedVoidAssertDb.ProcessedIntegrationEvents.Where(x => x.EventId == voided.EventId).ToListAsync());
            Assert.Empty(await closedVoidAssertDb.OperationMachineOverheadSettlementVoids.ToListAsync());
            Assert.Equal(1, (await closedVoidAssertDb.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);
            var cost = await closedVoidAssertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
            Assert.Equal(40m, cost.MachineOverheadCost);
            Assert.Equal(40m, cost.WipClearedCost);
            Assert.Empty(await closedVoidAssertDb.JournalVouchers.ToListAsync());
        }
        Assert.Equal("closed-accounting-period", Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None)).FailureCode);

        await using (var reopenForVoidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            (await reopenForVoidDb.AccountingPeriods.SingleAsync())
                .Reopen("auditor:test", "approved late machine void");
            await reopenForVoidDb.SaveChangesAsync();
        }
        await using (var replayVoidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(replayVoidDb, deadLetters).HandleAsync(voided, CancellationToken.None);
        }
        await using (var duplicateVoidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(duplicateVoidDb, deadLetters).HandleAsync(voided, CancellationToken.None);
        }

        await using (var finalAssertDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            Assert.Single(await finalAssertDb.ProcessedIntegrationEvents.Where(x => x.EventId == voided.EventId).ToListAsync());
            Assert.Equal(-40m, (await finalAssertDb.OperationMachineOverheadSettlementVoids.SingleAsync()).Amount);
            Assert.Null((await finalAssertDb.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);
            var finalCost = await finalAssertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
            Assert.Equal(0m, finalCost.MachineOverheadCost);
            Assert.Equal(0m, finalCost.WipClearedCost);
            Assert.Single(finalCost.Details, x => x.MachineOverheadBasis == MachineOverheadCostBasis.ActualOperationVoid);
            var voucher = await finalAssertDb.JournalVouchers.Include(x => x.Lines).SingleAsync();
            Assert.Equal(40m, voucher.Lines.Sum(x => x.DebitAmount));
            Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount));
        }

        await using (var closeAfterVoidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            (await closeAfterVoidDb.AccountingPeriods.SingleAsync())
                .Close("auditor:test", "close after machine void");
            await closeAfterVoidDb.SaveChangesAsync();
        }
        await using (var duplicateAfterCloseDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(duplicateAfterCloseDb, deadLetters).HandleAsync(voided, CancellationToken.None);
        }

        var conflictingVoid = voided with
        {
            EventId = "evt-machine-closed-void-conflict",
            Payload = voided.Payload with { VoidedAtUtc = voided.Payload.VoidedAtUtc.AddMinutes(1) },
        };
        await using (var conflictAfterCloseDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(conflictAfterCloseDb, deadLetters).HandleAsync(conflictingVoid, CancellationToken.None);
        }

        await using var idempotencyAssertDb = new ApplicationDbContext(options, new NoopMediator());
        Assert.Single(await idempotencyAssertDb.ProcessedIntegrationEvents.Where(x => x.EventId == voided.EventId).ToListAsync());
        Assert.Empty(await idempotencyAssertDb.ProcessedIntegrationEvents.Where(x => x.EventId == conflictingVoid.EventId).ToListAsync());
        Assert.Single(await idempotencyAssertDb.OperationMachineOverheadSettlementVoids.ToListAsync());
        Assert.Null((await idempotencyAssertDb.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);
        var idempotentCost = await idempotencyAssertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(0m, idempotentCost.MachineOverheadCost);
        Assert.Equal(0m, idempotentCost.WipClearedCost);
        Assert.Single(idempotentCost.Details, x => x.MachineOverheadBasis == MachineOverheadCostBasis.ActualOperationVoid);
        Assert.Single(await idempotencyAssertDb.JournalVouchers.ToListAsync());
        Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None), x => x.FailureCode == "closed-accounting-period");
        Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None), x => x.FailureCode == "conflicting-operation-machine-overhead-settlement");
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_priced_labor_then_zero_not_applicable_machine_settle_and_void_do_not_freeze_machine_currency()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-31T15:00:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var settled = MachineSettled(
            "evt-machine-na-zero", "org-machine-currency", "env-machine-currency",
            "WO-LABOR-FIRST", "OP-LABOR-FIRST", "WC-NOT-APPLICABLE",
            completedAtUtc, null, MesMachineTimeFactStatus.NotApplicable);

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            setupDb.AccountingPeriods.Add(AccountingPeriod.Open(
                "org-machine-currency", "env-machine-currency", "2026-08",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
            setupDb.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineNotApplicable(
                "org-machine-currency", "env-machine-currency", "WC-NOT-APPLICABLE", "2026-08",
                "CNY", 1, "system:test", "no machine overhead", completedAtUtc.AddDays(-30)));
            var cost = WorkOrderCost.Open(
                "org-machine-currency", "env-machine-currency", "WO-LABOR-FIRST", "SKU-001");
            cost.RecordLabor("RPT-USD-FIRST", "WC-LABOR", 1m, 80m, "USD", false, completedAtUtc.AddMinutes(-10));
            setupDb.WorkOrderCosts.Add(cost);
            await setupDb.SaveChangesAsync();
        }

        await using (var settlementDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(settlementDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }
        await using (var voidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(voidDb, deadLetters).HandleAsync(
                MachineVoided("evt-machine-na-zero-void", settled, completedAtUtc.AddHours(1)),
                CancellationToken.None);
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var snapshot = await assertDb.OperationMachineOverheadSettlements.SingleAsync();
        var reversal = await assertDb.OperationMachineOverheadSettlementVoids.SingleAsync();
        var persisted = await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(0m, snapshot.Amount);
        Assert.Equal(0m, reversal.Amount);
        Assert.Equal("USD", persisted.LaborCurrencyCode);
        Assert.Null(persisted.MachineOverheadCurrencyCode);
        Assert.Equal(80m, persisted.TotalAccumulatedCost);
        Assert.Empty(await deadLetters.ListAsync(
            MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Empty(await deadLetters.ListAsync(
            MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_zero_available_machine_settle_and_void_do_not_poison_later_priced_labor_currency()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-31T15:00:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var settled = MachineSettled(
            "evt-machine-zero-first", "org-machine-zero-first", "env-machine-zero-first",
            "WO-MACHINE-FIRST", "OP-MACHINE-FIRST", "WC-MACHINE-FIRST",
            completedAtUtc, 0);

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            setupDb.AccountingPeriods.Add(AccountingPeriod.Open(
                "org-machine-zero-first", "env-machine-zero-first", "2026-08",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
            setupDb.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
                "org-machine-zero-first", "env-machine-zero-first", "WC-MACHINE-FIRST", "2026-08",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "approved machine overhead rate", completedAtUtc.AddDays(-30)));
            await setupDb.SaveChangesAsync();
        }

        await using (var settlementDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(settlementDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }
        await using (var voidDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineVoidConsumer(voidDb, deadLetters).HandleAsync(
                MachineVoided("evt-machine-zero-first-void", settled, completedAtUtc.AddHours(1)),
                CancellationToken.None);
        }
        await using (var laborDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            var cost = await laborDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
            cost.RecordLabor("RPT-USD-LATER", "WC-LABOR", 1m, 80m, "USD", false, completedAtUtc.AddHours(2));
            await laborDb.SaveChangesAsync();
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var snapshot = await assertDb.OperationMachineOverheadSettlements.SingleAsync();
        var reversal = await assertDb.OperationMachineOverheadSettlementVoids.SingleAsync();
        var persisted = await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(0m, snapshot.Amount);
        Assert.Equal(0m, reversal.Amount);
        Assert.Equal("USD", persisted.LaborCurrencyCode);
        Assert.Null(persisted.MachineOverheadCurrencyCode);
        Assert.Equal(80m, persisted.TotalAccumulatedCost);
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_nonzero_machine_overhead_still_fails_closed_for_priced_labor_in_another_currency()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        var completedAtUtc = DateTimeOffset.Parse("2026-08-31T15:00:00Z");
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await setupDb.Database.MigrateAsync();
            setupDb.AccountingPeriods.Add(AccountingPeriod.Open(
                "org-machine-priced", "env-machine-priced", "2026-08",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
            setupDb.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
                "org-machine-priced", "env-machine-priced", "WC-MACHINE-PRICED", "2026-08",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "approved machine overhead rate", completedAtUtc.AddDays(-30)));
            var cost = WorkOrderCost.Open(
                "org-machine-priced", "env-machine-priced", "WO-MACHINE-PRICED", "SKU-001");
            cost.RecordLabor("RPT-USD-PRICED", "WC-LABOR", 1m, 80m, "USD", false, completedAtUtc.AddMinutes(-10));
            setupDb.WorkOrderCosts.Add(cost);
            await setupDb.SaveChangesAsync();
        }

        var settled = MachineSettled(
            "evt-machine-priced", "org-machine-priced", "env-machine-priced",
            "WO-MACHINE-PRICED", "OP-MACHINE-PRICED", "WC-MACHINE-PRICED",
            completedAtUtc, TimeSpan.TicksPerHour);
        await using (var settlementDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            await MachineSettlementConsumer(settlementDb, deadLetters).HandleAsync(settled, CancellationToken.None);
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        Assert.Empty(await assertDb.OperationMachineOverheadSettlements.ToListAsync());
        Assert.Empty(await assertDb.ProcessedIntegrationEvents.Where(x => x.EventId == settled.EventId).ToListAsync());
        Assert.Equal(80m, (await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync()).TotalAccumulatedCost);
        Assert.Equal("incompatible-work-order-machine-overhead-currency", Assert.Single(await deadLetters.ListAsync(
            MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None)).FailureCode);
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_concurrent_report_and_actual_settlement_leave_only_actual_labor_active()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var applicationName = $"erp-actual-labor-{Guid.CreateVersion7():N}";
        var connectionString = new NpgsqlConnectionStringBuilder(ErpPostgresLaneDatabase.ConnectionString)
        {
            ApplicationName = applicationName,
        }.ConnectionString;
        var options = ErpPostgresLaneDatabase.CreateOptions(connectionString);

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(setupDb);
            await setupDb.Database.MigrateAsync();
            setupDb.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
                "org-concurrent", "env-concurrent", "WC-CONCURRENT", 80m, "CNY",
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), null, 1,
                "system:test", "approved standard labor rate", DateTimeOffset.UtcNow));
            setupDb.AccountingPeriods.Add(AccountingPeriod.Open(
                "org-concurrent", "env-concurrent", "2026-08",
                new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
            setupDb.WorkCenterMachineOverheadRates.Add(WorkCenterMachineOverheadRate.DefineApplicable(
                "org-concurrent", "env-concurrent", "WC-CONCURRENT", "2026-08",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "approved machine overhead rate", DateTimeOffset.UtcNow));
            await setupDb.SaveChangesAsync();
        }

        await using var gateDb = new ApplicationDbContext(options, new NoopMediator());
        await using var gateTransaction = await gateDb.Database.BeginTransactionAsync();
        await new PostgreSqlWorkOrderCostMutationLock(gateDb)
            .AcquireAsync("org-concurrent", "env-concurrent", "WO-CONCURRENT", CancellationToken.None);

        await using var reportDb = new ApplicationDbContext(options, new NoopMediator());
        await using var settlementDb = new ApplicationDbContext(options, new NoopMediator());
        await using var machineDb = new ApplicationDbContext(options, new NoopMediator());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var reportedAtUtc = new DateTimeOffset(2026, 8, 31, 15, 40, 0, TimeSpan.Zero);
        var completedAtUtc = reportedAtUtc.AddMinutes(10);
        var report = new ProductionReportRecordedIntegrationEvent(
            "evt-report-concurrent", MesIntegrationEventTypes.ProductionReportRecorded, 1, reportedAtUtc,
            MesIntegrationEventSources.BusinessMes, "RPT-CONCURRENT", "WO-CONCURRENT",
            "org-concurrent", "env-concurrent", "operator:test", "report:RPT-CONCURRENT",
            new ProductionReportRecordedPayload(
                "RPT-CONCURRENT", "WO-CONCURRENT", "OP-CONCURRENT", "WC-CONCURRENT", null,
                10m, 0m, 0m, "ea", 5.0000004m, reportedAtUtc, false, MaterialMovementCount: 0));
        var settled = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-settled-concurrent", MesIntegrationEventTypes.OperationActualTimeSettled, 1,
            completedAtUtc.AddMinutes(1), MesIntegrationEventSources.BusinessMes,
            "correlation-concurrent", "causation-concurrent", "org-concurrent", "env-concurrent",
            "operator:test", "actual-time:OP-CONCURRENT:1:settled",
            new OperationActualTimeSettledPayload(
                "WO-CONCURRENT", "OP-CONCURRENT", "WC-CONCURRENT", 1, completedAtUtc,
                2 * TimeSpan.TicksPerHour, 2 * TimeSpan.TicksPerHour, ["RPT-CONCURRENT"]));
        var machineSettled = new MesOperationActualTimeSettledV2IntegrationEvent(
            "evt-machine-concurrent", MesIntegrationEventTypes.OperationActualTimeSettled,
            MesIntegrationEventVersions.V2, completedAtUtc.AddMinutes(1), MesIntegrationEventSources.BusinessMes,
            "correlation-concurrent", "causation-concurrent", "org-concurrent", "env-concurrent",
            "operator:test", "actual-time:OP-CONCURRENT:1:settled:v2",
            new OperationActualTimeSettledV2Payload(
                "WO-CONCURRENT", "OP-CONCURRENT", "WC-CONCURRENT", 1, completedAtUtc,
                2 * TimeSpan.TicksPerHour, 2 * TimeSpan.TicksPerHour, ["RPT-CONCURRENT"],
                "DEVICE-CONCURRENT", MesMachineTimeFactStatus.Available,
                2 * TimeSpan.TicksPerHour,
                MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1));

        var reportTask = new ProductionReportRecordedIntegrationEventHandlerForAccumulateLaborCost(
                reportDb, deadLetters, reportDb, new PostgreSqlWorkOrderCostMutationLock(reportDb), ErpTestCoding.For(reportDb))
            .HandleAsync(report, CancellationToken.None);
        var settlementTask = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                settlementDb, settlementDb, new PostgreSqlWorkOrderCostMutationLock(settlementDb),
                new OperationLaborSettlementOrchestrator(settlementDb, deadLetters, ErpTestCoding.For(settlementDb)))
            .HandleAsync(settled, CancellationToken.None);
        var machineTask = new MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead(
                machineDb, machineDb, new PostgreSqlWorkOrderCostMutationLock(machineDb),
                new OperationMachineOverheadSettlementOrchestrator(machineDb, deadLetters, new PostgreSqlErpAdvisoryLockAllocator(machineDb), ErpTestCoding.For(machineDb)))
            .HandleAsync(machineSettled, CancellationToken.None);
        await WaitForAdvisoryLockWaitersAsync(connectionString, applicationName, expectedCount: 3);
        Assert.False(reportTask.IsCompleted);
        Assert.False(settlementTask.IsCompleted);
        Assert.False(machineTask.IsCompleted);

        await gateTransaction.CommitAsync();
        await Task.WhenAll(reportTask, settlementTask, machineTask).WaitAsync(TimeSpan.FromSeconds(10));

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var cost = await assertDb.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(160m, cost.LaborCost);
        Assert.Equal(0m, cost.Details
            .Where(x => x.LaborBasis is LaborCostBasis.TheoreticalReport or LaborCostBasis.TheoreticalReportReplacement)
            .Sum(x => x.Amount));
        Assert.InRange(
            cost.Details.Count(x => x.LaborBasis == LaborCostBasis.TheoreticalReportReplacement),
            0,
            1);
        Assert.Single(cost.Details, x => x.LaborBasis == LaborCostBasis.ActualOperation);
        Assert.Single(await assertDb.OperationLaborSettlements.ToListAsync());
        Assert.Single(await assertDb.OperationLaborCoveredReports.ToListAsync());
        var reportSnapshot = Assert.Single(await assertDb.OperationLaborReportSnapshots.AsNoTracking().ToListAsync());
        Assert.Equal(10m, reportSnapshot.GoodQuantity);
        Assert.Equal(5m, reportSnapshot.TheoreticalRatePerHour);
        Assert.False(reportSnapshot.HasValidNumericScale);
        Assert.Equal(80m, cost.MachineOverheadCost);
        Assert.Single(cost.Details, x => x.MachineOverheadBasis == MachineOverheadCostBasis.ActualOperation);
        Assert.Single(await assertDb.OperationMachineOverheadSettlements.ToListAsync());
        Assert.Equal(1, (await assertDb.OperationMachineOverheadSettlementStates.SingleAsync()).ActiveRevision);

        var stageRead = await new GetWorkOrderCostVarianceQueryHandler(assertDb).Handle(
            new GetWorkOrderCostVarianceQuery("org-concurrent", "env-concurrent", "WO-CONCURRENT"),
            CancellationToken.None);
        Assert.Equal("unavailable", stageRead.LaborVarianceStatus);
        Assert.Equal("work_order_not_completed", stageRead.UnavailableReason);
        Assert.Null(stageRead.StandardLaborHours);
        Assert.Null(stageRead.LaborEfficiencyVarianceAmount);
        Assert.Null(stageRead.CapitalizationVarianceAmount);
        Assert.Equal("unavailable", Assert.Single(stageRead.Operations).Status);

        cost.Complete(10m, 1, 0, completedAtUtc.AddMinutes(2));
        await assertDb.SaveChangesAsync();
        assertDb.ChangeTracker.Clear();

        var read = await new GetWorkOrderCostVarianceQueryHandler(assertDb).Handle(
            new GetWorkOrderCostVarianceQuery("org-concurrent", "env-concurrent", "WO-CONCURRENT"),
            CancellationToken.None);
        Assert.Equal("unavailable", read.LaborVarianceStatus);
        Assert.Equal("numeric_scale_out_of_range", read.UnavailableReason);
        Assert.Null(read.StandardLaborHours);
        Assert.Equal(2.000000m, read.ActualLaborHours);
        Assert.Null(read.LaborEfficiencyVarianceAmount);
        Assert.Equal(2.000000m, read.ActualMachineHours);
        Assert.Equal(MachineOverheadReadStatus.Available, read.MachineCostStatus);
        Assert.Null(read.MachineCostUnavailableReason);
        Assert.Equal(60m, read.AppliedFixedMachineOverhead);
        Assert.Equal(20m, read.AppliedVariableMachineOverhead);
        Assert.Equal(80m, read.AppliedMachineOverheadTotal);

        var governedRateId = await assertDb.WorkCenterCostRates.Select(x => x.Id).SingleAsync();
        var oldRevision = OperationLaborSettlement.Create(
            "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", "WC-REVISION", 1,
            completedAtUtc, TimeSpan.TicksPerHour, governedRateId,
            1, "CNY", 1m, "evt-revision-old", "hash-revision-old");
        var activeRevision = OperationLaborSettlement.Create(
            "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", "WC-REVISION", 2,
            completedAtUtc, 2 * TimeSpan.TicksPerHour, governedRateId,
            2, "CNY", 1m, "evt-revision-active", "hash-revision-active");
        var revisionState = OperationLaborSettlementState.Open(
            "org-concurrent", "env-concurrent", "OP-REVISION");
        revisionState.ApplySettlement(1);
        revisionState.ApplySettlement(2);
        var roundingSettlement = OperationLaborSettlement.Create(
            "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-ROUND", "WC-ROUND", 1,
            completedAtUtc, TimeSpan.TicksPerHour, governedRateId,
            1, "CNY", 1m, "evt-round", "hash-round");
        var roundingState = OperationLaborSettlementState.Open(
            "org-concurrent", "env-concurrent", "OP-ROUND");
        roundingState.ApplySettlement(1);
        assertDb.AddRange(
            oldRevision,
            activeRevision,
            revisionState,
            OperationLaborCoveredReport.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", 1, "RPT-REVISION-OLD"),
            OperationLaborCoveredReport.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", 2, "RPT-REVISION-ACTIVE"),
            OperationLaborReportSnapshot.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", "WC-REVISION", "RPT-REVISION-OLD",
                100m, 0m, 0m, "ea", 2m, reportedAtUtc.AddMinutes(1), false, null, "evt-report-revision-old"),
            OperationLaborReportSnapshot.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-REVISION", "WC-REVISION", "RPT-REVISION-ACTIVE",
                4m, 0m, 0m, "ea", 2m, reportedAtUtc.AddMinutes(2), false, null, "evt-report-revision-active"),
            roundingSettlement,
            roundingState,
            OperationLaborCoveredReport.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-ROUND", 1, "RPT-ROUND"),
            OperationLaborReportSnapshot.Create(
                "org-concurrent", "env-concurrent", "WO-CONCURRENT", "OP-ROUND", "WC-ROUND", "RPT-ROUND",
                2.000001m, 0m, 0m, "ea", 2m, reportedAtUtc.AddMinutes(3), false, null, "evt-report-round"));
        await assertDb.SaveChangesAsync();
        assertDb.ChangeTracker.Clear();

        var vectorRead = await new GetWorkOrderCostVarianceQueryHandler(assertDb).Handle(
            new GetWorkOrderCostVarianceQuery("org-concurrent", "env-concurrent", "WO-CONCURRENT"),
            CancellationToken.None);
        Assert.Equal(3, vectorRead.TotalOperations);
        var operations = vectorRead.Operations.ToDictionary(x => x.OperationTaskId, StringComparer.Ordinal);
        Assert.Equal(2, operations["OP-REVISION"].SettlementRevision);
        Assert.Equal(new[] { "RPT-REVISION-ACTIVE" },
            operations["OP-REVISION"].CoveredReports.Select(x => x.ReportNo));
        Assert.Equal(1.000001m, operations["OP-ROUND"].StandardLaborHours);
        Assert.Equal(-0.000001m, operations["OP-ROUND"].LaborEfficiencyVarianceHours);
        Assert.Equal(1.000001m, operations["OP-ROUND"].StandardLaborCost);
        Assert.Equal(-0.000001m, operations["OP-ROUND"].LaborEfficiencyVarianceAmount);

        var secondPage = await new GetWorkOrderCostVarianceQueryHandler(assertDb).Handle(
            new GetWorkOrderCostVarianceQuery("org-concurrent", "env-concurrent", "WO-CONCURRENT", 2, 2),
            CancellationToken.None);
        Assert.Equal(3, secondPage.TotalOperations);
        Assert.Equal(2, secondPage.PageNumber);
        Assert.Equal(2, secondPage.PageSize);
        Assert.Equal("OP-ROUND", Assert.Single(secondPage.Operations).OperationTaskId);

        var snapshotIndexes = await assertDb.Database.SqlQueryRaw<string>("""
            SELECT indexname AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'erp'
              AND tablename = 'operation_labor_report_snapshots'
              AND indexname IN (
                'ux_operation_labor_report_snapshots_scope_report',
                'ix_operation_labor_report_snapshots_work_order_operation')
            ORDER BY indexname
            """).ToListAsync();
        Assert.Equal([
            "ix_operation_labor_report_snapshots_work_order_operation",
            "ux_operation_labor_report_snapshots_scope_report",
        ], snapshotIndexes);

        assertDb.OperationLaborReportSnapshots.Remove(await assertDb.OperationLaborReportSnapshots
            .SingleAsync(x => x.ReportNo == "RPT-CONCURRENT"));
        await assertDb.SaveChangesAsync();
        assertDb.ChangeTracker.Clear();
        var historicalRead = await new GetWorkOrderCostVarianceQueryHandler(assertDb).Handle(
            new GetWorkOrderCostVarianceQuery("org-concurrent", "env-concurrent", "WO-CONCURRENT"),
            CancellationToken.None);
        Assert.Equal("unavailable", historicalRead.LaborVarianceStatus);
        Assert.Equal("missing_report_snapshot", historicalRead.UnavailableReason);
        Assert.Equal(5.000000m, historicalRead.ActualLaborHours);
        Assert.Null(historicalRead.StandardLaborHours);
    }

    [ErpCostPostgresFact(Timeout = 30_000)]
    public async Task PostgreSQL_second_concurrent_rate_command_blocks_then_observes_committed_revision()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var applicationName = $"erp-rate-concurrency-{Guid.CreateVersion7():N}";
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(ErpPostgresLaneDatabase.ConnectionString)
        {
            ApplicationName = applicationName,
        };
        var connectionString = connectionStringBuilder.ConnectionString;
        var options = ErpPostgresLaneDatabase.CreateOptions(connectionString);

        await using (var setupDb = new ApplicationDbContext(options, new NoopMediator()))
        {
            ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(setupDb);
            await setupDb.Database.MigrateAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssembly(typeof(ConfigureWorkCenterCostRateCommand).Assembly)
            .AddUnitOfWorkBehaviors());
        services.AddErpPostgreSqlPersistence(connectionString);
        await using var provider = services.BuildServiceProvider();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.NotSame(firstDb, secondDb);

        await using var gateDb = new ApplicationDbContext(options, new NoopMediator());
        await using var gateTransaction = await gateDb.Database.BeginTransactionAsync();
        await new PostgreSqlErpAdvisoryLockAllocator(gateDb)
            .AcquireAsync(
                ErpAdvisoryLockDomain.WorkCenterLaborCostRate,
                "org-concurrent", "env-concurrent", "WC-CONCURRENT", CancellationToken.None);

        var effectiveFromUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var changedAtUtc = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero);
        var firstCommand = new ConfigureWorkCenterCostRateCommand(
            " org-concurrent ",
            "env-concurrent",
            " WC-CONCURRENT ",
            40m,
            "CNY",
            effectiveFromUtc,
            null,
            "user:first",
            "first concurrent rate",
            changedAtUtc);
        var secondCommand = new ConfigureWorkCenterCostRateCommand(
            "org-concurrent",
            " env-concurrent ",
            "WC-CONCURRENT",
            45m,
            "CNY",
            effectiveFromUtc,
            null,
            "user:second",
            "second concurrent rate",
            changedAtUtc.AddSeconds(1));

        var firstSend = firstScope.ServiceProvider.GetRequiredService<ISender>().Send(firstCommand);
        var secondSend = secondScope.ServiceProvider.GetRequiredService<ISender>().Send(secondCommand);
        var gateReleased = false;
        try
        {
            await WaitForAdvisoryLockWaitersAsync(connectionString, applicationName, expectedCount: 2);
            Assert.False(firstSend.IsCompleted);
            Assert.False(secondSend.IsCompleted);
            await gateTransaction.CommitAsync();
            gateReleased = true;

            var ids = await Task.WhenAll(firstSend, secondSend).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, ids.Distinct().Count());
        }
        finally
        {
            if (!gateReleased)
            {
                await gateTransaction.RollbackAsync();
            }

            try
            {
                await Task.WhenAll(firstSend, secondSend).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Preserve the primary assertion/command failure while still observing both tasks.
            }
        }

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var persisted = await assertDb.WorkCenterCostRates
            .Where(x => x.OrganizationId == "org-concurrent"
                && x.EnvironmentId == "env-concurrent"
                && x.WorkCenterId == "WC-CONCURRENT")
            .OrderBy(x => x.Revision)
            .ToListAsync();

        Assert.Collection(
            persisted,
            first => Assert.Equal(1, first.Revision),
            second => Assert.Equal(2, second.Revision));
        Assert.Equal([40m, 45m], persisted.Select(x => x.HourlyRate).OrderBy(x => x).ToArray());
    }

    [ErpCostPostgresFact]
    public async Task PostgreSQL_migration_backfills_legacy_rate_and_enforces_revision_indexes()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.OpenConnectionAsync();
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(ErpFacts.Schema);

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260720014936_AddDeliveryOrderConcurrencyToken");
        await using (var seed = new NpgsqlCommand($"""
            INSERT INTO {quotedSchema}.work_center_cost_rates
                (id, organization_id, environment_id, work_center_id, hourly_rate)
            VALUES
                (@id, 'org-legacy', 'env-legacy', 'WC-LEGACY', 37.5)
            """, (NpgsqlConnection)db.Database.GetDbConnection()))
        {
            seed.Parameters.AddWithValue("id", Guid.CreateVersion7());
            await seed.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var legacy = await db.WorkCenterCostRates.SingleAsync();
        Assert.Equal(1, legacy.Revision);
        Assert.Equal("CNY", legacy.CurrencyCode);
        Assert.Equal(DateTimeOffset.UnixEpoch, legacy.EffectiveFromUtc);
        Assert.Null(legacy.EffectiveToUtc);
        Assert.Equal("system:migration", legacy.ChangedBy);
        Assert.Equal("legacy cost-rate migration", legacy.Reason);
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 2, 54, 18, TimeSpan.Zero), legacy.ChangedAtUtc);

        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var indexCommand = new NpgsqlCommand("""
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'erp' AND tablename = 'work_center_cost_rates'
            """, (NpgsqlConnection)db.Database.GetDbConnection()))
        await using (var reader = await indexCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) indexes.Add(reader.GetString(0), reader.GetString(1));
        }

        Assert.Contains("ux_work_center_cost_rates_scope_revision", indexes.Keys);
        Assert.Contains("UNIQUE", indexes["ux_work_center_cost_rates_scope_revision"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix_work_center_cost_rates_effective_lookup", indexes.Keys);
        Assert.DoesNotContain("IX_work_center_cost_rates_organization_id_environment_id_work_~", indexes.Keys);

        await using (var metadataCommand = new NpgsqlCommand("""
            SELECT
                obj_description('erp.work_center_cost_rates'::regclass),
                col_description('erp.work_center_cost_rates'::regclass, (
                    SELECT attnum FROM pg_attribute
                    WHERE attrelid = 'erp.work_center_cost_rates'::regclass AND attname = 'hourly_rate')),
                col_description('erp.work_order_costs'::regclass, (
                    SELECT attnum FROM pg_attribute
                    WHERE attrelid = 'erp.work_order_costs'::regclass AND attname = 'labor_currency_code'))
            """, (NpgsqlConnection)db.Database.GetDbConnection()))
        await using (var metadata = await metadataCommand.ExecuteReaderAsync())
        {
            Assert.True(await metadata.ReadAsync());
            Assert.Equal("ERP append-only, effective-dated standard labor hourly-rate revision history by work center.", metadata.GetString(0));
            Assert.Equal("Positive standard labor hourly rate.", metadata.GetString(1));
            Assert.Equal("Frozen three-letter currency code shared by all priced labor on this work order; no implicit conversion is allowed.", metadata.GetString(2));
        }

        await using (var costDetailMetadataCommand = new NpgsqlCommand("""
            SELECT
                obj_description('erp.work_order_cost_details'::regclass),
                col_description('erp.work_order_cost_details'::regclass, (
                    SELECT attnum FROM pg_attribute
                    WHERE attrelid = 'erp.work_order_cost_details'::regclass AND attname = 'cost_type')),
                col_description('erp.work_order_cost_details'::regclass, (
                    SELECT attnum FROM pg_attribute
                    WHERE attrelid = 'erp.work_order_cost_details'::regclass AND attname = 'quantity')),
                col_description('erp.work_order_cost_details'::regclass, (
                    SELECT attnum FROM pg_attribute
                    WHERE attrelid = 'erp.work_order_cost_details'::regclass AND attname = 'rate'))
            """, (NpgsqlConnection)db.Database.GetDbConnection()))
        await using (var metadata = await costDetailMetadataCommand.ExecuteReaderAsync())
        {
            Assert.True(await metadata.ReadAsync());
            Assert.Equal("ERP auditable labor, material, or machine-overhead cost detail.", metadata.GetString(0));
            Assert.Equal("Labor, material, or machine-overhead cost type.", metadata.GetString(1));
            Assert.Equal("Labor or machine hours, or material quantity.", metadata.GetString(2));
            Assert.Equal("Labor or machine-overhead hourly rate, or moving-average material unit cost.", metadata.GetString(3));
        }

        db.WorkCenterCostRates.AddRange(
            WorkCenterCostRate.Define("org-legacy", "env-legacy", "WC-LEGACY", 40m, "CNY", DateTimeOffset.UnixEpoch, null, 2, "system:test", "first concurrent candidate", DateTimeOffset.UtcNow),
            WorkCenterCostRate.Define("org-legacy", "env-legacy", "WC-LEGACY", 41m, "CNY", DateTimeOffset.UnixEpoch, null, 2, "system:test", "second concurrent candidate", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [ErpCostPostgresFact]
    public async Task PostgreSQL_migration_enforces_gl_link_and_persists_reconciled_cost()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();

        db.GLAccounts.AddRange(
            GLAccount.Create("org-pg", "env-pg", "1405-WIP", "Work in process", GLAccountType.Asset),
            GLAccount.Create("org-pg", "env-pg", "1406-FINISHED-GOODS", "Finished goods", GLAccountType.Asset));
        db.JournalVouchers.Add(JournalVoucher.Post("org-pg", "env-pg", "JV-PG-001", new DateOnly(2026, 7, 11),
            [new JournalVoucherLineDraft("1406-FINISHED-GOODS", 160m, 0m, "capitalization"), new JournalVoucherLineDraft("1405-WIP", 0m, 160m, "clear WIP")],
            JournalVoucherSourceType.WorkOrderCapitalization, "MOVE-PG-FG"));
        var cost = WorkOrderCost.Open("org-pg", "env-pg", "WO-PG-001", "FG-PG-001");
        cost.RecordLabor("RPT-PG-001", "WC-PG", 2m, 50m, "CNY", false, DateTimeOffset.UtcNow);
        cost.RecordMaterial("MOVE-PG-RM", "RPT-PG-001", "RM-PG", 3m, 20m, DateTimeOffset.UtcNow);
        cost.Complete(8m, 1, 1, DateTimeOffset.UtcNow);
        cost.Capitalize("MOVE-PG-FG", 8m, 20m, DateTimeOffset.UtcNow);
        cost.RecordWipClearance(160m);
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var persisted = await db.WorkOrderCosts.Include(x => x.Details).SingleAsync();
        Assert.Equal(160m, persisted.TotalAccumulatedCost);
        Assert.Equal("CNY", persisted.LaborCurrencyCode);
        Assert.Equal(persisted.TotalAccumulatedCost, persisted.WipClearedCost);
        Assert.Equal(2, await db.JournalVouchers.SelectMany(x => x.Lines).CountAsync());
    }

    [ErpCostPostgresFact]
    public async Task PostgreSQL_fault_after_save_rolls_back_settlement_inbox_lineage_and_cost()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.MigrateAsync();
        db.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
            "org-rollback", "env-rollback", "WC-ROLLBACK", 80m, "CNY",
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1,
            "system:test", "rollback rate", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var failingUnitOfWork = new SaveThenFailUnitOfWork(db);
        var settled = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-rollback", MesIntegrationEventTypes.OperationActualTimeSettled, 1,
            DateTimeOffset.Parse("2026-08-31T16:00:00Z"), MesIntegrationEventSources.BusinessMes,
            "correlation-rollback", "causation-rollback", "org-rollback", "env-rollback",
            "operator:test", "actual-time:OP-ROLLBACK:1:settled",
            new OperationActualTimeSettledPayload(
                "WO-ROLLBACK", "OP-ROLLBACK", "WC-ROLLBACK", 1,
                DateTimeOffset.Parse("2026-08-31T15:50:00Z"),
                2 * TimeSpan.TicksPerHour, 2 * TimeSpan.TicksPerHour, ["RPT-ROLLBACK"]));
        var handler = new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
            db, failingUnitOfWork, new PostgreSqlWorkOrderCostMutationLock(db),
            new OperationLaborSettlementOrchestrator(db, deadLetters, ErpTestCoding.For(db)));

        await Assert.ThrowsAsync<InjectedSaveFailureException>(
            () => handler.HandleAsync(settled, CancellationToken.None));

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        Assert.Empty(await assertDb.OperationLaborSettlements.ToListAsync());
        Assert.Empty(await assertDb.OperationLaborSettlementStates.ToListAsync());
        Assert.Empty(await assertDb.OperationLaborCoveredReports.ToListAsync());
        Assert.Empty(await assertDb.WorkOrderCosts.ToListAsync());
        Assert.Empty(await assertDb.ProcessedIntegrationEvents.ToListAsync());
    }

    [ErpCostPostgresFact]
    public async Task PostgreSQL_capitalized_settlement_reads_back_balanced_voucher_and_unique_lineage_indexes()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.MigrateAsync();
        db.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
            "org-cap", "env-cap", "WC-CAP", 80m, "CNY",
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1,
            "system:test", "capitalization rate", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        var cost = WorkOrderCost.Open("org-cap", "env-cap", "WO-CAP", "FG-CAP");
        cost.RecordLabor("RPT-CAP", "WC-CAP", 2m, 80m, "CNY", false, DateTimeOffset.Parse("2026-08-31T15:40:00Z"));
        cost.Complete(10m, 1, 0, DateTimeOffset.Parse("2026-08-31T15:50:00Z"));
        cost.Capitalize("MOVE-CAP", 10m, 16m, DateTimeOffset.Parse("2026-08-31T15:51:00Z"));
        cost.RecordWipClearance(160m);
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var settled = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-cap", MesIntegrationEventTypes.OperationActualTimeSettled, 1,
            DateTimeOffset.Parse("2026-08-31T16:00:00Z"), MesIntegrationEventSources.BusinessMes,
            "correlation-cap", "causation-cap", "org-cap", "env-cap", "operator:test",
            "actual-time:OP-CAP:1:settled",
            new OperationActualTimeSettledPayload(
                "WO-CAP", "OP-CAP", "WC-CAP", 1, DateTimeOffset.Parse("2026-08-31T15:50:00Z"),
                90 * TimeSpan.TicksPerMinute, 90 * TimeSpan.TicksPerMinute, ["RPT-CAP"]));
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters, ErpTestCoding.For(db)))
            .HandleAsync(settled, CancellationToken.None);

        var mixedCost = WorkOrderCost.Open("org-cap", "env-cap", "WO-MIXED", "FG-MIXED");
        mixedCost.RecordLabor("RPT-MIXED", "WC-CAP", 1m, 80m, "USD", false,
            DateTimeOffset.Parse("2026-08-31T15:40:00Z"));
        db.WorkOrderCosts.Add(mixedCost);
        await db.SaveChangesAsync();
        var mixedSettlement = settled with
        {
            EventId = "evt-cap-mixed",
            IdempotencyKey = "actual-time:OP-MIXED:1:settled",
            Payload = settled.Payload with
            {
                WorkOrderId = "WO-MIXED",
                OperationTaskId = "OP-MIXED",
                CoveredProductionReportNos = ["RPT-MIXED"],
            },
        };
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters, ErpTestCoding.For(db)))
            .HandleAsync(mixedSettlement, CancellationToken.None);

        var readyButUnposted = WorkOrderCost.Open("org-cap", "env-cap", "WO-READY", "FG-READY");
        readyButUnposted.RecordLabor("RPT-READY", "WC-CAP", 2m, 80m, "CNY", false,
            DateTimeOffset.Parse("2026-08-31T15:40:00Z"));
        readyButUnposted.Complete(10m, 1, 0, DateTimeOffset.Parse("2026-08-31T15:50:00Z"));
        db.WorkOrderCosts.Add(readyButUnposted);
        await db.SaveChangesAsync();
        var prePostingSettlement = settled with
        {
            EventId = "evt-ready-settled",
            IdempotencyKey = "actual-time:OP-READY:1:settled",
            Payload = settled.Payload with
            {
                WorkOrderId = "WO-READY",
                OperationTaskId = "OP-READY",
                CoveredProductionReportNos = ["RPT-READY"],
            },
        };
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters, ErpTestCoding.For(db)))
            .HandleAsync(prePostingSettlement, CancellationToken.None);
        await new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters, ErpTestCoding.For(db)))
            .HandleAsync(new MesOperationActualTimeSettlementVoidedIntegrationEvent(
                "evt-ready-void", MesIntegrationEventTypes.OperationActualTimeSettlementVoided, 1,
                DateTimeOffset.Parse("2026-08-31T16:05:00Z"), MesIntegrationEventSources.BusinessMes,
                "correlation-ready", "causation-ready", "org-cap", "env-cap", "operator:test",
                "actual-time:OP-READY:1:voided",
                new OperationActualTimeSettlementVoidedPayload(
                    "WO-READY", "OP-READY", "WC-CAP", 1,
                    DateTimeOffset.Parse("2026-08-31T15:50:00Z"),
                    DateTimeOffset.Parse("2026-08-31T16:05:00Z"),
                    90 * TimeSpan.TicksPerMinute, 90 * TimeSpan.TicksPerMinute,
                    ["RPT-READY"])),
                CancellationToken.None);

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var persistedCost = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-CAP");
        var voucher = await assertDb.JournalVouchers.Include(x => x.Lines).SingleAsync();
        Assert.Equal(120m, persistedCost.LaborCost);
        Assert.Equal("CNY", persistedCost.LaborCurrencyCode);
        Assert.Equal(120m, persistedCost.WipClearedCost);
        Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount));
        Assert.Equal(40m, voucher.Lines.Sum(x => x.DebitAmount));
        var persistedMixedCost = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-MIXED");
        Assert.Equal("USD", persistedMixedCost.LaborCurrencyCode);
        Assert.Equal(80m, persistedMixedCost.LaborCost);
        Assert.DoesNotContain(await assertDb.OperationLaborSettlements.ToListAsync(),
            x => x.OperationTaskId == "OP-MIXED");
        Assert.DoesNotContain(await assertDb.OperationLaborSettlementStates.ToListAsync(),
            x => x.OperationTaskId == "OP-MIXED");
        Assert.DoesNotContain(await assertDb.ProcessedIntegrationEvents.ToListAsync(),
            x => x.EventId == "evt-cap-mixed");
        Assert.Equal("incompatible-work-order-labor-currency",
            Assert.Single(await deadLetters.ListAsync(
                MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None)).FailureCode);
        var persistedReady = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-READY");
        Assert.True(persistedReady.CapitalizationPublished);
        Assert.Equal(0m, persistedReady.CapitalizedQuantity);
        Assert.Equal(0m, persistedReady.LaborCost);
        Assert.Single(await assertDb.JournalVouchers.ToListAsync());

        var indexes = await assertDb.Database.SqlQueryRaw<string>("""
            SELECT indexname AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'erp'
              AND indexname IN (
                'ux_operation_labor_settlements_business_identity',
                'ux_operation_labor_settlement_voids_business_identity',
                'ux_operation_labor_covered_reports_report')
              AND indexdef ILIKE '%UNIQUE%'
            ORDER BY indexname
            """).ToListAsync();
        Assert.Equal(3, indexes.Count);
    }

    [ErpCostPostgresFact]
    public async Task PostgreSQL_partial_capitalization_settle_void_and_final_receipt_persist_balanced_vouchers()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        await db.Database.MigrateAsync();
        db.WorkCenterCostRates.Add(WorkCenterCostRate.Define(
            "org-partial", "env-partial", "WC-PARTIAL", 80m, "CNY",
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1,
            "system:test", "partial capitalization rate", DateTimeOffset.Parse("2026-08-01T00:00:00Z")));
        var cost = WorkOrderCost.Open("org-partial", "env-partial", "WO-PARTIAL", "FG-PARTIAL");
        cost.RecordLabor("RPT-PARTIAL", "WC-PARTIAL", 2m, 80m, "CNY", false,
            DateTimeOffset.Parse("2026-08-31T15:40:00Z"));
        cost.Complete(10m, 1, 0, DateTimeOffset.Parse("2026-08-31T15:50:00Z"));
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var receiptConsumer = new StockMovementPostedIntegrationEventHandlerForAccumulateMaterialCost(db, deadLetters, db, ErpTestCoding.For(db));
        await receiptConsumer.HandleAsync(PartialReceipt("evt-pg-partial", "MOVE-PG-PARTIAL", "FGR-PG-PARTIAL"), CancellationToken.None);

        var settled = new MesOperationActualTimeSettledIntegrationEvent(
            "evt-pg-partial-settle", MesIntegrationEventTypes.OperationActualTimeSettled, 1,
            DateTimeOffset.Parse("2026-08-31T16:00:00Z"), MesIntegrationEventSources.BusinessMes,
            "correlation-pg-partial", "causation-pg-partial", "org-partial", "env-partial",
            "operator:test", "actual-time:OP-PARTIAL:1:settled",
            new OperationActualTimeSettledPayload(
                "WO-PARTIAL", "OP-PARTIAL", "WC-PARTIAL", 1,
                DateTimeOffset.Parse("2026-08-31T15:50:00Z"),
                90 * TimeSpan.TicksPerMinute, 90 * TimeSpan.TicksPerMinute, ["RPT-PARTIAL"]));
        await new MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters, ErpTestCoding.For(db)))
            .HandleAsync(settled, CancellationToken.None);
        await new MesOperationActualTimeSettlementVoidedIntegrationEventHandlerForReverseLaborCost(
                db, db, new PostgreSqlWorkOrderCostMutationLock(db),
                new OperationLaborSettlementOrchestrator(db, deadLetters, ErpTestCoding.For(db)))
            .HandleAsync(new MesOperationActualTimeSettlementVoidedIntegrationEvent(
                "evt-pg-partial-void", MesIntegrationEventTypes.OperationActualTimeSettlementVoided, 1,
                DateTimeOffset.Parse("2026-08-31T16:05:00Z"), MesIntegrationEventSources.BusinessMes,
                "correlation-pg-partial", settled.EventId, "org-partial", "env-partial",
                "operator:test", "actual-time:OP-PARTIAL:1:voided",
                new OperationActualTimeSettlementVoidedPayload(
                    "WO-PARTIAL", "OP-PARTIAL", "WC-PARTIAL", 1,
                    DateTimeOffset.Parse("2026-08-31T15:50:00Z"),
                    DateTimeOffset.Parse("2026-08-31T16:05:00Z"),
                    90 * TimeSpan.TicksPerMinute, 90 * TimeSpan.TicksPerMinute, ["RPT-PARTIAL"])),
                CancellationToken.None);
        await receiptConsumer.HandleAsync(PartialReceipt("evt-pg-final", "MOVE-PG-FINAL", "FGR-PG-FINAL"), CancellationToken.None);

        await using var assertDb = new ApplicationDbContext(options, new NoopMediator());
        var persisted = await assertDb.WorkOrderCosts.Include(x => x.Details)
            .SingleAsync(x => x.WorkOrderId == "WO-PARTIAL");
        Assert.Equal(0m, persisted.LaborCost);
        Assert.Equal(10m, persisted.CapitalizedQuantity);
        Assert.Equal(0m, persisted.WipClearedCost);
        var vouchers = await assertDb.JournalVouchers.Include(x => x.Lines).ToListAsync();
        Assert.Equal(2, vouchers.Count);
        Assert.All(vouchers, voucher =>
            Assert.Equal(voucher.Lines.Sum(x => x.DebitAmount), voucher.Lines.Sum(x => x.CreditAmount)));
        var lines = vouchers.SelectMany(x => x.Lines).ToList();
        Assert.Equal(160m, lines.Where(x => x.AccountCode == "1406-FINISHED-GOODS").Sum(x => x.DebitAmount));
        Assert.Equal(80m, lines.Where(x => x.AccountCode == "1405-WIP").Sum(x => x.DebitAmount));
        Assert.Equal(80m, lines.Where(x => x.AccountCode == "1405-WIP").Sum(x => x.CreditAmount));
        var varianceLine = Assert.Single(lines, x => x.AccountCode == "5101-PRODUCTION-VARIANCE");
        Assert.Equal(0m, varianceLine.DebitAmount);
        Assert.Equal(160m, varianceLine.CreditAmount);
        Assert.Equal(
            persisted.WipClearedCost,
            lines.Where(x => x.AccountCode == "1405-WIP").Sum(x => x.CreditAmount - x.DebitAmount));
        Assert.Empty(await deadLetters.ListAsync(
            MesOperationActualTimeSettledIntegrationEventHandlerForAccumulateLaborCost.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));

        static StockMovementPostedIntegrationEvent PartialReceipt(string eventId, string movementId, string receiptId)
            => new(
                eventId, InventoryIntegrationEventTypes.StockMovementPosted, 1,
                DateTimeOffset.Parse("2026-08-31T15:55:00Z"), InventoryIntegrationEventSources.BusinessInventory,
                receiptId, receiptId, "org-partial", "env-partial", "inventory", movementId,
                new StockMovementPostedPayload(
                    movementId, "inbound", InventoryIntegrationEventSources.BusinessMes,
                    receiptId, "WO-PARTIAL", $"mes:finished-goods-receipt:{receiptId}",
                    "FG-PARTIAL", "ea", "finished-goods", "receiving", null, null,
                    "unrestricted", "organization", "org-partial", 5m,
                    DateTimeOffset.Parse("2026-08-31T15:55:00Z"), 16m, 80m));
    }

    [ErpCostPostgresFact]
    public async Task PostgreSQL_rejects_duplicate_settlement_void_and_covered_report_without_half_commit()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        var options = ErpPostgresLaneDatabase.CreateOptions();
        await using (var seed = new ApplicationDbContext(options, new NoopMediator()))
        {
            await seed.Database.MigrateAsync();
            var rate = WorkCenterCostRate.Define("org-unique", "env-unique", "WC-UNIQUE", 80m, "CNY", DateTimeOffset.Parse("2026-08-01T00:00:00Z"), null, 1, "system:test", "unique proof", DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
            seed.WorkCenterCostRates.Add(rate);
            var settlement = OperationLaborSettlement.Create("org-unique", "env-unique", "WO-UNIQUE", "OP-UNIQUE", "WC-UNIQUE", 1, DateTimeOffset.Parse("2026-08-31T15:00:00Z"), TimeSpan.TicksPerHour, rate.Id, 1, "CNY", 80m, "evt-unique", new string('a', 64));
            seed.OperationLaborSettlements.Add(settlement);
            seed.OperationLaborSettlementVoids.Add(OperationLaborSettlementVoid.Create(settlement, DateTimeOffset.Parse("2026-08-31T16:00:00Z"), "evt-void", new string('b', 64)));
            seed.OperationLaborCoveredReports.Add(OperationLaborCoveredReport.Create("org-unique", "env-unique", "WO-UNIQUE", "OP-UNIQUE", 1, "RPT-UNIQUE"));
            var machineRate = WorkCenterMachineOverheadRate.DefineApplicable(
                "org-unique", "env-unique", "WC-UNIQUE", "2026-08",
                30_000m, 10_000m, 1_000m, "CNY", 1,
                "system:test", "machine unique proof", DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
            seed.WorkCenterMachineOverheadRates.Add(machineRate);
            var machineSettlement = OperationMachineOverheadSettlement.CreateApplied(
                "org-unique", "env-unique", "WO-UNIQUE", "OP-UNIQUE", "WC-UNIQUE", 1,
                DateTimeOffset.Parse("2026-08-31T15:00:00Z"), "DEVICE-UNIQUE",
                TimeSpan.TicksPerHour, MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1,
                machineRate.Id, "2026-08", 1, "CNY", 30m, 10m,
                "evt-machine-unique", new string('e', 64));
            seed.OperationMachineOverheadSettlements.Add(machineSettlement);
            seed.OperationMachineOverheadSettlementVoids.Add(OperationMachineOverheadSettlementVoid.Create(
                machineSettlement, DateTimeOffset.Parse("2026-08-31T16:00:00Z"),
                "evt-machine-void", new string('f', 64)));
            await seed.SaveChangesAsync();
        }

        await AssertConstraintAsync(options, "ux_operation_labor_settlements_business_identity", db =>
        {
            var rateId = db.WorkCenterCostRates.Select(x => x.Id).Single();
            db.OperationLaborSettlements.Add(OperationLaborSettlement.Create("org-unique", "env-unique", "WO-DUP", "OP-UNIQUE", "WC-UNIQUE", 1, DateTimeOffset.Parse("2026-08-31T15:00:00Z"), TimeSpan.TicksPerHour, rateId, 1, "CNY", 80m, "evt-dup", new string('c', 64)));
        });
        await AssertConstraintAsync(options, "ux_operation_labor_settlement_voids_business_identity", db =>
        {
            var settlement = db.OperationLaborSettlements.Single();
            db.OperationLaborSettlementVoids.Add(OperationLaborSettlementVoid.Create(settlement, DateTimeOffset.Parse("2026-08-31T17:00:00Z"), "evt-void-dup", new string('d', 64)));
        });
        await AssertConstraintAsync(options, "ux_operation_labor_covered_reports_report", db =>
            db.OperationLaborCoveredReports.Add(OperationLaborCoveredReport.Create("org-unique", "env-unique", "WO-DUP", "OP-DUP", 2, "RPT-UNIQUE")));
        await AssertConstraintAsync(options, "ux_op_machine_overhead_settlements_identity", db =>
        {
            var rateId = db.WorkCenterMachineOverheadRates.Select(x => x.Id).Single();
            db.OperationMachineOverheadSettlements.Add(OperationMachineOverheadSettlement.CreateApplied(
                "org-unique", "env-unique", "WO-DUP", "OP-UNIQUE", "WC-UNIQUE", 1,
                DateTimeOffset.Parse("2026-08-31T15:00:00Z"), "DEVICE-DUP", TimeSpan.TicksPerHour,
                MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1,
                rateId, "2026-08", 1, "CNY", 30m, 10m, "evt-machine-dup", new string('1', 64)));
        });
        await AssertConstraintAsync(options, "ux_op_machine_overhead_settlement_voids_identity", db =>
        {
            var settlement = db.OperationMachineOverheadSettlements.Single();
            db.OperationMachineOverheadSettlementVoids.Add(OperationMachineOverheadSettlementVoid.Create(
                settlement, DateTimeOffset.Parse("2026-08-31T17:00:00Z"),
                "evt-machine-void-dup", new string('2', 64)));
        });

        await using var verify = new ApplicationDbContext(options, new NoopMediator());
        Assert.Equal(1, await verify.OperationLaborSettlements.CountAsync());
        Assert.Equal(1, await verify.OperationLaborSettlementVoids.CountAsync());
        Assert.Equal(1, await verify.OperationLaborCoveredReports.CountAsync());
        Assert.Equal(1, await verify.OperationMachineOverheadSettlements.CountAsync());
        Assert.Equal(1, await verify.OperationMachineOverheadSettlementVoids.CountAsync());
    }

    private static async Task AssertConstraintAsync(DbContextOptions<ApplicationDbContext> options, string constraintName, Action<ApplicationDbContext> arrange)
    {
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        arrange(db);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(constraintName, postgres.ConstraintName);
    }

    private static MesOperationActualTimeSettledV2IntegrationEventHandlerForAccumulateMachineOverhead MachineSettlementConsumer(
        ApplicationDbContext db,
        InMemoryIntegrationEventDeadLetterStore deadLetters)
        => new(
            db,
            db,
            new PostgreSqlWorkOrderCostMutationLock(db),
            new OperationMachineOverheadSettlementOrchestrator(db, deadLetters, new PostgreSqlErpAdvisoryLockAllocator(db), ErpTestCoding.For(db)));

    private static MesOperationActualTimeSettlementVoidedV2IntegrationEventHandlerForReverseMachineOverhead MachineVoidConsumer(
        ApplicationDbContext db,
        InMemoryIntegrationEventDeadLetterStore deadLetters)
        => new(
            db,
            db,
            new PostgreSqlWorkOrderCostMutationLock(db),
            new OperationMachineOverheadSettlementOrchestrator(db, deadLetters, new PostgreSqlErpAdvisoryLockAllocator(db), ErpTestCoding.For(db)));

    private static MesOperationActualTimeSettledV2IntegrationEvent MachineSettled(
        string eventId,
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string workCenterId,
        DateTimeOffset completedAtUtc,
        long? billableMachineTicks,
        MesMachineTimeFactStatus status = MesMachineTimeFactStatus.Available)
        => new(
            eventId,
            MesIntegrationEventTypes.OperationActualTimeSettled,
            MesIntegrationEventVersions.V2,
            completedAtUtc.AddMinutes(1),
            MesIntegrationEventSources.BusinessMes,
            $"correlation-{eventId}",
            $"causation-{eventId}",
            organizationId,
            environmentId,
            "operator:test",
            $"actual-time:{operationTaskId}:1:settled:v2",
            new OperationActualTimeSettledV2Payload(
                workOrderId,
                operationTaskId,
                workCenterId,
                1,
                completedAtUtc,
                TimeSpan.TicksPerHour,
                billableMachineTicks ?? 0,
                [],
                status == MesMachineTimeFactStatus.Available ? $"DEVICE-{operationTaskId}" : null,
                status,
                status == MesMachineTimeFactStatus.Available ? billableMachineTicks : null,
                status == MesMachineTimeFactStatus.Available
                    ? MesMachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1
                    : null));

    private static MesOperationActualTimeSettlementVoidedV2IntegrationEvent MachineVoided(
        string eventId,
        MesOperationActualTimeSettledV2IntegrationEvent settled,
        DateTimeOffset voidedAtUtc)
        => new(
            eventId,
            MesIntegrationEventTypes.OperationActualTimeSettlementVoided,
            MesIntegrationEventVersions.V2,
            voidedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            settled.CorrelationId,
            settled.EventId,
            settled.OrganizationId,
            settled.EnvironmentId,
            "operator:test",
            $"actual-time:{settled.Payload.OperationTaskId}:{settled.Payload.SettlementRevision}:voided:v2",
            new OperationActualTimeSettlementVoidedV2Payload(
                settled.Payload.WorkOrderId,
                settled.Payload.OperationTaskId,
                settled.Payload.WorkCenterId,
                settled.Payload.SettlementRevision,
                settled.Payload.CompletedAtUtc,
                voidedAtUtc,
                settled.Payload.ActualLaborTicks,
                settled.Payload.ActualMachineTicks,
                settled.Payload.CoveredProductionReportNos,
                settled.Payload.DeviceAssetId,
                settled.Payload.MachineTimeStatus,
                settled.Payload.BillableMachineTicks,
                settled.Payload.MachineTimeBasisCode));

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SaveThenFailUnitOfWork(ITransactionUnitOfWork inner) : ITransactionUnitOfWork
    {
        public IDbContextTransaction? CurrentTransaction
        {
            get => inner.CurrentTransaction;
            set => inner.CurrentTransaction = value;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => inner.SaveChangesAsync(cancellationToken);

        public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            _ = await ((IUnitOfWork)inner).SaveEntitiesAsync(cancellationToken);
            throw new InjectedSaveFailureException();
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => inner.BeginTransactionAsync(cancellationToken);
        public Task CommitAsync(CancellationToken cancellationToken = default)
            => inner.CommitAsync(cancellationToken);
        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => inner.RollbackAsync(cancellationToken);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InjectedSaveFailureException : Exception;

    private static async Task WaitForAdvisoryLockWaitersAsync(
        string connectionString,
        string applicationName,
        int expectedCount,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        // Opening the probe connection is a single operation that can hang, so it keeps its own explicit
        // budget instead of falling back to Npgsql's 15 s default. Caller cancellation propagates as-is;
        // only this helper's own budget turns into a TestTimeoutException.
        await TestTimeout.RunAsync(
            operation: $"open the advisory-lock probe connection for {applicationName}",
            action: async token => await connection.OpenAsync(token),
            timeout: TimeSpan.FromSeconds(10),
            cancellationToken,
            sensitiveValues: [connectionString]);
        await using var command = new NpgsqlCommand("""
            SELECT count(*)
            FROM pg_stat_activity
            WHERE application_name = @application_name
              AND wait_event_type = 'Lock'
              AND query LIKE 'SELECT pg_advisory_xact_lock%'
            """, connection);
        command.Parameters.AddWithValue("application_name", applicationName);

        // Real PostgreSQL: the only observable fact is pg_stat_activity, so poll it on a bounded budget.
        await Eventually.WaitAsync(
            condition: $"{expectedCount} PostgreSQL advisory-lock waiters for {applicationName}",
            observe: async token => Convert.ToInt32(await command.ExecuteScalarAsync(token)),
            isSatisfied: waitingCount => waitingCount >= expectedCount,
            describe: waitingCount => $"waiters={waitingCount}; expected>={expectedCount}",
            options: new EventuallyOptions(
                Timeout: TimeSpan.FromSeconds(10),
                PollInterval: TimeSpan.FromMilliseconds(50),
                SensitiveValues: [connectionString]),
            cancellationToken);
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ErpCostPostgresFactAttribute : FactAttribute
{
    public ErpCostPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
            Skip = "Set NERV_IIP_TEST_POSTGRES to run the real PostgreSQL ERP cost-accounting acceptance test.";
    }
}
