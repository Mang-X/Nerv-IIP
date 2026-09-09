using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Validation;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #3229：派生凭证号的有效上界是「<c>voucher_no</c> 列宽 − 前缀 − 分隔符 − 其余段」，不是上游单号的列宽。
///
/// 这个类钉住四件事：
/// - <see cref="Voucher_no_column_width_matches_the_policy_constant"/>：列宽从 **EF 模型**
///   （<c>IDesignTimeModel</c>）读，不读代码里的常量 → 单边改 <c>HasMaxLength</c> 即红；
/// - 顶格面：拿真实构造入口跑**从生产调用点枚举出来的**顶格输入，
///   把它实际产出的长度与从 EF 模型读到的列宽直接对撞；
/// - 撞号面：原样式/摘要式两个值域不相交、不同段划分不塌成同号；
/// - 摘要实现：用**冻结黄金向量**（外部独立实现算出的 hex 串硬编码在断言里）钉住，
///   **不是**「先用 <c>Digest</c> 求值再用同一个 <c>Digest</c> 复算」那种自指断言。
///
/// **值域边界（声明放弃了什么，别读成完备）**：
/// 1. 列宽读的是 **EF 模型**而不是迁移脚本，模型/迁移漂移不由本类负责。
/// 2. **本类不证明「所有凭证号都走 <see cref="ErpVoucherNoPolicy.Compose"/>」**——那需要源码扫描，
///    #3231 的实证是这类扫描不收敛，故本票不做。绕开 <c>Compose</c> 直接 <c>$"JV-…"</c> 仍然编译得过。
/// 3. **唯一索引下真的插得进/插不进**由真 Postgres 用例负责（见
///    <c>ErpCostAccountingPostgresAcceptanceTests</c>），EF InMemory 既看不见列宽也看不见唯一索引，
///    本类的绿**不能**读成「落库不会 22001」。
/// </summary>
public sealed class ErpVoucherNoLengthContractTests
{
    /// <summary>ERP 侧承载上游业务单号的列宽，全部是 100（工单号 / 报工单号 / 移动号 / 工序任务号 / 各类单据号）。</summary>
    private const int UpstreamNoColumnWidth = 100;

    /// <summary>
    /// <c>InventoryMovementId</c> 全仓**没有** <c>HasMaxLength</c>；发布侧是
    /// <c>InventoryIntegrationEventConverters.cs</c> 的 <c>movementId.ToString()</c>，即 GUID 文本 36 字符。
    /// </summary>
    private const int InventoryMovementIdWidth = 36;

    /// <summary>
    /// <c>CostVariancePosting.PostLateAdjustmentAsync</c> 的 <c>sourceId</c> 在生产侧的**最宽**取值。
    ///
    /// **这是本 PR 复审更正过的读数，别再照抄旧数**：该方法有 **5 个调用点**（不是 3 个），
    /// 覆盖 **7 种** <c>sourceId</c> 形状——
    /// <list type="bullet">
    /// <item><c>WorkOrderCostIntegrationEventHandlers.cs:219</c> → <c>payload.ReportNo</c>（列宽 100）；</item>
    /// <item>同文件 <c>:226</c> → <c>item.MovementId</c>（列宽 100）；</item>
    /// <item>同文件 <c>:299</c> → <c>payload.InventoryMovementId</c>（GUID 36）；</item>
    /// <item><c>OperationLaborSettlementIntegrationEventHandlers.cs:207</c> → <c>{OperationTaskId}-r{rev}</c>；</item>
    /// <item>同文件 <c>:607</c> → <c>{OperationTaskId}-r{rev}-void</c>；</item>
    /// <item><c>OperationMachineOverheadSettlementIntegrationEventHandlers.cs:195</c> → <c>machine-{OperationTaskId}-r{rev}</c>；</item>
    /// <item>同文件 <c>:347</c> → <c>machine-{OperationTaskId}-r{rev}-void</c>。</item>
    /// </list>
    /// <c>OperationTaskId</c> 列宽 100，<c>SettlementRevision</c> 是 <c>long</c>（十进制最多 19 位），
    /// 故最宽形状 <c>machine-{100}-r{19}-void</c> = 8+100+2+19+5 = <b>134</b>。
    /// 于是改前 <c>JV-WOC-ADJ-{workOrderId}-{sourceId}</c> 的类型上界是 11+100+1+134 = <b>246</b>，
    /// **不是**票面估的 148，也不是本 PR 首轮写的 212（首轮只枚举了前 3 个调用点）。
    ///
    /// 这个数取决于 <c>long</c> 的十进制位数，所以**重点不是它等于几**，而是：
    /// 原样式的长度**没有任何低于列宽的自然上界**，因此上界只能由 <see cref="ErpVoucherNoPolicy.Compose"/>
    /// 从列宽兜底，不能由任何人估算。
    /// </summary>
    private const string WidestAdjustmentSourceIdShape = "machine-{OperationTaskId}-r{SettlementRevision}-void";

