using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Validation;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #3229：派生凭证号的有效上界是「<c>voucher_no</c> 列宽 − 前缀 − 分隔符 − 其余段」，不是上游单号的列宽。
///
/// 这个类钉住三件事：
/// - <see cref="Voucher_no_column_width_matches_the_policy_constant"/>：列宽从 **EF 模型**
///   （<c>IDesignTimeModel</c>）读，不读代码里的常量 → 单边改 <c>HasMaxLength</c> 即红；
/// - <see cref="Saturated_upstream_identifiers_stay_within_the_column_width"/> 等：拿真实构造入口跑顶格输入，
///   把**它实际产出的长度**与**从 EF 模型读到的列宽**直接对撞；
/// - 撞号面：原样式/摘要式两个值域不相交、不同来源不塌成同号。
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
    /// <summary>票面 #3229 记录的、改前会溢出的最长上游标识长度：工单号列宽 100 + 库存移动 id（GUID 文本）36。</summary>
    private const int WorkOrderIdColumnWidth = 100;

    private const int InventoryMovementIdWidth = 36;

    /// <summary><c>PostLateAdjustmentAsync</c> 的 sourceId 三个调用方里最宽的那个：report_no / movement_id 列宽 100。</summary>
    private const int AdjustmentSourceIdColumnWidth = 100;

    [Fact]
    public void Voucher_no_column_width_matches_the_policy_constant()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var property = model.FindEntityType(typeof(JournalVoucher))!.FindProperty(nameof(JournalVoucher.VoucherNo))!;

        Assert.Equal("voucher_no", property.GetColumnName());
        Assert.Equal(ErpVoucherNoPolicy.ColumnMaxLength, property.GetMaxLength());
    }

    /// <summary>改口径前的构造式（前缀 + 上游单号）在顶格输入下确实超出列宽——这条读数说明缺陷不是假想的。</summary>
    [Fact]
    public void Pre_change_shapes_overflow_the_column_at_saturated_upstream_identifiers()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var columnWidth = model.FindEntityType(typeof(JournalVoucher))!
            .FindProperty(nameof(JournalVoucher.VoucherNo))!
            .GetMaxLength()!.Value;

        var workOrderId = new string('W', WorkOrderIdColumnWidth);
        var movementId = new string('M', InventoryMovementIdWidth);
        var sourceId = new string('S', AdjustmentSourceIdColumnWidth);
        var payableNo = new string('P', WorkOrderIdColumnWidth);

        Assert.Equal(144, $"JV-WOC-{workOrderId}-{movementId}".Length);
        Assert.Equal(212, $"JV-WOC-ADJ-{workOrderId}-{sourceId}".Length);
        Assert.Equal(106, $"JV-AP-{payableNo}".Length);
        Assert.True($"JV-WOC-{workOrderId}-{movementId}".Length > columnWidth);
        Assert.True($"JV-WOC-ADJ-{workOrderId}-{sourceId}".Length > columnWidth);
        Assert.True($"JV-AP-{payableNo}".Length > columnWidth);
    }

    /// <summary>顶格输入下，真实构造入口产出的长度必须塞得进从 EF 模型读到的列宽——本条不经过任何策略常量。</summary>
    [Theory]
    [InlineData("WOC", WorkOrderIdColumnWidth, InventoryMovementIdWidth)]
    [InlineData("WOCADJ", WorkOrderIdColumnWidth, AdjustmentSourceIdColumnWidth)]
    public void Saturated_upstream_identifiers_stay_within_the_column_width(string family, int firstWidth, int secondWidth)
    {
        using var dbContext = CreateModelOnlyDbContext();
        var columnWidth = dbContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(JournalVoucher))!
            .FindProperty(nameof(JournalVoucher.VoucherNo))!
            .GetMaxLength()!.Value;

        var voucherNo = ErpVoucherNoPolicy.Compose(family, new string('A', firstWidth), new string('B', secondWidth));

        Assert.True(
            voucherNo.Length <= columnWidth,
            $"族 {family} 的顶格凭证号长度 {voucherNo.Length} 超出列宽 {columnWidth}。");
    }

    [Theory]
    [InlineData("GRIR")]
    [InlineData("PRTN")]
    [InlineData("CN")]
    [InlineData("AP")]
    [InlineData("AR")]
    [InlineData("COST")]
    public void Saturated_single_segment_families_stay_within_the_column_width(string family)
    {
        using var dbContext = CreateModelOnlyDbContext();
        var columnWidth = dbContext.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(JournalVoucher))!
            .FindProperty(nameof(JournalVoucher.VoucherNo))!
            .GetMaxLength()!.Value;

        var voucherNo = ErpVoucherNoPolicy.Compose(family, new string('N', WorkOrderIdColumnWidth));

        Assert.True(
            voucherNo.Length <= columnWidth,
            $"族 {family} 的顶格凭证号长度 {voucherNo.Length} 超出列宽 {columnWidth}。");
    }

    /// <summary>合得下时必须与改前逐字节相同，否则存量行和「按号查重」会集体失配。</summary>
    [Fact]
    public void Short_inputs_keep_the_pre_change_voucher_numbers_byte_for_byte()
    {
        Assert.Equal("JV-WOC-WO-0001-MOVE-0001", ErpVoucherNoPolicy.Compose("WOC", "WO-0001", "MOVE-0001"));
        Assert.Equal("JV-GRIR-GR-0001", ErpVoucherNoPolicy.Compose("GRIR", "GR-0001"));
        Assert.Equal("JV-PRTN-PRTN-0001", ErpVoucherNoPolicy.Compose("PRTN", "PRTN-0001"));
        Assert.Equal("JV-CN-CN-0001", ErpVoucherNoPolicy.Compose("CN", "CN-0001"));
        Assert.Equal("JV-AP-AP-0001", ErpVoucherNoPolicy.Compose("AP", "AP-0001"));
        Assert.Equal("JV-AR-AR-0001", ErpVoucherNoPolicy.Compose("AR", "AR-0001"));
        Assert.Equal("JV-COST-COST-0001", ErpVoucherNoPolicy.Compose("COST", "COST-0001"));
    }

    /// <summary>恰好等于列宽的输入仍走原样式；多一位就换形态——上界那一位的行为要能读出来。</summary>
    [Fact]
    public void The_boundary_between_raw_and_digest_sits_exactly_at_the_column_width()
    {
        var prefixLength = ErpVoucherNoPolicy.GlobalPrefix.Length + "AP".Length + ErpVoucherNoPolicy.RawSeparator.Length;
        var exactFit = new string('N', ErpVoucherNoPolicy.ColumnMaxLength - prefixLength);
        var oneOver = exactFit + "N";

        var fitted = ErpVoucherNoPolicy.Compose("AP", exactFit);
        var overflowed = ErpVoucherNoPolicy.Compose("AP", oneOver);

        Assert.Equal(ErpVoucherNoPolicy.ColumnMaxLength, fitted.Length);
        Assert.Equal($"JV-AP-{exactFit}", fitted);
        Assert.Equal(ErpVoucherNoPolicy.Digest("AP", oneOver), overflowed);
        Assert.True(overflowed.Length < ErpVoucherNoPolicy.ColumnMaxLength);
    }

    /// <summary>两种形态在族名后那一位分别是 <c>-</c> 与 <c>~</c>，值域不相交。</summary>
    [Fact]
    public void Raw_and_digest_shapes_occupy_disjoint_value_ranges()
    {
        var raw = ErpVoucherNoPolicy.Compose("AP", "AP-0001");
        var digest = ErpVoucherNoPolicy.Compose("AP", new string('N', ErpVoucherNoPolicy.ColumnMaxLength));

        Assert.StartsWith("JV-AP" + ErpVoucherNoPolicy.RawSeparator, raw, StringComparison.Ordinal);
        Assert.StartsWith("JV-AP" + ErpVoucherNoPolicy.DigestMarker, digest, StringComparison.Ordinal);
        Assert.NotEqual(raw, digest);
    }

    /// <summary>
    /// 改前 <c>JV-WOC-</c> 是 <c>JV-WOC-ADJ-</c> 的前缀，工单号形如 <c>ADJ-x</c> 时两族撞号；
    /// 族名字符集不含 <c>-</c> 之后这条塌陷由构造关闭，不靠名单。
    /// </summary>
    [Fact]
    public void Family_names_cannot_contain_the_separator_so_families_stay_prefix_free()
    {
        Assert.Throws<ArgumentException>(() => ErpVoucherNoPolicy.Compose("WOC-ADJ", "WO-0001", "RPT-0001"));
        Assert.NotEqual(
            ErpVoucherNoPolicy.Compose("WOC", "ADJ-WO-0001", "RPT-0001"),
            ErpVoucherNoPolicy.Compose("WOCADJ", "WO-0001", "RPT-0001"));
    }

    /// <summary>摘要输入带长度前缀，故不同的段划分不会拼成同一个输入——这是摘要式不塌号的那一半论证。</summary>
    [Fact]
    public void Digest_inputs_are_length_prefixed_so_different_segment_splits_stay_distinct()
    {
        var head = new string('X', 60);
        var tail = new string('Y', 60);

        var left = ErpVoucherNoPolicy.Compose("WOC", head + "-" + tail, "Z");
        var right = ErpVoucherNoPolicy.Compose("WOC", head, tail + "-Z");

        Assert.NotEqual(left, right);
        Assert.Equal(ErpVoucherNoPolicy.Digest("WOC", head + "-" + tail, "Z"), left);
        Assert.Equal(ErpVoucherNoPolicy.Digest("WOC", head, tail + "-Z"), right);
    }

    /// <summary>同一来源必须稳定地得到同一个凭证号，否则重放会记出第二张凭证。</summary>
    [Fact]
    public void The_same_source_always_composes_to_the_same_voucher_number()
    {
        var workOrderId = new string('W', WorkOrderIdColumnWidth);
        var movementId = Guid.NewGuid().ToString();

        Assert.Equal(
            ErpVoucherNoPolicy.Compose("WOC", workOrderId, movementId),
            ErpVoucherNoPolicy.Compose("WOC", workOrderId, movementId));
    }

    /// <summary>族名过长时就地抛业务异常，而不是把越界值送进库换一个 22001。</summary>
    [Fact]
    public void An_over_long_family_is_rejected_in_place_instead_of_overflowing_the_column()
    {
        Assert.Equal(
            ErpVoucherNoPolicy.ColumnMaxLength
                - ErpVoucherNoPolicy.GlobalPrefix.Length
                - ErpVoucherNoPolicy.DigestMarker.Length
                - ErpVoucherNoPolicy.DigestLength,
            ErpVoucherNoPolicy.MaxFamilyLength);
        Assert.Throws<KnownException>(() =>
            ErpVoucherNoPolicy.Compose(new string('F', ErpVoucherNoPolicy.MaxFamilyLength + 1), "AP-0001"));
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
