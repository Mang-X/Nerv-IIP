using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Coding;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #3278 / S6：命令侧建凭证位点的凭证号**是什么**。
///
/// <para>
/// <b>打的是「是什么」不是「在不在」</b>：只断言「调了分配器」或「凭证号非空」的用例，
/// 对「把上游单号当 requestedCode 传进去」「把取号幂等键换成别的串」这两类退化**零鉴别力**——
/// 那两种写法照样调了分配器。所以本类逐位点钉三件事：
/// ① 凭证号落在 <c>journal-voucher</c> 规则的值域里（<c>JV-yyyyMMdd-NNNNNN</c>）；
/// ② 它既**不等于**驱动单据的单号，也**不等于**改前那个派生串（逐位点写出改前的原值）；
/// ③ 十个位点的号**两两互异**——十个 handler 共用同一个 <see cref="ErpCodingService"/>，
///    任何一个位点把号写死、或把另一个位点的号抄过来，这一条立刻红。
/// </para>
///
/// <para>
/// <b>覆盖边界</b>：本类走的是 #3278 / S6 的命令侧位点（含 <c>MANUAL</c> 那处——它改前就走分配器，
/// 收进来只为让互异性检查覆盖同一个计数器上的全部命令侧消费者）。
/// ⛔ 不覆盖集成事件消费侧的 5 个建凭证位点（S7），也⛔ 不覆盖 seed 的两处
/// （按母票「显式不做」仍写 <c>JV-2026-S/C{n}</c>）⇒ 落地后凭证号并存**三种**格式。
/// </para>
///
/// <para>
/// <b>为什么这里可以用 InMemory</b>：本类量的是**取号**这一步的输出，不是落库约束。
/// 「同一来源重复触发只记一张」依赖 S5 那条 partial unique index，EF InMemory 看不见它，
/// 那条读数在 <see cref="ErpJournalVoucherNoPostgresAcceptanceTests"/> 里跑真 Postgres。
/// </para>
/// </summary>
public sealed class JournalVoucherNoAllocationTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";

    /// <summary><c>StandardCodeRules</c> 里 <c>journal-voucher</c> 规则的产出形状：<c>JV-</c> + 8 位日期 + 6 位日重置序列。</summary>
    private static readonly Regex AllocatedVoucherNoShape = new(@"^JV-[0-9]{8}-[0-9]{6}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 取号幂等键**两种形态都**塞得进 <c>code_idempotency_keys.idempotency_key</c>。
    ///
    /// <para>⭐ 这条实测推翻了「原样式够用」这个直觉：<c>journal_vouchers.source_no</c> 列宽与
    /// <c>idempotency_key</c> 列宽**同为 150**，原样式还要再加类型码与分隔符 ⇒ 顶格来源单号
    /// 必然越界（实测最坏 158 &gt; 150）。越界不在入口被拒，而在 <c>SaveChangesAsync</c>
    /// 换来 PostgreSQL <c>22001</c>。所以构造入口必须有非截断的摘要式回落。</para>
    ///
    /// <para>两个上界都**算出来**而不是手抄：类型码取 <see cref="JournalVoucherSourceType.All"/>
    /// 这个闭集里最长的一个，来源单号取**真实 EF 模型**上的列宽。</para>
    ///
    /// <para><b>失效方向</b>：加宽 <c>source_no</c>、追加更长的类型码、或改窄
    /// <c>idempotency_key</c>，都会让本条重新计算——顶格那一格因此永远落在摘要式上，仍然合规。
    /// ⛔ 本条看不见的方向：调用方传进来一个**比 <c>source_no</c> 列宽还长**的串
    /// （即它根本不是那一列的值）——那种输入下摘要式仍然定长合规，但那张凭证本身也落不了库。</para>
    /// </summary>
    [Fact]
    public async Task Allocation_idempotency_key_stays_within_the_code_idempotency_column()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var sourceNoMaxLength = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model
            .FindEntityType(typeof(JournalVoucher))!
            .GetProperty(nameof(JournalVoucher.SourceNo))
            .GetMaxLength();
        Assert.NotNull(sourceNoMaxLength);

        var longestCode = JournalVoucherSourceType.All.MaxBy(x => x.Code.Length)!;
        var saturatedSourceNo = new string('N', sourceNoMaxLength.Value);

        // ① 顶格输入：原样式会越界 ⇒ 构造入口必须已经退到摘要式，且摘要式合得下。
        var rawWorstCase = longestCode.Code.Length + JournalVoucherNoAllocation.SourceKeySeparator.Length + sourceNoMaxLength.Value;
        Assert.True(
            rawWorstCase > JournalVoucherNoAllocation.KeyMaxLength,
            $"原样式最坏长度 {rawWorstCase} 没有超出列宽 {JournalVoucherNoAllocation.KeyMaxLength}——" +
            "那说明本条已不再量到摘要式回落，请重新确认这条回落还有没有存在理由。");
        var saturatedKey = JournalVoucherNoAllocation.AllocationIdempotencyKey(longestCode, saturatedSourceNo);
        Assert.Equal(
            JournalVoucherNoAllocation.DigestAllocationIdempotencyKey(longestCode, saturatedSourceNo),
            saturatedKey);
        Assert.True(saturatedKey.Length <= JournalVoucherNoAllocation.KeyMaxLength, $"摘要式键长 {saturatedKey.Length} 超出列宽。");

        // ② 逐族枚举：两种形态的最坏长度都必须合得下（摘要式定长，原样式取该族的分界点）。
        foreach (var sourceType in JournalVoucherSourceType.All)
        {
            var longestRawSourceNo = new string('N', JournalVoucherNoAllocation.KeyMaxLength - sourceType.Code.Length - JournalVoucherNoAllocation.SourceKeySeparator.Length);
            var rawKey = JournalVoucherNoAllocation.AllocationIdempotencyKey(sourceType, longestRawSourceNo);
            Assert.Equal(JournalVoucherNoAllocation.KeyMaxLength, rawKey.Length);
            Assert.StartsWith(sourceType.Code + JournalVoucherNoAllocation.SourceKeySeparator, rawKey, StringComparison.Ordinal);

            // 再长一个字符就翻到摘要式，且长度骤降到定长——⛔ 不是截断。
            var overflowKey = JournalVoucherNoAllocation.AllocationIdempotencyKey(sourceType, longestRawSourceNo + "N");
            Assert.Equal(sourceType.Code.Length + JournalVoucherNoAllocation.DigestMarker.Length + JournalVoucherNoAllocation.DigestLength, overflowKey.Length);
            Assert.True(overflowKey.Length <= JournalVoucherNoAllocation.KeyMaxLength);
        }
    }

    /// <summary>
    /// 两种形态的值域不相交，且各自内部不塌号。⛔ 不截断：越界输入换来定长摘要，不是被砍掉尾巴。
    /// </summary>
    [Fact]
    public void Raw_and_digest_allocation_keys_occupy_disjoint_value_ranges()
    {
        var overflowSourceNo = new string('N', JournalVoucherNoAllocation.KeyMaxLength);
        foreach (var sourceType in JournalVoucherSourceType.All)
        {
            // 类型码既不含 ':' 也不含 '~' ⇒ 类型码后那一位唯一地区分两种形态。
            Assert.DoesNotContain(JournalVoucherNoAllocation.SourceKeySeparator, sourceType.Code, StringComparison.Ordinal);
            Assert.DoesNotContain(JournalVoucherNoAllocation.DigestMarker, sourceType.Code, StringComparison.Ordinal);

            var raw = JournalVoucherNoAllocation.AllocationIdempotencyKey(sourceType, "SRC-0001");
            var digest = JournalVoucherNoAllocation.AllocationIdempotencyKey(sourceType, overflowSourceNo);
            Assert.Equal(JournalVoucherNoAllocation.SourceKeySeparator, raw.Substring(sourceType.Code.Length, 1));
            Assert.Equal(JournalVoucherNoAllocation.DigestMarker, digest.Substring(sourceType.Code.Length, 1));
            Assert.NotEqual(raw, digest);
        }

        // 摘要输入带长度前缀 ⇒ 不同的段划分拼不出同一个输入。
        Assert.NotEqual(
            JournalVoucherNoAllocation.CanonicalKey(JournalVoucherSourceType.AccountPayable, "X"),
            JournalVoucherNoAllocation.CanonicalKey(JournalVoucherSourceType.SupplierInvoice, "X"));
        Assert.NotEqual(
            JournalVoucherNoAllocation.DigestAllocationIdempotencyKey(JournalVoucherSourceType.AccountPayable, overflowSourceNo),
            JournalVoucherNoAllocation.DigestAllocationIdempotencyKey(JournalVoucherSourceType.AccountPayable, overflowSourceNo + "N"));
    }

    /// <summary>
    /// 幂等键 <c>{类型码}:{来源单号}</c> 在闭集上是单射：类型码里不含分隔符，
    /// 所以「第一个 <c>:</c>」唯一地划出类型边界，<c>(类型, 单号)</c> 两两不同则键两两不同。
    ///
    /// <para><b>这条钉的是机制不是样本</b>：只举几个具体键互不相等的例子挡不住「某天给类型码里加一个冒号」。
    /// 另一半（类型码互异）由 <c>JournalVoucherSourceContractTests</c> 承担，本条不重复。</para>
    /// </summary>
    [Fact]
    public void Source_type_codes_carry_no_separator_so_the_allocation_key_stays_injective()
    {
        Assert.NotEmpty(JournalVoucherSourceType.All);
        foreach (var sourceType in JournalVoucherSourceType.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(sourceType.Code));
            Assert.DoesNotContain(JournalVoucherNoAllocation.SourceKeySeparator, sourceType.Code, StringComparison.Ordinal);
        }

        // 分隔符出现在**来源单号**一侧不会造成歧义（类型边界由第一个分隔符决定）；
        // 这一对是那条推论的最不利样本。
        Assert.NotEqual(
            JournalVoucherNoAllocation.AllocationIdempotencyKey(JournalVoucherSourceType.AccountPayable, "X:Y"),
            JournalVoucherNoAllocation.AllocationIdempotencyKey(JournalVoucherSourceType.AccountPayable, "X:Y:Z"));
    }

    /// <summary>
    /// 十个命令侧建凭证位点：凭证号取分配器短号，不再派生自上游单号，且彼此互异。
    /// </summary>
    [Fact]
    public async Task Command_side_voucher_sites_take_distinct_allocator_numbers_instead_of_deriving_from_the_upstream_document()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // ⭐ 十个 handler 共用同一个分配器实例。无参 `new ErpCodingService()` 是**进程内**分配器，
        // 每个 handler 各有一份计数器，十个位点会各自从 000001 起数——那样互异性检查恒假绿/恒红，
        // 量到的也不是生产行为（生产走 EF 持久化分配器，计数器在库里）。
        var coding = new ErpCodingService();

        await ErpFinanceSourceDocumentFixtures.SeedSupplierInvoiceAsync(dbContext, "INV-S6-AP", "SUP-001");
        await new CreateAccountPayableCommandHandler(dbContext, coding).Handle(
            new CreateAccountPayableCommand(Org, Env, "AP-S6-001", "INV-S6-AP", "SUP-001", 500m, "CNY", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), "NET30"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await ErpFinanceSourceDocumentFixtures.SeedSupplierInvoiceAsync(dbContext, "INV-S6-AP2", "SUP-001");
        await new CreateAccountPayableCommandHandler(dbContext, coding).Handle(
            new CreateAccountPayableCommand(Org, Env, "AP-S6-002", "INV-S6-AP2", "SUP-001", 500m, "CNY", new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), "NET30"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await ErpFinanceSourceDocumentFixtures.SeedDeliveryOrderAsync(dbContext, "DO-S6-AR", "CUS-001");
        await new CreateAccountReceivableCommandHandler(dbContext, coding).Handle(
            new CreateAccountReceivableCommand(Org, Env, "AR-S6-001", "DO-S6-AR", "CUS-001", 500m, "CNY", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15), "NET14"),
            CancellationToken.None);
        await ErpFinanceSourceDocumentFixtures.SeedDeliveryOrderAsync(dbContext, "DO-S6-AR2", "CUS-001");
        await new CreateAccountReceivableCommandHandler(dbContext, coding).Handle(
            new CreateAccountReceivableCommand(Org, Env, "AR-S6-002", "DO-S6-AR2", "CUS-001", 500m, "CNY", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15), "NET14"),
            CancellationToken.None);
        await new CreateCostCandidateCommandHandler(dbContext, coding).Handle(
            new CreateCostCandidateCommand(Org, Env, "COST-S6-001", "mes-report", "RPT-S6-001", 30m, "CNY"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new RegisterAccountPayablePaymentCommandHandler(dbContext, coding).Handle(
            new RegisterAccountPayablePaymentCommand(Org, Env, "AP-S6-001", 40m, new DateOnly(2026, 6, 20), "BANK-001", "idem-s6-ap-pay"),
            CancellationToken.None);
        var approvedPaymentExecutionNo = await new ApprovePaymentExecutionCommandHandler(dbContext, coding).Handle(
            new ApprovePaymentExecutionCommand(Org, Env, "AP-S6-002", 40m, new DateOnly(2026, 6, 20), "BANK-001", "idem-s6-ap-approve"),
            CancellationToken.None);
        await new RegisterAccountReceivableCollectionCommandHandler(dbContext, coding).Handle(
            new RegisterAccountReceivableCollectionCommand(Org, Env, "AR-S6-001", 35m, new DateOnly(2026, 6, 20), "BANK-001", "idem-s6-ar-collect"),
            CancellationToken.None);
        var registeredCashReceiptNo = await new RegisterCashReceiptCommandHandler(dbContext, coding).Handle(
            new RegisterCashReceiptCommand(Org, Env, "AR-S6-002", 35m, new DateOnly(2026, 6, 20), "BANK-001", "idem-s6-ar-register"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new ExecutePaymentExecutionCommandHandler(dbContext, coding).Handle(
            new ExecutePaymentExecutionCommand(Org, Env, approvedPaymentExecutionNo, "u-finance"),
            CancellationToken.None);
        await new MatchCashReceiptCommandHandler(dbContext, coding).Handle(
            new MatchCashReceiptCommand(Org, Env, registeredCashReceiptNo),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var registeredPaymentExecutionNo = dbContext.PaymentExecutions
            .Single(x => x.PaymentExecutionNo != approvedPaymentExecutionNo).PaymentExecutionNo;
        var matchedCashReceiptNo = dbContext.CashReceipts
            .Single(x => x.CashReceiptNo != registeredCashReceiptNo).CashReceiptNo;

        // ── 供应商发票 GR/IR 清账两条入口（ErpProcurementCommands.cs:1087 / :1207）
        await ErpFinanceSourceDocumentFixtures.SeedPurchaseReceiptAsync(dbContext, "RCV-S6-INV", "SUP-001");
        await new RecordSupplierInvoiceCommandHandler(dbContext, coding).Handle(
            new RecordSupplierInvoiceCommand(
                Org, Env, "INV-S6-MATCH", "PO-SRC-RCV-S6-INV", "RCV-S6-INV",
                new DateOnly(2026, 6, 10), new DateOnly(2026, 7, 10), "CNY", 0m, 0m,
                [new SupplierInvoiceCommandLine("L1", "L1", 1m, 1m)],
                "AP-S6-MATCH",
                "idem-s6-invoice-match"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await ErpFinanceSourceDocumentFixtures.SeedPurchaseReceiptAsync(dbContext, "RCV-S6-HELD", "SUP-001");
        await new RecordSupplierInvoiceCommandHandler(dbContext, coding).Handle(
            new RecordSupplierInvoiceCommand(
                Org, Env, "INV-S6-HELD", "PO-SRC-RCV-S6-HELD", "RCV-S6-HELD",
                new DateOnly(2026, 6, 11), new DateOnly(2026, 7, 11), "CNY", 0m, 0m,
                // 数量超收货量且容差为 0 ⇒ 落在 PaymentHeld，才走得到放行那条入口。
                [new SupplierInvoiceCommandLine("L1", "L1", 1.1m, 1m)],
                "AP-S6-HELD",
                "idem-s6-invoice-held"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ReleaseSupplierInvoicePaymentHoldCommandHandler(dbContext, coding).Handle(
            new ReleaseSupplierInvoicePaymentHoldCommand(Org, Env, "INV-S6-HELD", "AP-S6-HELD-RELEASED", "idem-s6-invoice-release"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // ── 手工凭证（ErpFinanceCommands.cs:844）。改前就走 journal-voucher 规则，S6 没改它；
        //    收进来只为让互异性检查覆盖同一个计数器上的全部命令侧消费者。
        await new PostJournalVoucherCommandHandler(dbContext, coding).Handle(
            new PostJournalVoucherCommand(
                Org, Env, null, new DateOnly(2026, 6, 25),
                [
                    new JournalVoucherCommandLine("1401", 10m, 0m, "manual debit"),
                    new JournalVoucherCommandLine("2202", 0m, 10m, "manual credit"),
                ],
                "idem-s6-manual"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // 逐位点：(位点, 来源类型, 来源单号, 改前的凭证号原值)。
        // 改前原值逐条写出来，是为了让「把上游单号当 requestedCode 传回去」这种回退**有东西可撞**。
        var sites = new (string Site, JournalVoucherSourceType SourceType, string SourceNo, string? PreChangeVoucherNo)[]
        {
            ("ErpFinanceCommands.cs:192 ForAccountPayable", JournalVoucherSourceType.AccountPayable, "AP-S6-001", "JV-AP-AP-S6-001"),
            ("ErpFinanceCommands.cs:255 ForAccountReceivable", JournalVoucherSourceType.AccountReceivable, "AR-S6-001", "JV-AR-AR-S6-001"),
            ("ErpFinanceCommands.cs:298 ForCostCandidate", JournalVoucherSourceType.CostCandidate, "COST-S6-001", "JV-COST-COST-S6-001"),
            ("ErpFinanceCommands.cs:411 RegisterAccountPayablePayment", JournalVoucherSourceType.PaymentExecution, registeredPaymentExecutionNo, registeredPaymentExecutionNo),
            ("ErpFinanceCommands.cs:553 ExecutePaymentExecution", JournalVoucherSourceType.PaymentExecution, approvedPaymentExecutionNo, approvedPaymentExecutionNo),
            ("ErpFinanceCommands.cs:647 RegisterAccountReceivableCollection", JournalVoucherSourceType.CashReceipt, matchedCashReceiptNo, matchedCashReceiptNo),
            ("ErpFinanceCommands.cs:762 MatchCashReceipt", JournalVoucherSourceType.CashReceipt, registeredCashReceiptNo, registeredCashReceiptNo),
            ("ErpProcurementCommands.cs:1087 RecordSupplierInvoice", JournalVoucherSourceType.SupplierInvoice, "INV-S6-MATCH", "JV-AP-AP-S6-MATCH"),
            ("ErpProcurementCommands.cs:1207 ReleaseSupplierInvoicePaymentHold", JournalVoucherSourceType.SupplierInvoice, "INV-S6-HELD", "JV-AP-AP-S6-HELD-RELEASED"),
            // MANUAL 位点改前就是分配器短号，没有「改前原值」可撞 ⇒ 这一格只参与形状与互异性。
            ("ErpFinanceCommands.cs:844 PostJournalVoucher", JournalVoucherSourceType.Manual, null!, null),
        };

        var observed = new List<(string Site, string VoucherNo)>();
        foreach (var site in sites)
        {
            var voucher = site.SourceNo is null
                ? dbContext.JournalVouchers.Single(x => x.SourceType == site.SourceType.Code)
                : dbContext.JournalVouchers.Single(x => x.SourceType == site.SourceType.Code && x.SourceNo == site.SourceNo);
            Assert.True(
                AllocatedVoucherNoShape.IsMatch(voucher.VoucherNo),
                $"{site.Site} 的凭证号『{voucher.VoucherNo}』不是 journal-voucher 规则的产出形状。");
            if (site.PreChangeVoucherNo is not null)
            {
                Assert.NotEqual(site.PreChangeVoucherNo, voucher.VoucherNo);
            }

            if (site.SourceNo is not null)
            {
                Assert.NotEqual(site.SourceNo, voucher.VoucherNo);
            }

            observed.Add((site.Site, voucher.VoucherNo));
        }

        // ⭐ 付款凭证的现金行摘要：改前写的是 voucherNo，而那时 voucherNo 恰等于付款执行单号。
        // 换号后若仍回抄 voucherNo，摘要会退化成凭证号自指（对账时零信息量）——
        // 变异实测（MUT-M7）证明：不写这一条，把摘要改回 voucherNo 会**全绿存活**。
        foreach (var paymentExecutionNo in new[] { registeredPaymentExecutionNo, approvedPaymentExecutionNo })
        {
            var paymentVoucher = dbContext.JournalVouchers
                .Include(x => x.Lines)
                .Single(x => x.SourceType == JournalVoucherSourceType.PaymentExecution.Code && x.SourceNo == paymentExecutionNo);
            var cashLine = Assert.Single(paymentVoucher.Lines, x => x.AccountCode == "BANK-001");
            Assert.Contains(paymentExecutionNo, cashLine.Memo, StringComparison.Ordinal);
            Assert.DoesNotContain(paymentVoucher.VoucherNo, cashLine.Memo, StringComparison.Ordinal);
        }

        Assert.Equal(10, observed.Count);
        // 互异：十个位点共用一个计数器，任何一格写死常量或抄别格的号都会在这里红。
        Assert.Equal(observed.Count, observed.Select(x => x.VoucherNo).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 同一来源单据重复取号拿回**同一个**号，且不推进序列。
    /// 这条量的是取号那一层的幂等，⛔ 不是落库那条唯一索引（那在真 Postgres 上量）。
    /// </summary>
    [Fact]
    public async Task Repeated_allocation_for_the_same_source_document_returns_the_same_number()
    {
        var coding = new ErpCodingService();
        var first = await JournalVoucherNoAllocation.AllocateAsync(
            coding, Org, Env, JournalVoucherSourceType.AccountPayable, "AP-REPLAY-001", CancellationToken.None);
        var replay = await JournalVoucherNoAllocation.AllocateAsync(
            coding, Org, Env, JournalVoucherSourceType.AccountPayable, "AP-REPLAY-001", CancellationToken.None);
        var otherSource = await JournalVoucherNoAllocation.AllocateAsync(
            coding, Org, Env, JournalVoucherSourceType.AccountPayable, "AP-REPLAY-002", CancellationToken.None);
        // 同一单号、不同类型也必须拿到不同的号——否则幂等键漏掉了类型这一段。
        var otherType = await JournalVoucherNoAllocation.AllocateAsync(
            coding, Org, Env, JournalVoucherSourceType.SupplierInvoice, "AP-REPLAY-001", CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.NotEqual(first, otherSource);
        Assert.NotEqual(first, otherType);
        Assert.NotEqual(otherSource, otherType);
        foreach (var code in new[] { first, otherSource, otherType })
        {
            Assert.Matches(AllocatedVoucherNoShape, code);
        }
    }
}
