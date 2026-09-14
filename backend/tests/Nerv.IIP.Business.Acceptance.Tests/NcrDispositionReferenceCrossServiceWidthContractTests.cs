using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Contracts.Quality;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using QualityDbContext = Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// GitHub #3318：<c>NcrDispositionDecidedPayload</c> 的三个处置引用字段（返修工单 / 报废流水 / 退供单）
/// 都会被同一个消费者写进 MES 的**同一列** <c>defect_records.disposition_reference_id</c>。
/// 按 #3281 判据「一个值写进多列时有效上界取列宽最小值」，它的有效上界是「Quality 产出列宽」与
/// 「MES 承载列宽」的最小值——改前 Quality 三列各 150、MES 只有 100，有效上界其实是 100，
/// 而生产者按 150 产出。
///
/// 这个类是本仓唯一能把这句话写成断言的地方：本测试项目同时引用 Quality.Web 与 Mes.Web，
/// 因而能在同一个进程里读到**两个服务各自的 EF 模型**。单服务的常量对撞只能证明「我这一侧没被改」。
///
/// **钉住四件事，全部从 EF 模型 + 契约类型反射派生，代码里零手抄上界**：
/// <list type="number">
/// <item><see cref="Mes_receiving_column_is_at_least_as_wide_as_every_quality_producer_column"/>：
/// 任一侧列宽被单边改窄/放宽即红；</item>
/// <item><see cref="Every_string_payload_field_is_either_a_disposition_reference_carrier_or_a_named_non_carrier"/>：
/// 「哪些 payload 字段会变成 <c>disposition_reference_id</c>」这个集合被**反射闭集**封闭——
/// 契约里新增一个字符串字段就会红，而不是静默漏出第四个承载面；</item>
/// <item><see cref="Manual_ncr_close_entry_never_accepts_a_reference_wider_than_the_mes_receiving_column"/>：
/// 手工关单入口（<c>CloseNonconformanceReportCommandValidator</c>，Quality 侧唯一能人工录入
/// 报废流水号 / 退供单号的写面）的上界不得超过 MES 承载列宽；</item>
/// <item><see cref="Rework_branch_reference_is_bounded_by_the_mes_work_order_id_column"/>：
/// 返修那一支的取值来自 MES 工单号，其结构上界由 <c>work_orders.work_order_id</c> 列宽决定。</item>
/// </list>
///
/// **合同分类**（<c>docs/governance/testing/validity.md</c>）：<c>ProviderBehavior</c> + <c>Regression</c>。
/// <c>ProviderBehavior</c> 的权威来源是两侧的 migration / EntityConfiguration 列宽约束与
/// <c>NcrDispositionDecidedPayload</c> 契约类型；<c>Regression</c> 的权威来源是 GitHub #3318。
///
/// **值域边界（声明放弃了什么，别读成完备）**：
/// <list type="bullet">
/// <item>列宽读的是 **EF 模型**而不是迁移脚本。模型与迁移单边漂移不由本类抓——
/// 它由 EF 自己在 <c>MigrateAsync</c> 内的 <c>ValidateMigrations</c> 抛
/// <c>PendingModelChangesWarning</c>（<c>InvalidOperationException</c>：
/// 「The model for context 'X' has pending changes.」）暴露，因而只在**跑真库迁移的用例**上显形。
/// <b>本仓没有 pending-model-changes 门禁</b>——<c>HasPendingModelChanges</c> 全仓零调用点，
/// <c>.github/</c> 与 <c>scripts/</c> 里也没有任何 job 或脚本跑
/// <c>dotnet ef migrations has-pending-model-changes</c>（#3347 三条枚举路径实读）。
/// 在 PR 上这条保护由 <c>PostgreSQL Provider Tests</c> job 承担：业务服务路径恒选中 impact-plan 的
/// <c>postgresql</c> gate（#3347 实测），因此 Quality/MES 这两侧的漂移在引入它的那个 PR 上就会红。
/// 别照抄别处注释里那句「由 pending-model-changes 门禁承担」。</item>
/// <item>本类只覆盖这三条跨服务字段对。其它 Quality → MES / MES → Quality 的字段对已由
/// PR #3317 正文逐条枚举登记，不由本类看守。</item>
/// <item>本类是纯模型读取 + 纯反射 + 校验器调用，**不能**证明真库落库行为；
/// 那一面由 <see cref="MesDefectDispositionReferenceAcceptanceTests"/> 的 <c>postgres:18</c> 读数承担。</item>
/// </list>
/// </summary>
public sealed class NcrDispositionReferenceCrossServiceWidthContractTests
{
    /// <summary>
    /// <c>NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect</c> 的 <c>DispositionType</c>
    /// 三选一分支里，会成为 <c>disposition_reference_id</c> 的那三个 payload 字段。
    /// 这不是白名单：<see cref="Every_string_payload_field_is_either_a_disposition_reference_carrier_or_a_named_non_carrier"/>
    /// 用反射把整个字符串字段集合按「承载面 ∪ 具名非承载面 == 全集」封闭。
    /// </summary>
    private static readonly string[] DispositionReferenceCarriers =
    [
        nameof(NcrDispositionDecidedPayload.ReturnDocumentId),
        nameof(NcrDispositionDecidedPayload.ReworkWorkOrderId),
        nameof(NcrDispositionDecidedPayload.ScrapMovementId),
    ];

