using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Validation;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// 凭证号相关的两个**宽度事实**，从 EF 模型与生产侧取值形状读出来对撞。
///
/// <b>#3278 / S8 退役登记（⛔ 别把这个类读成还在守 #3229）</b>。本类改前有 13 条用例，
/// 其中 12 条钉的是 <c>ErpVoucherNoPolicy.Compose</c> 的派生构造：族名字符集、闭集登记表对撞、
/// 顶格输入不越列宽、原样式/摘要式值域不相交、摘要黄金向量、分段歧义、构造幂等。
/// S6（PR #3495）与 S7（PR #3496）把全部 15 个生产建凭证位点改成分配器短号后，
/// 派生构造在生产侧调用点归零，S8 删除了 <c>Compose</c> / <c>Digest</c> / <c>CanonicalKey</c> /
/// <c>VoucherFamily</c>，那 12 条断言**要证的对象已不存在**，随之退役。
/// 每条断言的真不变量由谁承担，逐条写在 PR #3278 / S8 的正文里。
///
/// 剩下的两条各自仍有活的承重方：
/// <list type="number">
/// <item><see cref="Voucher_no_column_width_matches_the_policy_constant"/>：
///   <c>ErpVoucherNoPolicy.ColumnMaxLength</c> 仍在 <c>PostJournalVoucherCommandValidator</c>
///   （<c>ErpFinanceCommands.cs</c> 的 <c>RuleFor(x =&gt; x.VoucherNo).MaximumLength(...)</c>）承重，
///   这条把它与 EF 模型里的 <c>voucher_no</c> 列宽绑死 ⇒ 任一单边改动即红；</item>
/// <item><see cref="Widest_production_source_identifier_matches_the_shape_it_is_derived_from"/>：
///   <see cref="WidestAdjustmentSourceIdWidth"/> 是 <c>journal_vouchers.source_no</c> 列宽的下界依据，
///   被 <c>JournalVoucherSourceContractTests.Source_columns_are_nullable_and_wide_enough_for_every_production_source_id</c>
///   当作活断言引用。这条保证那个数不是手抄的，而是从下面那个形状算出来的。</item>
/// </list>
///
/// <b>值域边界（声明放弃了什么，⛔ 别读成完备）</b>：
/// 1. 列宽读的是 <b>EF 模型</b>而不是迁移脚本，模型/迁移漂移不由本类负责。
/// 2. 本类**不**证明「所有凭证号都合法」或「所有凭证号都来自分配器」——
///    分配器短号的形状由 <c>JournalVoucherNoAllocationTests</c> /
///    <c>ConsumerJournalVoucherNumberAllocationTests</c> 钉，落库那一层由真 Postgres 用例钉。
/// 3. EF InMemory 既看不见列宽也看不见唯一索引，本类的绿**不能**读成「落库不会 22001」。
/// </summary>
public sealed class ErpVoucherNoLengthContractTests
{
    /// <summary>
    /// ERP 侧承载上游业务单号的列宽，全部是 100（工单号 / 报工单号 / 移动号 / 工序任务号 / 各类单据号）。
    ///
    /// ⛔ <b>这是手抄数，本类没有把它钉到任何真实列宽上</b>（#3278 / S8 如实登记）。
    /// 它在 <see cref="WidestAdjustmentSourceId"/> 里只负责把 134 那个形状拼出来；
    /// 而 134 本身由 <see cref="Widest_production_source_identifier_matches_the_shape_it_is_derived_from"/>
    /// 对撞看住，⛔ 100 没有。
    /// <b>失效方向</b>：最宽那一支的承重列是**生产者侧** MES 的 <c>operation_task_id</c>（列宽 100），
    /// 把它加宽到 200 时本仓零红（S8 实测），而真实最宽 sourceId 会变成 234、撞 <c>source_no</c> 的 150。
    /// 补不上的结构性原因：本测试程序集**零 MES 项目引用**，Erp 侧同名列是下游副本列、对入站 payload 零约束。
    /// </summary>
    private const int UpstreamNoColumnWidth = 100;

    /// <summary>
    /// <c>CostVariancePosting.PostLateAdjustmentAsync</c> 的 <c>sourceId</c> 在生产侧的**最宽**取值形状。
    ///
    /// **这是复审更正过的读数，别再照抄旧数**：该方法有 **5 个调用点**（不是 3 个），
    /// 覆盖 **7 种** <c>sourceId</c> 形状——
    /// <list type="bullet">
    /// <item><c>WorkOrderCostIntegrationEventHandlers.cs</c> → <c>payload.ReportNo</c>（列宽 100）；</item>
    /// <item>同文件 → <c>item.MovementId</c>（列宽 100）；</item>
    /// <item>同文件 → <c>payload.InventoryMovementId</c>（<c>StockMovementId</c> 是
    ///   <c>IGuidStronglyTypedId</c> ⇒ GUID 文本恒 36）；</item>
    /// <item><c>OperationLaborSettlementIntegrationEventHandlers.cs</c> → <c>{OperationTaskId}-r{rev}</c>；</item>
    /// <item>同文件 → <c>{OperationTaskId}-r{rev}-void</c>；</item>
    /// <item><c>OperationMachineOverheadSettlementIntegrationEventHandlers.cs</c> → <c>machine-{OperationTaskId}-r{rev}</c>；</item>
    /// <item>同文件 → <c>machine-{OperationTaskId}-r{rev}-void</c>。</item>
    /// </list>
    /// <c>OperationTaskId</c> 列宽 100，<c>SettlementRevision</c> 是**单调递增的非负** <c>long</c>
    /// 修订号（十进制最多 19 位；若取到负值则含负号 20 位，宽度 135——本条按非负值域算），
    /// 故最宽形状 <c>machine-{100}-r{19}-void</c> = 8+100+2+19+5 = <b>134</b>。
    ///
    /// ⚠️ <b>这个数今天约束的是 <c>journal_vouchers.source_no</c> 的列宽，⛔ 不再是凭证号</b>：
    /// S5（PR #3480）把「这张凭证是否已记」的判据搬到 <c>(source_type, source_no)</c> 并建了
    /// partial unique index，上面那 7 种 <c>sourceId</c> 是写进 <c>source_no</c> 的值。
    /// </summary>
    private const string WidestAdjustmentSourceIdShape = "machine-{OperationTaskId}-r{SettlementRevision}-void";

