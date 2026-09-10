using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Erp.Domain;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Validation;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #3288：Erp 编码幂等键的有效上界是「<c>code_idempotency_keys.idempotency_key</c> 列宽
/// − handler 落库前追加的最长后缀」，不是列宽本身。
///
/// 这个类钉住四件事，而不是只钉住某一个数字：
/// <list type="bullet">
/// <item><see cref="Code_idempotency_key_column_width_matches_the_policy_constant"/>：列宽从 **EF 模型
/// 闭集枚举**（<c>IDesignTimeModel</c>）读，不读代码里的常量 → 单边改 <c>HasMaxLength</c>
/// 或新增一张带该列的表都会红；</item>
/// <item><see cref="Every_suffixing_write_face_rejects_a_base_key_that_would_overflow_the_column"/>：
/// 逐个写面跑**真实校验器**，上界那一位放行、+1 位拒绝；</item>
/// <item><see cref="Composed_keys_of_a_max_length_base_key_exactly_saturate_the_column"/>：拿真实
/// <c>Compose</c> 跑「恰好顶到上界」的基础键，把**实际落库的键长**与**从 EF 模型读到的列宽**直接对撞
/// → 这条不经过任何策略常量，是三条腿里唯一的真对撞；把某个校验器上界手抄回 150 时红的就是它；</item>
/// <item><see cref="Optional_idempotency_keys_stay_optional"/>：把护栏「只加长度规则、不加
/// <c>NotEmpty()</c>」当断言写死——后来人顺手补 <c>NotEmpty()</c> 会红，而不是静默把
/// 「不传幂等键」这条今天合法的路径变成 400（那属 #3287 的语义决定）。</item>
/// </list>
///
/// **值域边界（声明放弃了什么，别读成完备）**：
/// <list type="number">
/// <item>列宽读的是 **EF 模型**而不是迁移脚本。只改迁移不改模型（或反之）本类抓不到，
/// 那是「模型/迁移漂移」另一类护栏的职责。</item>
/// <item>**本类不证明「所有拼接都走 <see cref="ErpCodingIdempotencyKeyPolicy.Compose"/>」**。
/// #3176 / PR #3214 三轮实证这类源码文本扫描不收敛并已按裁定移除；#3231 另已实测「换成不可拼接
/// 的包装类型」只关得掉 <c>+</c>。绕开 <c>Compose</c> 直接 <c>$"{key}:rfq"</c> 仍然编译得过，
/// 且**不会红**。别把本类的绿读成「拼接方式已被看住」。</item>
/// <item>本类枚举的写面是**票面点名的那三处**（#3288 逐位点判定表），不是「Erp 里所有会把值写进
/// <c>idempotency_key</c> 的路径」。已知**不在本类射程内**的同列写入还有
/// <c>ErpReturnIntegrationEventHandlers</c> / <c>WmsInboundOrderCompleted…</c> 等消费者的
/// <c>$"{ConsumerName}:{payload.IdempotencyKey}"</c> **前缀**式构造——那些是集成事件路径、
/// 键由发布侧 converter 生成而非调用方直接可控，与本票的「调用方可控的合法 API 输入」不同族，
/// 故不在本 PR 一并处理，也**不由本类看守**。</item>
/// <item>「顶格键在真库里到底炸不炸 22001」由真 Postgres 验证（本 PR 正文给了一次性容器读数）；
/// EF InMemory 与 model-only 上下文都看不见列宽，本类的绿**不能**读成「落库不会 22001」。</item>
/// </list>
/// </summary>
public sealed class ErpCodingIdempotencyKeyLengthContractTests
{
    /// <summary>
    /// <c>idempotency_key</c> 这一列在 Erp EF 模型里的**具名豁免**：该表来自共享的
    /// <c>Nerv.IIP.Messaging.CAP</c> 死信箱，列宽 500、不由本策略管辖。
    /// 豁免必须具名且被计数封闭——「名单里没有」不构成豁免。
    /// </summary>
    private const string DeadLetterEntityName = "Nerv.IIP.Messaging.CAP.IntegrationEventDeadLetter";

    private const int DeadLetterColumnMaxLength = 500;

    /// <summary>
    /// EF 模型里 <c>idempotency_key</c> 列的总数（含上面那条具名豁免）。
    /// 这条计数是**闭集下界**：新增一张带该列的表、或改列名让值域塌成空集，都会红——
    /// 否则 <c>Assert.All</c> 对空集恒真，护栏会静默缴械。
    /// </summary>
    private const int IdempotencyKeyColumnCount = 2;

