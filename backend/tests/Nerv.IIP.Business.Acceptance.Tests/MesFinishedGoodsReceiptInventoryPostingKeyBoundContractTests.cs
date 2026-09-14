using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Validators;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;

using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using InventoryStockMovementAggregate = Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// 完工入库过账幂等键与其**跨服务**承载列之间的机器可验关系（#3332）。
/// </summary>
/// <remarks>
/// <para><b>被证的真不变量</b>：<c>FinishedGoodsReceiptInventoryPostingKey</c> 在 Mes 侧
/// **任何合法输入**下产出的键，都装得进 Inventory 侧最窄的那条承载列。
/// 两侧的数值全部在运行时从各自的 EF 模型 / 校验器读出，**本类不手抄任何长度数字**：
/// Mes 三段作用域取 <c>finished_goods_receipt_requests</c> 的列宽，
/// 调用方原始键取重投命令校验器的上界，Inventory 侧取承载列列宽的最小值（#3281 判据）。</para>
///
/// <para><b>为什么这条断言住在 Acceptance</b>：键在 Mes.Domain 构造、在 Inventory 落库，
/// 两个服务互不引用，只有测试侧能把两个 EF 模型放在一起对撞。
/// ⚠️ <b>能同时看到两侧的测试程序集不止一个</b>（按「同一个 <c>*.Tests.csproj</c> 里直接
/// <c>ProjectReference</c> 同时出现 Mes 与 Inventory」这个面枚举，今天是 3 个：本项目、
/// <c>Nerv.IIP.FacadeCoverage.Tests</c>、<c>Nerv.IIP.Business.Performance.Tests</c>；
/// 传递引用不计入这个面）。选本项目是因为另两个各有职责——一个是门面覆盖架构测试、
/// 一个是性能测试，都不该承载跨服务契约。**理由是职责，不是「唯一」。**
/// Mes 侧的 <c>FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength</c> 是一份**跨服务副本**，
/// 由本类与 Inventory 真模型对撞钉住，任一单边改动即红。</para>
///
/// <para><b>承载列登记表是手写链接，数值不是</b>：<see cref="InventoryCarrierColumns"/> 登记的是
/// <c>(实体类型, 属性名)</c>，列宽运行时从 Inventory EF 模型读；解析不到就红
/// （<see cref="Every_declared_inventory_carrier_column_resolves"/>），
/// 所以改名或删列不会静默通过。</para>
///
/// <para><b>本类不证明什么</b>：</para>
/// <list type="number">
/// <item>不证明登记表**穷举**了所有承载该键的列。登记依据是逐跳实读消费侧写入点
/// （<c>InventoryMovementRequestedIntegrationEventHandlerForPostingMovement</c> →
/// <c>PostStockMovementCommand</c> 落 <c>stock_movements</c>；同一 handler 的授权待定分支落
/// <c>authority_resolution_pending_audits</c>）。新增一个消费侧写入点不会让本类报红。</item>
/// <item>**故意不登记** <c>integration_event_dead_letters.idempotency_key</c>（500）：
/// 它的写入端 <c>IntegrationEventDeadLetter</c> 走 <c>TruncateOptional</c> **截断**写入，
/// 截断列不构成上界，登记它等于登记一个假权威。</item>
/// <item>不证明这把键在 Inventory 再次被拼进 <c>StockMovementPosted</c> 信封键之后仍装得下——
/// 那是同族的**下一跳**溢出（<c>EventIds.Idempotency</c> 是纯拼接，最坏超过下游
/// <c>processed_integration_events</c> 的 512），已另行立票，不在本类射程。</item>
/// <item>不证明下游那三处按字面前缀 <c>StartsWith</c> 的路由在新形态下仍成立——
/// 那由 <c>FinishedGoodsReceiptInventoryPostingKeyTests</c> 的「每种形态都带字面前缀」
/// 与各服务自己的用例分别承担。</item>
/// </list>
/// </remarks>
public sealed class MesFinishedGoodsReceiptInventoryPostingKeyBoundContractTests
{
    private const string IdempotencyKeyPropertyName = "IdempotencyKey";

    /// <summary>
    /// Inventory 侧原样存放该键的列。**登记前已实读写入点确认落库的就是入参本身**
    /// （不是 #3290 那种哈希派生的幽灵权威）。
    /// </summary>
    private static readonly CarrierColumn[] InventoryCarrierColumns =
    [
        new(typeof(InventoryStockMovementAggregate.StockMovement), IdempotencyKeyPropertyName, "inventory.stock_movements"),
        new(typeof(InventoryAuthorityResolutionPendingAudit), IdempotencyKeyPropertyName, "inventory.authority_resolution_pending_audits"),
    ];

