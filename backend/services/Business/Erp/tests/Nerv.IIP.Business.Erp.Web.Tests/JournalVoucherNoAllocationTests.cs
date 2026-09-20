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
///
/// <para>
/// ⛔ <b>本类不再有「跨席位键文法冻结」那条轴</b>（#3278 收口）：S6/S7 曾各自实现一份键派生，
/// 那条轴要防的是**两份副本漂移**。派生已收拢成 <see cref="JournalVoucherNoAllocation"/> 一份，
/// 漂移在结构上不可能发生 ⇒ 不变量本身消失，不是被降级。键文法本身（前缀 / 长度前缀 / U+001F /
/// SHA-256 / 大写十六进制）现由 <c>ConsumerJournalVoucherNumberKeyContractTests</c>
/// <c>.Digest_input_is_frozen_by_golden_vectors</c> 的外部冻结向量单点承担。
///
/// ⛔ <b>本类也不断言「客户端可写键与派生键值域不相交」</b>——那条曾经写在这里，
/// 但复审实测它是假的（前导空白可绕过 FluentValidation 里的前缀判据，因为
/// <c>CodeAllocator.Normalize</c> 的 <c>Trim()</c> 跑在校验之后）。
/// 现在的表述是「不会自然相撞」，连同三条失效方向写在
/// <see cref="JournalVoucherNoAllocation"/> 的 remarks 里。
/// </para>
/// </summary>
public sealed class JournalVoucherNoAllocationTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";

    /// <summary><c>StandardCodeRules</c> 里 <c>journal-voucher</c> 规则的产出形状：<c>JV-</c> + 8 位日期 + 6 位日重置序列。</summary>
    private static readonly Regex AllocatedVoucherNoShape = new(@"^JV-[0-9]{8}-[0-9]{6}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 取号幂等键塞得进 <c>code_idempotency_keys.idempotency_key</c>，且**键长与来源单号长度无关**。
    ///
    /// <para>⭐ 前提读数（被算出来的，不是形式主义）：<c>journal_vouchers.source_no</c> 列宽与
    /// <c>idempotency_key</c> 列宽**同为 150**，<c>source_type</c> 列宽 32 ⇒ 可读拼法
    /// <c>"{类型}:{单号}"</c> 的**列允许**上界是 183 &gt; 150，顶格落库即 PostgreSQL <c>22001</c>（#3229 同形）。
    /// 三个列宽都从**真实 EF 模型**读出来对撞，⛔ 不手抄。</para>
    ///
    /// <para>⛔ <b>这条不能换成「键长 &lt;= 150」</b>：那条在单号只有一位时也成立，
    /// 鉴别不了「摘要式」与「可读拼串」——所以必须同时钉住「短单号与顶格单号的键**等长**」。</para>
    ///
    /// <para><b>失效方向</b>：加宽 <c>source_no</c> 不会让本条红（键长与它无关，这正是摘要式的目的）；
    /// 会让本条红的是**加宽 <c>source_type</c> 列 / 追加更长的类型码 / 改窄 <c>idempotency_key</c>**。
    /// ⛔ 本条看不见的方向：调用方传进来一个比 <c>source_no</c> 列宽还长的串——
    /// 那种输入下键仍定长合规，但那张凭证本身也落不了库。</para>
    /// </summary>
    [Fact]
    public async Task Allocation_idempotency_key_stays_within_the_code_idempotency_column()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model;
        var voucher = model.FindEntityType(typeof(JournalVoucher))!;
        var sourceNoWidth = voucher.GetProperty(nameof(JournalVoucher.SourceNo)).GetMaxLength()!.Value;
        var sourceTypeWidth = voucher.GetProperty(nameof(JournalVoucher.SourceType)).GetMaxLength()!.Value;
        var keyWidth = model.FindEntityType(typeof(CodeIdempotencyKey))!
            .GetProperty(nameof(CodeIdempotencyKey.IdempotencyKey)).GetMaxLength()!.Value;

        // ⚠️ ⛔ 这里**刻意不写**「可读拼法 32+1+150=183 必越界」那种前提：
        // source_type 是私有构造的闭集（12 个码最长 SUPPINV = 7），**没有生产者写得出 32 字符类型码**；
        // source_no 是本表自己的列，也不约束调用方传进来的值。
        // 「列宽 × 列宽」算出来的上界不属于任何一条真实链路——这张票上同形失真已复发三次（158/183/154）。
        // 摘要式的承重理由是**失败形态**（22001 在调用方 UoW 才抛、消费者 gate 接不住 ⇒ poison message），
        // 不是「今天会溢出」。本条只量「键长与单号长度无关、且塞得进列」这两件可观测的事。
        var saturatedSourceNo = new string('N', sourceNoWidth);
        foreach (var sourceType in JournalVoucherSourceType.All)
        {
            var shortKey = JournalVoucherNoAllocation.AllocationIdempotencyKey(sourceType, "X");
            var saturatedKey = JournalVoucherNoAllocation.AllocationIdempotencyKey(sourceType, saturatedSourceNo);

            // 键长与单号长度无关（这一条才鉴别得了摘要式 vs 可读拼串）。
            Assert.Equal(shortKey.Length, saturatedKey.Length);
            Assert.NotEqual(shortKey, saturatedKey);
            Assert.Equal(
                JournalVoucherNoAllocation.KeyPrefix.Length + sourceType.Code.Length + 1 + JournalVoucherNoAllocation.DigestLength,
                shortKey.Length);
            Assert.True(saturatedKey.Length <= keyWidth, $"族『{sourceType.Code}』的键长 {saturatedKey.Length} 超出列宽 {keyWidth}。");
        }

        // 余量格：族码即使顶到 source_type 的列宽（32）也塞得下。
        // ⛔ 别把这一格读成「有生产者会写出 32 字符类型码」——闭集不允许；
        // 它量的只是「本文法对该列宽仍有余量」，是一条**宽松**的健壮性读数。
        Assert.True(
            JournalVoucherNoAllocation.KeyPrefix.Length + sourceTypeWidth + 1 + JournalVoucherNoAllocation.DigestLength <= keyWidth);
    }

    /// <summary>
    /// 键在 <c>(类型, 单号)</c> 上是单射——**钉的是机制，不是样本**。
    ///
    /// <para>摘要输入是带长度前缀、以 U+001F 分隔的规范串，所以「段划分不同但拼起来一样」的两组输入
    /// 不会塌成同一个键。下面那一对在**裸拼**下逐字节相同（<c>"WOC"+"ADJ-1"</c> 与 <c>"WOCADJ"+"-1"</c>），
    /// 是这条机制的最不利样本。</para>
    ///
    /// <para>另一半是类型码里不含分隔符——只举几个具体键互不相等的例子挡不住
    /// 「某天给类型码里加一个冒号」。类型码互异那一半由 <c>JournalVoucherSourceContractTests</c> 承担，本条不重复。</para>
    ///
    /// <para>⭐ <b>鉴别力边界（如实登记，⛔ 别把本条读成「规范串构造被钉住了」）</b>：
    /// 本条打的是「键**单射**」这个**结果**，而这个结果被键里的**明文类型码**（<c>jv:{Code}:</c>）兜住 ⇒
    /// 对**规范串内部怎么构造**几乎零鉴别力。**隔离变异实测**（判红数「错误消息」条数）：
    /// 把规范串里「类型段与单号段之间」那一个分隔符从 U+001F 改成 <c>-</c>、
    /// 或把规范串里的类型段整个丢掉，**本条都不红**。
    /// ⇒ <b>规范串构造在本程序集里是单点承重</b>，承重方是
    /// <c>ConsumerJournalVoucherNumberKeyContractTests.Digest_input_is_frozen_by_golden_vectors</c>
    /// 那 5 组由 Python <c>hashlib</c> 外部算出的冻结向量。那条一旦被删、或被改成自指复算
    /// （用 <c>Digest</c> 求值再用 <c>Digest</c> 复算），规范串就没有任何东西看着了。
    /// ⛔ 不在本类补第二套向量——重复冻结正是 #3278 收口删掉的那类装置。</para>
    /// </summary>
    [Fact]
    public void Segment_split_does_not_collapse_two_different_sources_onto_one_key()
    {
        Assert.NotEmpty(JournalVoucherSourceType.All);
        foreach (var sourceType in JournalVoucherSourceType.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(sourceType.Code));
            Assert.DoesNotContain(":", sourceType.Code, StringComparison.Ordinal);
        }

        // 裸拼下这两组完全相同；规范串下必须分开。
        Assert.Equal(
            JournalVoucherSourceType.WorkOrderCapitalization.Code + "ADJ-1",
            JournalVoucherSourceType.WorkOrderCostAdjustment.Code + "-1");
        Assert.NotEqual(
            JournalVoucherNoAllocation.CanonicalKey(JournalVoucherSourceType.WorkOrderCapitalization, "ADJ-1"),
            JournalVoucherNoAllocation.CanonicalKey(JournalVoucherSourceType.WorkOrderCostAdjustment, "-1"));
        Assert.NotEqual(
            JournalVoucherNoAllocation.AllocationIdempotencyKey(JournalVoucherSourceType.WorkOrderCapitalization, "ADJ-1"),
            JournalVoucherNoAllocation.AllocationIdempotencyKey(JournalVoucherSourceType.WorkOrderCostAdjustment, "-1"));

        // 全部已登记族 × 两个单号 ⇒ 键两两互异。少了这条，「键里只写摘要、丢掉类型段」会全绿。
        var keys = JournalVoucherSourceType.All
            .SelectMany(sourceType => new[] { "SRC-0001", "SRC-0002" }
                .Select(sourceNo => JournalVoucherNoAllocation.AllocationIdempotencyKey(sourceType, sourceNo)))
            .ToArray();
        Assert.Equal(JournalVoucherSourceType.All.Count * 2, keys.Length);
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
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

        // 付款凭证的现金行摘要：改前写的是 voucherNo，而那时 voucherNo 恰等于付款执行单号。
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