    private const int WidestAdjustmentSourceIdWidth = 134;

    [Fact]
    public void Voucher_no_column_width_matches_the_policy_constant()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var property = model.FindEntityType(typeof(JournalVoucher))!.FindProperty(nameof(JournalVoucher.VoucherNo))!;

        Assert.Equal("voucher_no", property.GetColumnName());
        Assert.Equal(ErpVoucherNoPolicy.ColumnMaxLength, property.GetMaxLength());
    }

    /// <summary>改口径前的构造式在顶格输入下确实超出列宽——这条读数说明缺陷不是假想的。</summary>
    [Fact]
    public void Pre_change_shapes_overflow_the_column_at_saturated_upstream_identifiers()
    {
        var columnWidth = ColumnWidthFromModel();
        var workOrderId = new string('W', UpstreamNoColumnWidth);
        var movementId = new string('M', InventoryMovementIdWidth);
        var widestSourceId = WidestAdjustmentSourceId();
        var payableNo = new string('P', UpstreamNoColumnWidth);

        Assert.Equal(WidestAdjustmentSourceIdWidth, widestSourceId.Length);
        Assert.Equal(144, $"JV-WOC-{workOrderId}-{movementId}".Length);
        Assert.Equal(246, $"JV-WOC-ADJ-{workOrderId}-{widestSourceId}".Length);
        Assert.Equal(106, $"JV-AP-{payableNo}".Length);
        Assert.True($"JV-WOC-{workOrderId}-{movementId}".Length > columnWidth);
        Assert.True($"JV-WOC-ADJ-{workOrderId}-{widestSourceId}".Length > columnWidth);
        Assert.True($"JV-AP-{payableNo}".Length > columnWidth);
    }

    /// <summary>
    /// 族**从 <see cref="VoucherFamily.All"/> 闭集枚举**，不是手抄名单：
    /// 新增族自动进入这条覆盖面。断言的是「族名字符集」与「摘要式仍塞得进列宽」——
    /// 改闭集类型前这两条靠运行期 <c>AssertFamily</c> 守，现在由类型 + 本条共同承担。
    /// </summary>
    [Theory]
    [MemberData(nameof(AllFamilies))]
    public void Every_family_keeps_the_family_boundary_unambiguous_and_fits_the_column(VoucherFamily family)
    {
        var columnWidth = ColumnWidthFromModel();

        Assert.All(
            family.Name,
            character => Assert.True(
                character is (>= 'A' and <= 'Z') or (>= '0' and <= '9'),
                $"族名 {family.Name} 含非 [A-Z0-9] 字符 '{character}'，族名边界不再唯一。"));
        Assert.DoesNotContain(ErpVoucherNoPolicy.RawSeparator, family.Name, StringComparison.Ordinal);
        Assert.DoesNotContain(ErpVoucherNoPolicy.DigestMarker, family.Name, StringComparison.Ordinal);
        Assert.True(
            ErpVoucherNoPolicy.GlobalPrefix.Length
                + family.Name.Length
                + ErpVoucherNoPolicy.DigestMarker.Length
                + ErpVoucherNoPolicy.DigestLength
                <= columnWidth,
            $"族 {family.Name} 的摘要式凭证号超出列宽 {columnWidth}。");
    }

    /// <summary>
    /// 每个族在**它自己最宽的生产输入**下都必须塞得进从 EF 模型读到的列宽。
    /// 输入宽度取自各族真实来源列宽，不是随便挑的数。
    /// </summary>
    [Theory]
    [MemberData(nameof(SaturatedProductionInputs))]
    public void Saturated_production_inputs_stay_within_the_column_width(VoucherFamily family, string[] segments)
    {
        var columnWidth = ColumnWidthFromModel();

        var voucherNo = ErpVoucherNoPolicy.Compose(family, segments);

        Assert.True(
            voucherNo.Length <= columnWidth,
            $"族 {family.Name} 的顶格凭证号长度 {voucherNo.Length} 超出列宽 {columnWidth}。");
    }

    /// <summary>合得下时必须与改前逐字节相同，否则存量行和「按号查重」会集体失配。</summary>
    [Fact]
    public void Short_inputs_keep_the_pre_change_voucher_numbers_byte_for_byte()
    {
        Assert.Equal("JV-WOC-WO-0001-MOVE-0001", ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, "WO-0001", "MOVE-0001"));
        Assert.Equal("JV-GRIR-GR-0001", ErpVoucherNoPolicy.Compose(VoucherFamily.GoodsReceiptIrAccrual, "GR-0001"));
        Assert.Equal("JV-PRTN-PRTN-0001", ErpVoucherNoPolicy.Compose(VoucherFamily.PurchaseReturn, "PRTN-0001"));
        Assert.Equal("JV-CN-CN-0001", ErpVoucherNoPolicy.Compose(VoucherFamily.CreditNote, "CN-0001"));
        Assert.Equal("JV-AP-AP-0001", ErpVoucherNoPolicy.Compose(VoucherFamily.AccountPayable, "AP-0001"));
        Assert.Equal("JV-AR-AR-0001", ErpVoucherNoPolicy.Compose(VoucherFamily.AccountReceivable, "AR-0001"));
        Assert.Equal("JV-COST-COST-0001", ErpVoucherNoPolicy.Compose(VoucherFamily.CostCandidate, "COST-0001"));
    }

    /// <summary>
    /// 唯一**不**逐字节兼容的族：调整族由 <c>JV-WOC-ADJ-</c> 改成 <c>JV-WOCADJ-</c>。
    /// 这条把「值域已变」当断言写死，免得它被读成兼容。改前形状与资本化族前缀相互包含，
    /// 那才是它必须改名的原因。
    /// </summary>
    [Fact]
    public void The_adjustment_family_deliberately_changed_its_value_range()
    {
        var current = ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCostAdjustment, "WO-0001", "RPT-0001");

        Assert.Equal("JV-WOCADJ-WO-0001-RPT-0001", current);
        Assert.NotEqual("JV-WOC-ADJ-WO-0001-RPT-0001", current);
        Assert.NotEqual(
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, "ADJ-WO-0001", "RPT-0001"),
            current);
    }

    /// <summary>
    /// 恰好等于列宽的输入仍走原样式，多一位换摘要式。
    /// **摘要式那一侧用冻结黄金向量断言**（hex 由本仓之外的独立实现算出并硬编码），
    /// 不用 <c>Digest(...)</c> 复算——否则改 <c>Digest</c> 两侧同步变，等号恒成立。
    /// </summary>
    [Fact]
    public void The_boundary_between_raw_and_digest_sits_exactly_at_the_column_width()
    {
        var prefixLength = ErpVoucherNoPolicy.GlobalPrefix.Length
            + VoucherFamily.AccountPayable.Name.Length
            + ErpVoucherNoPolicy.RawSeparator.Length;
        var exactFit = new string('N', ErpVoucherNoPolicy.ColumnMaxLength - prefixLength);
        var oneOver = exactFit + "N";

        var fitted = ErpVoucherNoPolicy.Compose(VoucherFamily.AccountPayable, exactFit);
        var overflowed = ErpVoucherNoPolicy.Compose(VoucherFamily.AccountPayable, oneOver);

        Assert.Equal(ErpVoucherNoPolicy.ColumnMaxLength, fitted.Length);
        Assert.Equal($"JV-AP-{exactFit}", fitted);
        Assert.Equal(GoldenApOneOverColumnWidth, overflowed);
        Assert.True(overflowed.Length < ErpVoucherNoPolicy.ColumnMaxLength);
    }

    /// <summary>
    /// 摘要实现的**冻结黄金向量**：期望值是外部独立实现按
    /// 「<c>JV-{FAMILY}~UPPERHEX(SHA256(规范串))</c>、规范串 = 各段以 U+001F 分隔并各自前置十进制长度」
    /// 算出后硬编码进来的。改 <c>Digest</c>、<c>CanonicalKey</c>、分隔符、族名任何一处都会红。
    /// </summary>
    [Fact]
    public void Digest_shape_matches_frozen_golden_vectors()
    {
        Assert.Equal(
            GoldenApOneOverColumnWidth,
            ErpVoucherNoPolicy.Compose(VoucherFamily.AccountPayable, new string('N', 95)));
        Assert.Equal(
            "JV-WOC~8F32B13C55520993706A9240AAD2CD11F28B7A9B8D5618E2AC72CA8ACAEB7657",
            ErpVoucherNoPolicy.Compose(
                VoucherFamily.WorkOrderCapitalization,
                new string('W', UpstreamNoColumnWidth),
                "11111111-2222-4333-8444-555555555555"));
        Assert.Equal(
            "JV-WOCADJ~DAF72AA2B99AED8CE17F44AA1606D978C9FD53D9D134D5594410D15B761DFB61",
            ErpVoucherNoPolicy.Compose(
                VoucherFamily.WorkOrderCostAdjustment,
                new string('W', UpstreamNoColumnWidth),
                new string('S', UpstreamNoColumnWidth)));
        Assert.Equal(
            "JV-WOCADJ~C82CB824FD3474EFC81F2E9FAEC876DE7DB610C08C17243F828EBFFB52AD1305",
            ErpVoucherNoPolicy.Compose(
                VoucherFamily.WorkOrderCostAdjustment,
                new string('W', UpstreamNoColumnWidth),
                WidestAdjustmentSourceId()));
    }

    /// <summary>两种形态在族名后那一位分别是 <c>-</c> 与 <c>~</c>，值域不相交。</summary>
    [Fact]
    public void Raw_and_digest_shapes_occupy_disjoint_value_ranges()
    {
        var raw = ErpVoucherNoPolicy.Compose(VoucherFamily.AccountPayable, "AP-0001");
        var digest = ErpVoucherNoPolicy.Compose(VoucherFamily.AccountPayable, new string('N', ErpVoucherNoPolicy.ColumnMaxLength));

        Assert.StartsWith("JV-AP" + ErpVoucherNoPolicy.RawSeparator, raw, StringComparison.Ordinal);
        Assert.StartsWith("JV-AP" + ErpVoucherNoPolicy.DigestMarker, digest, StringComparison.Ordinal);
        Assert.NotEqual(raw, digest);
    }

    /// <summary>摘要输入带长度前缀且以 U+001F 分隔，故不同的段划分不会拼成同一个输入。</summary>
    [Fact]
    public void Digest_inputs_keep_different_segment_splits_distinct()
    {
        var head = new string('X', 60);
        var tail = new string('Y', 60);

        var left = ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, head + "-" + tail, "Z");
        var right = ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, head, tail + "-Z");

        Assert.NotEqual(left, right);
        Assert.StartsWith("JV-WOC" + ErpVoucherNoPolicy.DigestMarker, left, StringComparison.Ordinal);
        Assert.StartsWith("JV-WOC" + ErpVoucherNoPolicy.DigestMarker, right, StringComparison.Ordinal);
    }

    /// <summary>同一来源必须稳定地得到同一个凭证号，否则重放会记出第二张凭证。</summary>
    [Fact]
    public void The_same_source_always_composes_to_the_same_voucher_number()
    {
        var workOrderId = new string('W', UpstreamNoColumnWidth);
        var movementId = Guid.NewGuid().ToString();

        Assert.Equal(
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, workOrderId, movementId),
            ErpVoucherNoPolicy.Compose(VoucherFamily.WorkOrderCapitalization, workOrderId, movementId));
    }

    /// <summary>段缺失属编译期程序员错误，抛 <see cref="ArgumentException"/> 而不是业务异常。</summary>
    [Fact]
    public void Missing_segments_are_programmer_errors()
    {
        Assert.Throws<ArgumentException>(() => ErpVoucherNoPolicy.Compose(VoucherFamily.AccountPayable));
        Assert.Throws<ArgumentException>(() => ErpVoucherNoPolicy.Compose(VoucherFamily.AccountPayable, " "));
    }

    public static TheoryData<VoucherFamily> AllFamilies()
    {
        var data = new TheoryData<VoucherFamily>();
        foreach (var family in VoucherFamily.All)
        {
            data.Add(family);
        }

        return data;
    }

    public static TheoryData<VoucherFamily, string[]> SaturatedProductionInputs()
    {
        var upstreamNo = new string('N', UpstreamNoColumnWidth);
        var data = new TheoryData<VoucherFamily, string[]>
        {
            // 成品入库资本化：工单号（列宽 100）+ 库存移动 id（GUID 36）。
            { VoucherFamily.WorkOrderCapitalization, [new string('W', UpstreamNoColumnWidth), new string('M', InventoryMovementIdWidth)] },
            // 迟到调整：工单号（列宽 100）+ 5 个调用点里最宽的 sourceId 形状。
            { VoucherFamily.WorkOrderCostAdjustment, [new string('W', UpstreamNoColumnWidth), WidestAdjustmentSourceId()] },
            { VoucherFamily.GoodsReceiptIrAccrual, [upstreamNo] },
            { VoucherFamily.PurchaseReturn, [upstreamNo] },
            { VoucherFamily.CreditNote, [upstreamNo] },
            { VoucherFamily.AccountPayable, [upstreamNo] },
            { VoucherFamily.AccountReceivable, [upstreamNo] },
            { VoucherFamily.CostCandidate, [upstreamNo] },
        };
        return data;
    }

    private const string GoldenApOneOverColumnWidth =
        "JV-AP~BD2C658EB8F4BD251805C6B5271D05D744AF2A6F1FF1B844D6748F6BA043A449";

    /// <summary><see cref="WidestAdjustmentSourceIdShape"/> 在类型上界下的实例：8+100+2+19+5 = 134。</summary>
    private static string WidestAdjustmentSourceId()
    {
        Assert.Contains("machine-", WidestAdjustmentSourceIdShape, StringComparison.Ordinal);
        return "machine-"
            + new string('T', UpstreamNoColumnWidth)
            + "-r"
            + long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "-void";
    }

    private static int ColumnWidthFromModel()
    {
        using var dbContext = CreateModelOnlyDbContext();
        return dbContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(JournalVoucher))!
            .FindProperty(nameof(JournalVoucher.VoucherNo))!
            .GetMaxLength()!.Value;
    }

    private static ApplicationDbContext CreateModelOnlyDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=nerv_iip_voucher_no_contract;Username=nerv;Password=nerv",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ErpFacts.Schema))
            .Options;
        return new ApplicationDbContext(options, new ContractNoopMediator());
    }

    private sealed class ContractNoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("This test mediator only supports publish.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");
    }
}