    /// <summary>Mes 侧构成作用域段的三列。它们的列宽就是作用域段的最坏取值。</summary>
    private static readonly string[] ScopeProperties =
    [
        nameof(FinishedGoodsReceiptRequest.OrganizationId),
        nameof(FinishedGoodsReceiptRequest.EnvironmentId),
        nameof(FinishedGoodsReceiptRequest.RequestNo),
    ];

    [Fact]
    public void Every_declared_inventory_carrier_column_resolves()
    {
        using var inventory = InventoryModel();

        var unresolved = InventoryCarrierColumns
            .Where(column => column.Resolve(inventory) is null)
            .Select(column => column.ToString())
            .ToArray();

        Assert.True(
            InventoryCarrierColumns.Length > 0 && unresolved.Length == 0,
            $"承载列登记表解析失败（{unresolved.Length}）：{string.Join(", ", unresolved)}");
    }

    [Fact]
    public void Domain_constant_equals_the_narrowest_inventory_carrier_column()
    {
        var narrowest = NarrowestCarrierColumnWidth();
        Assert.True(
            narrowest == FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength,
            $"Mes 侧跨服务副本 ColumnMaxLength = {FinishedGoodsReceiptInventoryPostingKey.ColumnMaxLength}，"
            + $"而 Inventory 最窄承载列 = {narrowest}，两侧已漂移。");
    }

    /// <summary>
    /// 最坏情况：作用域三段各自取到 Mes 列宽上限，调用方原始键取到重投命令校验器上限，
    /// 首次过账键与重投键都必须装得进 Inventory 最窄的承载列。
    /// </summary>
    [Fact]
    public void Worst_case_derived_keys_fit_the_narrowest_inventory_carrier_column()
    {
        var columnWidth = NarrowestCarrierColumnWidth();
        var scope = WorstCaseScopeSegments();
        var callerKeyLength = MaximumRetryIdempotencyKeyLength();

        Assert.True(callerKeyLength > 0, "重投命令校验器没有解析出幂等键长度上界，本条断言会退化成空转。");

        var baseKey = FinishedGoodsReceiptInventoryPostingKey.Build(scope[0], scope[1], scope[2]);
        Assert.True(
            baseKey.Length <= columnWidth,
            $"最坏首次过账键长度 {baseKey.Length} > 承载列 {columnWidth}：{baseKey}");

        var retryKey = FinishedGoodsReceiptInventoryPostingKey.BuildRetry(
            scope[0],
            scope[1],
            scope[2],
            new string('k', callerKeyLength));
        Assert.True(
            retryKey.Length <= columnWidth,
            $"最坏重投键长度 {retryKey.Length} > 承载列 {columnWidth}：{retryKey}");
    }

    /// <summary>
    /// 把最坏情况下产出的两种键**真的送进 Inventory 自己的命令校验器**。
    /// </summary>
    /// <remarks>
    /// 这一条与上一条不是同一份鉴别力：上一条只量长度，本条走
    /// <c>PostStockMovementCommandValidator</c> 的 <c>RequiredInventoryCode</c>，
    /// 因此同时检验**字符集**——回落形态用 base64url（<c>[A-Za-z0-9-_]</c>），
    /// 若哪天换成标准 base64（含 <c>+</c> <c>/</c>）或别的编码，长度照样合格而这一条会红。
    /// 校验器的上界与字符集都从 Inventory 侧读，本类**不复制那条正则**。
    /// </remarks>
    [Fact]
    public void Worst_case_derived_keys_pass_the_inventory_command_validator()
    {
        var scope = WorstCaseScopeSegments();
        var callerKeyLength = MaximumRetryIdempotencyKeyLength();
        var validator = new PostStockMovementCommandValidator();

        foreach (var key in new[]
        {
            FinishedGoodsReceiptInventoryPostingKey.Build(scope[0], scope[1], scope[2]),
            FinishedGoodsReceiptInventoryPostingKey.BuildRetry(scope[0], scope[1], scope[2], new string('k', callerKeyLength)),
        })
        {
            // PropertyName 用 OrdinalIgnoreCase 比对：app.UseFastEndpoints(...) 启动时会把
            // ValidatorOptions.Global.PropertyNameResolver 换成 camelCase 解析器且不还原，本程序集里
            // 有用例会启动 WebApplicationFactory，之后这里拿到的就是 camelCase 名（#3342）。
            // 这一处的危险形态与 #3342 其余位点不同：下面断言的是「没有落在这条规则上的失败」，
            // 过滤器一旦对不上就退化成**恒空过滤 + 恒真断言**（空转），永远不会红——
            // 即 #3318 那条「断言还在跑，但它要证的事已不存在」。
            // 不能改断 ErrorMessage：Inventory 的 RequiredInventoryCode 里 WithMessage 模板是
            // "{PropertyName} may only contain ..."，含 {PropertyName} 占位符，NotEmpty/MaximumLength
            // 更是默认文案，文案本身就随同一个解析器漂移。
            var failures = validator.Validate(MovementCommandCarrying(scope, key)).Errors
                .Where(error => string.Equals(error.PropertyName, IdempotencyKeyPropertyName, StringComparison.OrdinalIgnoreCase))
                .Select(error => error.ErrorMessage)
                .ToArray();

            Assert.True(
                failures.Length == 0,
                $"派生键被 Inventory 命令校验器拒绝（{key.Length} 字符）：{key}\n{string.Join("\n", failures)}");
        }
    }