    /// <summary>
    /// payload 里其余的字符串字段：它们各自落到 MES 的**别的**列（或根本不落 MES），
    /// 不由本契约管辖。具名 + 计数封闭，「名单里没有」不构成豁免。
    /// </summary>
    private static readonly string[] NonCarrierStringFields =
    [
        nameof(NcrDispositionDecidedPayload.DispositionApprovalChainId),
        nameof(NcrDispositionDecidedPayload.DispositionType),
        nameof(NcrDispositionDecidedPayload.LocationCode),
        nameof(NcrDispositionDecidedPayload.LotNo),
        nameof(NcrDispositionDecidedPayload.NcrCode),
        nameof(NcrDispositionDecidedPayload.NcrId),
        nameof(NcrDispositionDecidedPayload.OwnerId),
        nameof(NcrDispositionDecidedPayload.OwnerType),
        nameof(NcrDispositionDecidedPayload.SerialNo),
        nameof(NcrDispositionDecidedPayload.SiteCode),
        nameof(NcrDispositionDecidedPayload.SkuCode),
        nameof(NcrDispositionDecidedPayload.SourceDocumentId),
        nameof(NcrDispositionDecidedPayload.UomCode),
    ];

    /// <summary>改前的 MES 承载列宽。这条读数说明缺陷不是假想的，也把「行为变化只发生在 101–150 这一段」写死。</summary>
    private const int PreChangeMesReceivingWidth = 100;

    [Fact]
    public void Mes_receiving_column_is_at_least_as_wide_as_every_quality_producer_column()
    {
        var producers = QualityProducerColumnWidths();
        var receivingWidth = MesReceivingColumnWidth();

        Assert.Equal(
            DispositionReferenceCarriers,
            producers.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.All(
            producers,
            producer => Assert.True(
                receivingWidth >= producer.Value,
                $"MES 承载列 defect_records.disposition_reference_id 宽 {receivingWidth}，"
                + $"窄于 Quality 产出列（{producer.Key}）的 {producer.Value}；"
                + "超宽取值会在 SaveChangesAsync 抛 22001，而该消费者函数体内一条 catch 都没有。"));

        // 改前读数：缺陷确实存在过，且行为变化只发生在「> 改前列宽」的那一段。
        Assert.All(
            producers,
            producer => Assert.True(
                PreChangeMesReceivingWidth < producer.Value,
                $"改前 MES 承载列宽 {PreChangeMesReceivingWidth} 未低于 Quality 产出列 {producer.Key} 的 {producer.Value}，缺陷描述有误。"));
        Assert.True(receivingWidth > PreChangeMesReceivingWidth, "MES 承载列没有加宽。");

        // 守卫用的常量必须就是这一列的宽度，而不是另抄的一个数。
        Assert.Equal(MesDefectDispositionReferenceIdPolicy.ColumnMaxLength, receivingWidth);
    }

    [Fact]
    public void Every_string_payload_field_is_either_a_disposition_reference_carrier_or_a_named_non_carrier()
    {
        var stringFields = typeof(NcrDispositionDecidedPayload)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            DispositionReferenceCarriers.Concat(NonCarrierStringFields).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            stringFields);
        // 反同义反复：两个分区都非空，且没有交集——否则上面那条等式可以被空集或重复项凑出来。
        Assert.NotEmpty(DispositionReferenceCarriers);
        Assert.NotEmpty(NonCarrierStringFields);
        Assert.Empty(DispositionReferenceCarriers.Intersect(NonCarrierStringFields, StringComparer.Ordinal));
    }

