using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using NetCorePal.Extensions.DependencyInjection;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #3278 / S6：凭证号换成分配器短号之后，**重放仍然只记一张凭证**。
///
/// <para>
/// <b>为什么必须真 Postgres</b>：换号后「只记一张」由两层承担——取号这一层按来源身份回放同一个号，
/// 落库那一层撞 S5 的 <c>(organization_id, environment_id, source_type, source_no)</c> partial unique index。
/// EF InMemory **看不见唯一索引**，第二层在它上面恒绿，重复行只会多一条不会报错。
/// </para>
/// <para>
/// <b>为什么必须走生产装配</b>：无参 <c>new ErpCodingService()</c> 是**进程内**分配器，每个 handler 实例各有一份
/// 计数器与幂等表，第二次调用拿不到第一次写下的幂等键 ⇒ <c>IsIdempotentReplay</c> 恒 false，
/// 重放分支根本走不到，而且多个位点会各自从 000001 起数出重复凭证号。
/// 这里用 <c>AddErpPostgreSqlPersistence</c> + <c>ErpCodingService</c>，分配器落在库里，与生产一致。
/// </para>
/// <para>
/// <b>覆盖边界</b>：本类只走 S6 的命令侧位点。⛔ 不覆盖集成事件消费侧的 5 处（S7，含 CAP 重投那面），
/// ⛔ 不覆盖 seed 的两处（母票「显式不做」，仍写 <c>JV-2026-S/C{n}</c>）。
/// </para>
/// </summary>
[Collection("ERP PostgreSQL acceptance")]
public sealed class ErpJournalVoucherNoPostgresAcceptanceTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";

    /// <summary>
    /// 命令侧四类来源各自重复触发一次：每类只留一张凭证，凭证号是分配器短号，且**重放拿回同一个号**
    /// （号没有被多烧一个）。最后再直接往同一来源插第二张（凭证号刻意取另一个号）⇒ 必须撞
    /// <b>来源索引</b>而不是 <c>voucher_no</c> 索引——这证明换号之后承重的是来源两列。
    /// </summary>
    [ErpCostPostgresFact(Timeout = 180_000)]
    public async Task PostgreSQL_repeated_commands_for_the_same_source_document_record_one_voucher_each()
    {
        await ErpPostgresLaneDatabase.ResetSchemaAsync();
        await using var provider = CreateErpPersistenceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var coding = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        ErpPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        await SeedAccountsAsync(db);

        // ── ① 直接应付（ErpFinanceCommands.cs:192）
        await ErpFinanceSourceDocumentFixtures.SeedSupplierInvoiceAsync(db, "INV-PG-AP", "SUP-001");
        var payableCommand = new CreateAccountPayableCommand(
            Org, Env, "AP-PG-001", "INV-PG-AP", "SUP-001", 500m, "CNY",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), "NET30", "idem-pg-ap");
        await new CreateAccountPayableCommandHandler(db, coding).Handle(payableCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        await new CreateAccountPayableCommandHandler(db, coding).Handle(payableCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        var payableVoucherNo = await AssertSingleAllocatedVoucherAsync(db, JournalVoucherSourceType.AccountPayable, "AP-PG-001");

        // ── ② 应收（:255）
        await ErpFinanceSourceDocumentFixtures.SeedDeliveryOrderAsync(db, "DO-PG-AR", "CUS-001");
        var receivableCommand = new CreateAccountReceivableCommand(
            Org, Env, "AR-PG-001", "DO-PG-AR", "CUS-001", 500m, "CNY",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15), "NET14", "idem-pg-ar");
        await new CreateAccountReceivableCommandHandler(db, coding).Handle(receivableCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        await new CreateAccountReceivableCommandHandler(db, coding).Handle(receivableCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        var receivableVoucherNo = await AssertSingleAllocatedVoucherAsync(db, JournalVoucherSourceType.AccountReceivable, "AR-PG-001");

        // ── ③ 付款执行：批准即执行入口（:411）。重放走的是「来源两列已有」那条早退。
        var paymentCommand = new RegisterAccountPayablePaymentCommand(
            Org, Env, "AP-PG-001", 40m, new DateOnly(2026, 6, 20), "BANK-001", "idem-pg-ap-pay");
        await new RegisterAccountPayablePaymentCommandHandler(db, coding).Handle(paymentCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        await new RegisterAccountPayablePaymentCommandHandler(db, coding).Handle(paymentCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        var paymentExecutionNo = (await db.PaymentExecutions.AsNoTracking().SingleAsync()).PaymentExecutionNo;
        var paymentVoucherNo = await AssertSingleAllocatedVoucherAsync(db, JournalVoucherSourceType.PaymentExecution, paymentExecutionNo);

        // ⭐ 两条付款入口必须认**同一把来源键**。
        // ⚠️ 只在这里对 Register 那条入口做一次回放比对是**不够的**——变异实测（MUT-M4）证明：
        // 把 ExecutePaymentExecution 那一处的取号键改成 `{付款执行单号}-EXEC`，
        // 只比 Register 这条的矩阵**全绿存活**（那条入口今天被状态守卫/来源守卫挡住，走不到取号）。
        // 所以下面单独跑一遍「先批准后执行」，再拿**规范键**去回放它实际用的号。
        Assert.Equal(
            paymentVoucherNo,
            await JournalVoucherNoAllocation.AllocateAsync(
                coding, Org, Env, JournalVoucherSourceType.PaymentExecution, paymentExecutionNo, CancellationToken.None));

        // ── ③b 付款执行：先批准后执行入口（:553）
        var approvedPaymentExecutionNo = await new ApprovePaymentExecutionCommandHandler(db, coding).Handle(
            new ApprovePaymentExecutionCommand(Org, Env, "AP-PG-001", 50m, new DateOnly(2026, 6, 20), "BANK-001", "idem-pg-ap-approve"),
            CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        var executeCommand = new ExecutePaymentExecutionCommand(Org, Env, approvedPaymentExecutionNo, "u-finance");
        await new ExecutePaymentExecutionCommandHandler(db, coding).Handle(executeCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        await new ExecutePaymentExecutionCommandHandler(db, coding).Handle(executeCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        var executedPaymentVoucherNo = await AssertSingleAllocatedVoucherAsync(
            db, JournalVoucherSourceType.PaymentExecution, approvedPaymentExecutionNo);
        // 这条入口写下的号必须就是**规范键**（APPAY, 付款执行单号）回放出来的号。
        // 它一旦用了别的键，这里的回放会换回一个全新的号 ⇒ 红。
        Assert.Equal(
            executedPaymentVoucherNo,
            await JournalVoucherNoAllocation.AllocateAsync(
                coding, Org, Env, JournalVoucherSourceType.PaymentExecution, approvedPaymentExecutionNo, CancellationToken.None));

        // ── ④ 收款：登记即匹配入口（:647）+ 先登记后匹配入口（:762）
        var collectionCommand = new RegisterAccountReceivableCollectionCommand(
            Org, Env, "AR-PG-001", 35m, new DateOnly(2026, 6, 20), "BANK-001", "idem-pg-ar-collect");
        await new RegisterAccountReceivableCollectionCommandHandler(db, coding).Handle(collectionCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        await new RegisterAccountReceivableCollectionCommandHandler(db, coding).Handle(collectionCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        var cashReceiptNo = (await db.CashReceipts.AsNoTracking().SingleAsync()).CashReceiptNo;
        var collectionVoucherNo = await AssertSingleAllocatedVoucherAsync(db, JournalVoucherSourceType.CashReceipt, cashReceiptNo);
        Assert.Equal(
            collectionVoucherNo,
            await JournalVoucherNoAllocation.AllocateAsync(
                coding, Org, Env, JournalVoucherSourceType.CashReceipt, cashReceiptNo, CancellationToken.None));

        // ── ④b 收款：先登记后匹配入口（:762）。理由同 ③b —— 这条入口的取号键要单独量。
        var registeredCashReceiptNo = await new RegisterCashReceiptCommandHandler(db, coding).Handle(
            new RegisterCashReceiptCommand(Org, Env, "AR-PG-001", 25m, new DateOnly(2026, 6, 20), "BANK-001", "idem-pg-ar-register"),
            CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        var matchCommand = new MatchCashReceiptCommand(Org, Env, registeredCashReceiptNo);
        await new MatchCashReceiptCommandHandler(db, coding).Handle(matchCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        await new MatchCashReceiptCommandHandler(db, coding).Handle(matchCommand, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);
        var matchedCollectionVoucherNo = await AssertSingleAllocatedVoucherAsync(
            db, JournalVoucherSourceType.CashReceipt, registeredCashReceiptNo);
        Assert.Equal(
            matchedCollectionVoucherNo,
            await JournalVoucherNoAllocation.AllocateAsync(
                coding, Org, Env, JournalVoucherSourceType.CashReceipt, registeredCashReceiptNo, CancellationToken.None));

        // 六个位点的号互异——同一个日重置计数器上各取各的。
        var allocated = new[]
        {
            payableVoucherNo, receivableVoucherNo, paymentVoucherNo, executedPaymentVoucherNo,
            collectionVoucherNo, matchedCollectionVoucherNo,
        };
        Assert.Equal(allocated.Length, allocated.Distinct(StringComparer.Ordinal).Count());

        // ── ⑤ 同一来源、**另一个**凭证号 ⇒ 只可能撞来源索引。
        db.ChangeTracker.Clear();
        db.JournalVouchers.Add(JournalVoucher.Post(
            Org, Env, "JV-20991231-999999", new DateOnly(2026, 6, 1),
            [
                new JournalVoucherLineDraft("5001", 1m, 0m, "duplicate probe", "CNY", 1m, 1m, null),
                new JournalVoucherLineDraft("2202", 0m, 1m, "duplicate probe", "CNY", 1m, null, 1m),
            ],
            JournalVoucherSourceType.AccountPayable,
            "AP-PG-001"));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(CancellationToken.None));
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Contains("source_type", postgres.ConstraintName, StringComparison.Ordinal);
        Assert.DoesNotContain("voucher_no", postgres.ConstraintName, StringComparison.Ordinal);
    }

    private static async Task<string> AssertSingleAllocatedVoucherAsync(
        ApplicationDbContext db,
        JournalVoucherSourceType sourceType,
        string sourceNo)
    {
        db.ChangeTracker.Clear();
        var voucherNos = await db.JournalVouchers.AsNoTracking()
            .Where(x => x.SourceType == sourceType.Code && x.SourceNo == sourceNo)
            .Select(x => x.VoucherNo)
            .ToListAsync();
        var voucherNo = Assert.Single(voucherNos);
        // 形状：journal-voucher 规则的短号，且**不等于**来源单号（改前这两个族的凭证号就是来源单号本身）。
        Assert.Matches(@"^JV-[0-9]{8}-[0-9]{6}$", voucherNo);
        Assert.NotEqual(sourceNo, voucherNo);
        return voucherNo;
    }

    private static async Task SeedAccountsAsync(ApplicationDbContext db)
    {
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
            db.GLAccounts.Add(GLAccount.Create(Org, Env, code, name, type));
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static ServiceProvider CreateErpPersistenceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(PostJournalVoucherCommand).Assembly));
        services.AddErpPostgreSqlPersistence(ErpPostgresLaneDatabase.ConnectionString);
        services.AddScoped<ErpCodingService>();
        return services.BuildServiceProvider();
    }
}