    internal const int WidestAdjustmentSourceIdWidth = 134;

    [Fact]
    public void Voucher_no_column_width_matches_the_policy_constant()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var property = model.FindEntityType(typeof(JournalVoucher))!.FindProperty(nameof(JournalVoucher.VoucherNo))!;

        Assert.Equal("voucher_no", property.GetColumnName());
        Assert.Equal(ErpVoucherNoPolicy.ColumnMaxLength, property.GetMaxLength());
    }

    /// <summary>
    /// <see cref="WidestAdjustmentSourceIdWidth"/> 必须等于
    /// <see cref="WidestAdjustmentSourceIdShape"/> 在类型上界下实例化出来的实际长度。
    ///
    /// 为什么需要这条：那个数是 <c>source_no</c> 列宽的下界依据，被另一个类当活断言引用。
    /// 没有这条，134 就是一个手抄的数——而「把一个紧的上界配一条不属于该链路的宽列」
    /// 在这张票上已经复发过三次（158 / 183 / 154，全部是从**别的表的列宽**推出来的）。
    /// <c>long.MaxValue</c> 的十进制位数变了、或那个形状多一段，这条就红。
    /// </summary>
    [Fact]
    public void Widest_production_source_identifier_matches_the_shape_it_is_derived_from()
    {
        Assert.Equal(WidestAdjustmentSourceIdWidth, WidestAdjustmentSourceId().Length);
        Assert.StartsWith("machine-", WidestAdjustmentSourceId(), StringComparison.Ordinal);
        Assert.EndsWith("-void", WidestAdjustmentSourceId(), StringComparison.Ordinal);
        Assert.Contains("machine-", WidestAdjustmentSourceIdShape, StringComparison.Ordinal);
        Assert.EndsWith("-void", WidestAdjustmentSourceIdShape, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>调用方给号的那条入口真的按列宽拒绝</b>——上界取自 <b>EF 模型</b>，⛔ 不取
    /// <c>ErpVoucherNoPolicy.ColumnMaxLength</c>：取常量会让「校验器改成手抄一个别的数」这一格恒绿。
    ///
    /// ⭐ <b>#3278 / S8 新增，补的是一格实测出来的存活变异</b>。S8 的判断是
    /// 「<c>ErpVoucherNoPolicy</c> 退化保留而不是删除，因为 <c>ColumnMaxLength</c> 仍在
    /// <c>PostJournalVoucherCommandValidator</c> 承重」。变异实测（把那条
    /// <c>RuleFor(x =&gt; x.VoucherNo).MaximumLength(...)</c> 整条删掉）改前 <b>全绿存活</b>
    /// ⇒ 那个「仍在承重」的判断当时没有任何断言背书，常量随时可能静默变成死代码。
    /// 这条把承重关系本身写成断言。
    ///
    /// <b>边界口径</b>：恰好等于列宽必须**通过**，多一位必须**被拒**——只测被拒会被
    /// 「上界收得过紧」的实现蒙混过去。⛔ 本条不证明「落库不会 22001」：
    /// 绕开命令直接写 EF 或原生 SQL 都够不着校验器。
    /// </summary>
    [Fact]
    public void The_caller_supplied_voucher_no_is_rejected_beyond_the_column_width()
    {
        int columnWidth;
        using (var dbContext = CreateModelOnlyDbContext())
        {
            columnWidth = dbContext.GetService<IDesignTimeModel>().Model
                .FindEntityType(typeof(JournalVoucher))!
                .FindProperty(nameof(JournalVoucher.VoucherNo))!
                .GetMaxLength()!.Value;
        }

        var validator = new PostJournalVoucherCommandValidator();

        Assert.True(
            validator.Validate(BalancedCommand(new string('N', columnWidth))).IsValid,
            $"恰好等于列宽 {columnWidth} 的凭证号必须通过，否则上界收得比列宽还紧。");
        Assert.False(
            validator.Validate(BalancedCommand(new string('N', columnWidth + 1))).IsValid,
            $"比列宽 {columnWidth} 多一位的凭证号必须被拒。");
    }

    private static PostJournalVoucherCommand BalancedCommand(string voucherNo)
        => new(
            "org-001",
            "env-dev",
            voucherNo,
            new DateOnly(2026, 6, 1),
            [
                new JournalVoucherCommandLine("1401", 10m, 0m, "debit leg"),
                new JournalVoucherCommandLine("2202", 0m, 10m, "credit leg"),
            ]);

    /// <summary><see cref="WidestAdjustmentSourceIdShape"/> 在类型上界下的实例：8+100+2+19+5 = 134。</summary>
    private static string WidestAdjustmentSourceId()
        => "machine-"
            + new string('T', UpstreamNoColumnWidth)
            + "-r"
            + long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "-void";

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