    /// <summary>
    /// 手工关单入口的上界必须跟着承载列宽走（照 #3315 复审必改 1 的形状，但这里的结论方向不同）。
    ///
    /// <c>CloseNonconformanceReportCommandValidator</c> 是 Quality 侧唯一能**人工录入**报废流水号 /
    /// 退供单号的写面（返修工单号那一支由 MES 回执绑定，端点上是 <c>Must(IsNullOrWhiteSpace)</c>）。
    /// 它此前手抄 <c>MaximumLength(150)</c>：那个数恰好等于 Quality 自己的产出列宽，且加宽后也恰好
    /// 等于 MES 承载列宽，因此**本票没有产生「可达但手工放不掉」的新死角**，按裁定不改那两行；
    /// 但「这个入口的上界不得超过 MES 能存下的宽度」必须写成断言，否则下次谁把它放宽到 200，
    /// 消费链会重新变成 poison message，而两侧的单服务契约用例都不会红。
    /// </summary>
    [Fact]
    public void Manual_ncr_close_entry_never_accepts_a_reference_wider_than_the_mes_receiving_column()
    {
        var validator = new CloseNonconformanceReportCommandValidator();
        var receivingWidth = MesReceivingColumnWidth();

        var atBound = validator.Validate(CloseCommand(new string('s', receivingWidth)));
        Assert.True(atBound.IsValid, $"承载列宽那一位必须放行，实际错误：{string.Join("；", atBound.Errors)}");

        var overBound = validator.Validate(CloseCommand(new string('s', receivingWidth + 1)));
        Assert.False(
            overBound.IsValid,
            $"手工关单入口放行了 {receivingWidth + 1} 字符的处置引用，但 MES 承载列只有 {receivingWidth} —— "
            + "这一位会在消费者侧抛 22001 并逃逸成 poison message。");
        // 拒绝原因必须落在这条引用规则上，而不是命令里别的字段。
        // PropertyName 的大小写/分隔形态取决于进程内有没有别的用例改过 FluentValidation 的全局
        // PropertyNameResolver（整程序集一起跑时确实会变，单类过滤跑时不会），因此比对前把非字母数字
        // 字符去掉并忽略大小写；失败消息带上实际观察到的取值，形态真漂了能一眼看出来。
        var observedProperties = overBound.Errors.Select(error => error.PropertyName).ToArray();
        Assert.True(
            observedProperties.Any(property => string.Equals(
                new string([.. property.Where(char.IsLetterOrDigit)]),
                nameof(CloseNonconformanceReportCommand.ScrapMovementId),
                StringComparison.OrdinalIgnoreCase)),
            $"越界那一位被拒绝了，但错误没有落在 {nameof(CloseNonconformanceReportCommand.ScrapMovementId)} 这条规则上；"
            + $"实际的 PropertyName：{(observedProperties.Length == 0 ? "<空>" : string.Join("、", observedProperties))}。");
    }