    /// <summary>改前三处写面各自的上界（无规则＝整列宽 150；位点 3 手抄 150）。</summary>
    private const int PreChangeBound = 150;

    [Fact]
    public void Code_idempotency_key_column_width_matches_the_policy_constant()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        // 从 EF 模型**闭集枚举**，不是手写白名单：新增一张带 idempotency_key 的表会自动进值域。
        var columns = IdempotencyKeyColumns(model);
        Assert.Equal(IdempotencyKeyColumnCount, columns.Length);

        var exempted = columns
            .Where(property => property.DeclaringType.Name == DeadLetterEntityName)
            .ToArray();
        Assert.Equal(DeadLetterColumnMaxLength, Assert.Single(exempted).GetMaxLength());

        var governed = columns.Except(exempted).ToArray();
        Assert.Single(governed);
        // 豁免 + 受管 == 闭集总数：不许出现「既不在豁免名单、也没被受管断言覆盖」的第三类。
        Assert.Equal(IdempotencyKeyColumnCount, governed.Length + exempted.Length);
        Assert.All(
            governed,
            property => Assert.Equal(
                ErpCodingIdempotencyKeyPolicy.ColumnMaxLength,
                property.GetMaxLength()));
    }

    /// <summary>
    /// 每个写面的上界都必须是**从列宽减去它自己的后缀算出来的**，不是手抄的数字。
    /// 这条把「上界 + 最长后缀 == 列宽」写成等式：任何一侧被单边改动都会红。
    /// </summary>
    [Theory]
    [MemberData(nameof(SuffixingWriteFaces))]
    public void Every_write_face_bound_is_derived_from_the_column_width_minus_its_own_suffix(
        string faceName,
        int bound,
        string suffix)
    {
        Assert.NotEmpty(faceName);
        Assert.Equal(ErpCodingIdempotencyKeyPolicy.ColumnMaxLength, bound + suffix.Length);
        Assert.True(
            bound < ErpCodingIdempotencyKeyPolicy.ColumnMaxLength,
            $"写面 {faceName} 的上界 {bound} 没有低于列宽，说明后缀没被扣掉。");
    }

    /// <summary>
    /// 改前的上界（位点 1、2 无规则＝整列宽放行，位点 3 手抄 150）在顶格输入下确实超出列宽
    /// ——这条读数说明缺陷不是假想的，也把「行为变化只发生在 151–166 这一段」的边界写死。
    /// </summary>
    [Theory]
    [MemberData(nameof(SuffixingWriteFaces))]
    public void The_pre_change_bound_overflows_the_column_for_every_write_face(
        string faceName,
        int bound,
        string suffix)
    {
        var columnWidth = ColumnWidthFromModel();

        Assert.True(
            PreChangeBound + suffix.Length > columnWidth,
            $"写面 {faceName} 的改前上界 {PreChangeBound} 加后缀 {suffix} 未超列宽 {columnWidth}，缺陷描述有误。");
        Assert.True(bound < PreChangeBound, $"写面 {faceName} 的上界没有收紧。");
        Assert.Equal(PreChangeBound - bound, suffix.Length);
    }

    /// <summary>
    /// 三个写面各跑**真实校验器**：上界那一位放行、+1 位拒绝，且拒绝的确实是幂等键那条规则。
    /// </summary>
    [Fact]
    public void Every_suffixing_write_face_rejects_a_base_key_that_would_overflow_the_column()
    {
        // 位点 1：采购申请转采购订单（业务网关入口；改前本命令完全没有幂等键长度规则）。
        AssertBoundary(
            ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler.BaseIdempotencyKeyMaxLength,
            key => new ConvertPurchaseRequisitionsToPurchaseOrderCommandValidator()
                .Validate(ConvertCommand(key)));

        // 位点 2：登记供应商发票（Erp 自有端点；改前本命令完全没有幂等键长度规则）。
        AssertBoundary(
            RecordSupplierInvoiceCommandHandler.BaseIdempotencyKeyMaxLength,
            key => new RecordSupplierInvoiceCommandValidator()
                .Validate(RecordSupplierInvoiceCommand(key)));

        // 位点 3：解除发票付款冻结（Erp 自有端点；改前手抄 MaximumLength(150)＝列宽本身）。
        AssertBoundary(
            ReleaseSupplierInvoicePaymentHoldCommandHandler.BaseIdempotencyKeyMaxLength,
            key => new ReleaseSupplierInvoicePaymentHoldCommandValidator()
                .Validate(ReleasePaymentHoldCommand(key)));
    }

    /// <summary>
    /// 三条腿里唯一的**真对撞**：拿真实 <see cref="ErpCodingIdempotencyKeyPolicy.Compose"/>
    /// 跑「恰好顶到该写面上界」的基础键，把它**实际产出的键长**与**从 EF 模型读到的列宽**直接比。
    /// 这条不经过 <see cref="ErpCodingIdempotencyKeyPolicy.ColumnMaxLength"/>，
    /// 所以把任何一个校验器上界手抄回 150 时红的就是它。
    /// </summary>
    [Theory]
    [MemberData(nameof(SuffixingWriteFaces))]
    public void Composed_keys_of_a_max_length_base_key_exactly_saturate_the_column(
        string faceName,
        int bound,
        string suffix)
    {
        var columnWidth = ColumnWidthFromModel();
        var saturatedBaseKey = new string('k', bound);

        var composed = ErpCodingIdempotencyKeyPolicy.Compose(saturatedBaseKey, suffix);

        Assert.Equal(saturatedBaseKey + suffix, composed);
        Assert.Equal(columnWidth, composed.Length);
        Assert.True(
            composed.Length <= columnWidth,
            $"写面 {faceName} 的顶格键长度 {composed.Length} 超出列宽 {columnWidth}。");
    }

    /// <summary>
    /// <see cref="ErpCodingIdempotencyKeyPolicy.Compose"/> 越界就地拒绝，且**绝不截断**
    /// ——截断会把仅末几位不同的两个键折叠成同一个，那会让两次不同的创建请求换回同一个业务号。
    /// 这条覆盖的是绕过校验器的调用方（内部直接构造命令）。
    /// </summary>
    [Fact]
    public void Compose_rejects_an_overflowing_key_instead_of_sending_it_to_the_database()
    {
        var suffix = ErpCodingIdempotencyKeyPolicy.AccountPayableSuffix;
        var bound = ErpCodingIdempotencyKeyPolicy.BaseMaxLengthFor(suffix);

        var fitted = ErpCodingIdempotencyKeyPolicy.Compose(new string('k', bound), suffix);
        Assert.Equal(ErpCodingIdempotencyKeyPolicy.ColumnMaxLength, fitted.Length);

        var exception = Assert.Throws<KnownException>(
            () => ErpCodingIdempotencyKeyPolicy.Compose(new string('k', bound + 1), suffix));
        Assert.Contains("幂等键", exception.Message, StringComparison.Ordinal);

        // 仅末位不同的两个顶格键必须仍然互异（反截断）。
        var left = ErpCodingIdempotencyKeyPolicy.Compose(new string('k', bound - 1) + "a", suffix);
        var right = ErpCodingIdempotencyKeyPolicy.Compose(new string('k', bound - 1) + "b", suffix);
        Assert.NotEqual(left, right);
    }

    /// <summary>
    /// <see cref="ErpCodingIdempotencyKeyPolicy.BaseMaxLengthFor"/> 取的是**最长**后缀，
    /// 不是第一个、也不是最短的那个——同一条命令的键被多个写面拼接时，有效上界取最小值。
    /// 没有后缀时回落到整列宽。
    /// </summary>
    [Fact]
    public void The_bound_falls_back_to_the_column_width_and_otherwise_subtracts_the_longest_suffix()
    {
        Assert.Equal(
            ErpCodingIdempotencyKeyPolicy.ColumnMaxLength,
            ErpCodingIdempotencyKeyPolicy.BaseMaxLengthFor());
        Assert.Equal(
            ErpCodingIdempotencyKeyPolicy.ColumnMaxLength - ErpCodingIdempotencyKeyPolicy.AccountPayableSuffix.Length,
            ErpCodingIdempotencyKeyPolicy.BaseMaxLengthFor(
                ErpCodingIdempotencyKeyPolicy.RequestForQuotationSuffix,
                ErpCodingIdempotencyKeyPolicy.AccountPayableSuffix));
        Assert.Equal(
            ErpCodingIdempotencyKeyPolicy.ColumnMaxLength - ErpCodingIdempotencyKeyPolicy.AccountPayableSuffix.Length,
            ErpCodingIdempotencyKeyPolicy.BaseMaxLengthFor(
                ErpCodingIdempotencyKeyPolicy.AccountPayableSuffix,
                ErpCodingIdempotencyKeyPolicy.RequestForQuotationSuffix));
        Assert.True(
            ErpCodingIdempotencyKeyPolicy.AccountPayableSuffix.Length
                > ErpCodingIdempotencyKeyPolicy.RequestForQuotationSuffix.Length,
            "两个后缀等长时上一条断言退化成同义反复，需要换取值。");
    }

    /// <summary>
    /// **护栏当断言写**（#3288 实施护栏 1）：本票只加长度规则，位点 1、2 的
    /// 「不传幂等键」今天合法，必须继续合法；位点 3 的 <c>NotEmpty</c> 是**改前就有的**，
    /// 也必须原样保留。后来人顺手给位点 1、2 补 <c>NotEmpty()</c> 会红——
    /// 「幂等键该不该可选」是 #3287 要答的语义决定，不许被一次长度收紧顺手改掉。
    /// </summary>
    [Fact]
    public void Optional_idempotency_keys_stay_optional()
    {
        Assert.True(
            new ConvertPurchaseRequisitionsToPurchaseOrderCommandValidator()
                .Validate(ConvertCommand(null)).IsValid,
            "位点 1 的幂等键改前可不传，长度收紧不得把它变成必填。");
        Assert.True(
            new RecordSupplierInvoiceCommandValidator()
                .Validate(RecordSupplierInvoiceCommand(null)).IsValid,
            "位点 2 的幂等键改前可不传，长度收紧不得把它变成必填。");

        // 位点 3 的命令参数本身是非空 string，NotEmpty 是改前既有规则，空串仍须拒。
        Assert.False(
            new ReleaseSupplierInvoicePaymentHoldCommandValidator()
                .Validate(ReleasePaymentHoldCommand(string.Empty)).IsValid,
            "位点 3 改前就有 NotEmpty，不得在本票里被弱化。");
    }

    public static TheoryData<string, int, string> SuffixingWriteFaces()
    {
        return new TheoryData<string, int, string>
        {
            {
                nameof(ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler),
                ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler.BaseIdempotencyKeyMaxLength,
                ErpCodingIdempotencyKeyPolicy.RequestForQuotationSuffix
            },
            {
                nameof(RecordSupplierInvoiceCommandHandler),
                RecordSupplierInvoiceCommandHandler.BaseIdempotencyKeyMaxLength,
                ErpCodingIdempotencyKeyPolicy.AccountPayableSuffix
            },
            {
                nameof(ReleaseSupplierInvoicePaymentHoldCommandHandler),
                ReleaseSupplierInvoicePaymentHoldCommandHandler.BaseIdempotencyKeyMaxLength,
                ErpCodingIdempotencyKeyPolicy.AccountPayableSuffix
            },
        };
    }

    private static void AssertBoundary(
        int bound,
        Func<string, FluentValidation.Results.ValidationResult> validate)
    {
        var atBound = validate(new string('k', bound));
        Assert.True(atBound.IsValid, $"上界 {bound} 那一位必须放行，实际错误：{string.Join("；", atBound.Errors)}");

        var overBound = validate(new string('k', bound + 1));
        Assert.False(overBound.IsValid, $"上界 {bound} 加一位必须拒绝。");
        Assert.Contains(
            overBound.Errors,
            error => string.Equals(error.PropertyName, "IdempotencyKey", StringComparison.Ordinal));
    }

    private static ConvertPurchaseRequisitionsToPurchaseOrderCommand ConvertCommand(string? idempotencyKey)
    {
        return new ConvertPurchaseRequisitionsToPurchaseOrderCommand(
            "org-001",
            "env-dev",
            ["PR-2026-0001"],
            IdempotencyKey: idempotencyKey);
    }

    private static RecordSupplierInvoiceCommand RecordSupplierInvoiceCommand(string? idempotencyKey)
    {
        return new RecordSupplierInvoiceCommand(
            "org-001",
            "env-dev",
            null,
            "PO-2026-0001",
            "GR-2026-0001",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 1),
            "CNY",
            0.05m,
            0.05m,
            [new SupplierInvoiceCommandLine("1", "1", 10m, 100m)],
            IdempotencyKey: idempotencyKey);
    }

    private static ReleaseSupplierInvoicePaymentHoldCommand ReleasePaymentHoldCommand(string idempotencyKey)
    {
        return new ReleaseSupplierInvoicePaymentHoldCommand(
            "org-001",
            "env-dev",
            "SI-2026-0001",
            null,
            idempotencyKey);
    }

    private static IProperty[] IdempotencyKeyColumns(IModel model)
    {
        return model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => string.Equals(property.GetColumnName(), "idempotency_key", StringComparison.Ordinal))
            .ToArray();
    }

    private static int ColumnWidthFromModel()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var governed = IdempotencyKeyColumns(model)
            .Where(property => property.DeclaringType.Name != DeadLetterEntityName)
            .ToArray();
        return Assert.Single(governed).GetMaxLength()!.Value;
    }

    private static ApplicationDbContext CreateModelOnlyDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=nerv_iip_coding_idempotency_key_contract;Username=nerv;Password=nerv",
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