    private static PostStockMovementCommand MovementCommandCarrying(string[] scope, string idempotencyKey) =>
        new(
            OrganizationId: scope[0],
            EnvironmentId: scope[1],
            MovementType: "inbound",
            SourceService: "business-mes",
            SourceDocumentId: scope[2],
            SourceDocumentLineId: "WO-001",
            IdempotencyKey: idempotencyKey,
            SkuCode: "FG-001",
            UomCode: "ea",
            SiteCode: "SITE-001",
            LocationCode: "WH-WB-FG-01",
            LotNo: null,
            SerialNo: null,
            QualityStatus: "unrestricted",
            OwnerType: "production",
            OwnerId: null,
            Quantity: 1m);

    /// <summary>
    /// 改动前的形状留一条回归读数：**纯拼接**的最坏长度远超承载列，
    /// 也就是说本位点不存在任何正的「列宽 − 最长附加段」上界可供声明（#3324 把它移出登记表的原因）。
    /// 这条不是断言实现，是把「为什么必须回落」钉在可执行的读数上。
    /// </summary>
    [Fact]
    public void Plain_concatenation_would_not_fit_even_with_an_empty_caller_key()
    {
        var columnWidth = NarrowestCarrierColumnWidth();
        var scope = WorstCaseScopeSegments();
        var concatenated = $"{FinishedGoodsReceiptInventoryPostingKey.Prefix}{scope[0]}:{scope[1]}:{scope[2]}";

        Assert.True(
            concatenated.Length > columnWidth,
            $"纯拼接最坏长度 {concatenated.Length} 已不超过承载列 {columnWidth}，本票的前提需要重新评估。");
    }

    private static int NarrowestCarrierColumnWidth()
    {
        using var inventory = InventoryModel();
        var widths = InventoryCarrierColumns
            .Select(column => column.Resolve(inventory))
            .OfType<int>()
            .ToArray();

        Assert.Equal(InventoryCarrierColumns.Length, widths.Length);
        return widths.Min();
    }

    private static string[] WorstCaseScopeSegments()
    {
        using var mes = MesModel();
        var entity = mes.Model.FindEntityType(typeof(FinishedGoodsReceiptRequest));
        Assert.NotNull(entity);

        return ScopeProperties
            .Select(name =>
            {
                var width = entity.FindProperty(name)?.GetMaxLength();
                Assert.True(width is > 0, $"Mes 作用域列 {name} 解析不到列宽。");
                return new string('s', width!.Value);
            })
            .ToArray();
    }

    private static int MaximumRetryIdempotencyKeyLength()
    {
        var validator = new RetryFinishedGoodsReceiptInventoryPostingCommandValidator();
        if (validator is not IEnumerable<IValidationRule> rules)
        {
            return 0;
        }

        int? maximum = null;
        foreach (var rule in rules)
        {
            var member = rule.Member?.Name ?? rule.PropertyName;
            if (!string.Equals(member, IdempotencyKeyPropertyName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var component in rule.Components)
            {
                if (component.Validator is ILengthValidator length && length.Max > 0)
                {
                    maximum = maximum is { } current ? Math.Min(current, length.Max) : length.Max;
                }
            }
        }

        return maximum ?? 0;
    }

    private sealed record CarrierColumn(Type EntityType, string PropertyName, string Label)
    {
        public int? Resolve(DbContext context) =>
            context.Model.FindEntityType(EntityType)?.FindProperty(PropertyName)?.GetMaxLength();

        public override string ToString() => $"{Label}.{PropertyName}";
    }

    private static DbContext InventoryModel() => ModelOnly<InventoryDbContext>(
        options => new InventoryDbContext(options, NullMediator.Instance));

    private static DbContext MesModel() => ModelOnly<MesDbContext>(
        options => new MesDbContext(options, NullMediator.Instance));

    /// <summary>只用于读 EF 模型：不开连接、不建库。连接串必须语法合法，Npgsql 才肯建 provider。</summary>
    private static DbContext ModelOnly<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=127.0.0.1;Database=nerv_iip_finished_goods_posting_key_contract;Username=nerv;Password=nerv")
            .Options;
        return factory(options);
    }

    private sealed class NullMediator : IMediator
    {
        public static readonly NullMediator Instance = new();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            throw new NotSupportedException();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