    /// <summary>
    /// 返修那一支的取值不是人工录入的：它由 MES 的返修工单创建回执绑定到
    /// <c>nonconformance_reports.rework_work_order_id</c>，取值就是 MES 的工单号。
    /// 因此这一支的结构上界由 <c>mes.work_orders.work_order_id</c> 的列宽决定，
    /// 而不是由 Quality 那一列的声明宽度决定——这一条把该结构上界也钉在承载列宽以内。
    /// </summary>
    [Fact]
    public void Rework_branch_reference_is_bounded_by_the_mes_work_order_id_column()
    {
        using var mes = CreateMesModelOnlyDbContext();
        var mesModel = mes.GetService<IDesignTimeModel>().Model;
        var workOrderIdWidth = ColumnWidth(mesModel, "work_orders", "work_order_id");
        var receivingWidth = MesReceivingColumnWidth();

        Assert.True(
            workOrderIdWidth <= receivingWidth,
            $"返修工单号列宽 {workOrderIdWidth} 超过 MES 承载列宽 {receivingWidth}：返修那一支回写自己就会溢出。");
        // 反同义反复：这一支的上界必须严格窄于承载列宽，否则本用例与上面那条列宽等式重复。
        Assert.True(
            workOrderIdWidth < receivingWidth,
            "返修支上界与承载列宽相等，本用例退化为同义反复，需要重述而不是删掉断言。");
    }

    private static CloseNonconformanceReportCommand CloseCommand(string scrapMovementId) =>
        new(new NonconformanceReportId(Guid.NewGuid()), scrapMovementId, null, "closed by acceptance contract test");

    /// <summary>
    /// Quality 侧三个产出列的宽度。列名不手抄：按 payload 字段名在
    /// <see cref="NonconformanceReport"/> 实体上取同名属性，再从 EF 模型读列宽。
    /// </summary>
    private static IReadOnlyDictionary<string, int> QualityProducerColumnWidths()
    {
        using var quality = CreateQualityModelOnlyDbContext();
        var entityType = quality.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NonconformanceReport))
            ?? throw new InvalidOperationException("Quality 模型里找不到 NonconformanceReport 实体。");
        return DispositionReferenceCarriers.ToDictionary(
            carrier => carrier,
            carrier => (entityType.FindProperty(carrier)
                    ?? throw new InvalidOperationException($"NonconformanceReport 上没有 {carrier} 属性。"))
                .GetMaxLength()
                ?? throw new InvalidOperationException($"NonconformanceReport.{carrier} 没有声明列宽。"),
            StringComparer.Ordinal);
    }

    private static int MesReceivingColumnWidth()
    {
        using var mes = CreateMesModelOnlyDbContext();
        return ColumnWidth(
            mes.GetService<IDesignTimeModel>().Model,
            "defect_records",
            "disposition_reference_id");
    }

    private static int ColumnWidth(IModel model, string table, string column)
    {
        var property = Assert.Single(
            model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()),
            candidate =>
                string.Equals(candidate.DeclaringType.GetTableName(), table, StringComparison.Ordinal)
                && string.Equals(candidate.GetColumnName(), column, StringComparison.Ordinal));
        return property.GetMaxLength()
            ?? throw new InvalidOperationException($"{table}.{column} 没有声明列宽。");
    }

    private static MesDbContext CreateMesModelOnlyDbContext() =>
        new(
            new DbContextOptionsBuilder<MesDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=nerv_iip_disposition_reference_contract;Username=nerv;Password=nerv",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema))
                .Options,
            DispositionReferenceCrossServiceNoopMediator.Instance);

    private static QualityDbContext CreateQualityModelOnlyDbContext() =>
        new(
            new DbContextOptionsBuilder<QualityDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=nerv_iip_disposition_reference_contract;Username=nerv;Password=nerv",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", QualityFacts.Schema))
                .Options,
            DispositionReferenceCrossServiceNoopMediator.Instance);

    private sealed class DispositionReferenceCrossServiceNoopMediator : IMediator
    {
        internal static readonly DispositionReferenceCrossServiceNoopMediator Instance = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("This contract test mediator only supports publish.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This contract test mediator only supports publish.");
    }
}
