using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using QualityDbContext = Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// GitHub #3315：<c>InspectionResultPayload.SourceDocumentId</c> 这一个值同时写进 Quality 与 MES 两个服务的列，
/// 按 #3281 判据「一个值写进多列时有效上界取列宽最小值」，它的有效上界是**两侧列宽的最小值**——
/// 改前 Quality 侧声明 250、MES 侧只有 100，有效上界其实是 100，而生产者按 250 产出。
///
/// 这个类是本仓唯一能把这句话写成断言的地方：本测试项目同时引用 Quality.Web 与 Mes.Web，
/// 因而能在同一个进程里读到**两个服务各自的 EF 模型**。单服务的测试项目做不到，
/// 单服务的常量对撞也只能证明「我这一侧没被改」。
///
/// **钉住四件事，全部从 EF 模型闭集枚举 + 生产代码派生，代码里零手抄上界**：
/// <list type="number">
/// <item><see cref="Mes_receiving_columns_are_at_least_as_wide_as_the_quality_producer_column"/>：
/// 任一侧列宽被单边改窄即红；</item>
/// <item><see cref="First_article_composite_identity_at_the_mes_id_upper_bound_fits_every_receiving_column"/>：
/// 首件复合身份的上界由 **MES 工单 id / 工序任务 id 列宽**派生，复合拼法取自生产代码
/// <c>FirstArticleInspection.SourceDocumentId</c>——复合构成被加长（多一段、分隔符变长）即红；</item>
/// <item><see cref="Periodic_source_line_identity_at_its_structural_upper_bound_fits_every_receiving_column"/>：
/// 周期检来源行身份的上界由 **Quality 工序 id 列宽 + 最长 kind + Guid "D" 位数 + long 最大位数**派生，
/// 拼法取自生产代码 <c>PeriodicInspectionSourceLine.LineId</c>；</item>
/// <item><see cref="Quality_task_side_identity_columns_never_exceed_the_record_side_producer_column"/>：
/// 任务侧那两列的取值会经 <c>InspectionTask.InspectionRecordSourceDocumentId()</c> 变成检验记录的来源身份，
/// 因此它们不得宽于检验记录那一列。</item>
/// </list>
///
/// **合同分类**（<c>docs/governance/testing/validity.md</c>）：<c>ProviderBehavior</c> + <c>Regression</c>。
/// <c>ProviderBehavior</c> 的权威来源是两侧的 migration / EntityConfiguration 列宽约束；
/// <c>Regression</c> 的权威来源是 GitHub #3315 的期望第 2、3 项。
///
/// **值域边界（声明放弃了什么，别读成完备）**：
/// <list type="bullet">
/// <item>列宽读的是 **EF 模型**而不是迁移脚本。只改迁移不改模型（或反之）本类抓不到，
/// 那是「模型/迁移漂移」另一类护栏（pending model changes 门禁）的职责。</item>
/// <item>本类只覆盖 <c>InspectionResultPayload.SourceDocumentId</c> 这一条跨服务字段对。
/// 其它 Quality → MES / MES → Quality 的字段对已在 PR 正文按跨服务字段对逐条枚举并登记，
/// **本票只登记不修**，也**不由本类看守**。</item>
/// <item>本类是纯模型读取 + 纯函数，按 validity.md 的证明范围表，它**不能**证明真库落库行为；
/// 那一面由 <see cref="QualityFirstArticleMesQualityHoldAcceptanceTests"/> 的
/// <c>postgres:18</c> 读数承担。</item>
/// </list>
/// </summary>
public sealed class QualitySourceDocumentIdCrossServiceWidthContractTests
{
    private const string SourceDocumentIdColumn = "source_document_id";

    /// <summary>
    /// MES 模型里 <c>source_document_id</c> 这一列的**具名豁免**：<c>work_orders</c> 的同名列属于
    /// owned type <c>WorkOrderSource</c>，承载的是 DemandPlanning 计划单/建议单溯源身份，
    /// 与 Quality 检验来源身份不是同一个值，不由本契约管辖。
    /// 豁免必须具名且被计数封闭——「名单里没有」不构成豁免。
    /// </summary>
    private const string MesExemptedTable = "work_orders";

    /// <summary>MES 模型里 <c>source_document_id</c> 列的总数（含上面那条具名豁免）。</summary>
    private const int MesSourceDocumentIdColumnCount = 3;

    /// <summary>Quality 侧真正产出 <c>payload.SourceDocumentId</c> 的那张表。</summary>
    private const string QualityProducerTable = "inspection_records";

    /// <summary>
    /// 改前的 MES 承载列宽。这条读数说明缺陷不是假想的，也把「行为变化只发生在 101–250 这一段」写死。
    /// </summary>
    private const int PreChangeMesReceivingWidth = 100;

    [Fact]
    public void Mes_receiving_columns_are_at_least_as_wide_as_the_quality_producer_column()
    {
        var producerWidth = QualityProducerColumnWidth();
        var receiving = MesReceivingColumns();

        Assert.All(
            receiving,
            column => Assert.True(
                column.Value >= producerWidth,
                $"MES 承载列 {column.Key} 宽 {column.Value}，窄于 Quality 产出列 {QualityProducerTable}.{SourceDocumentIdColumn} 的 {producerWidth}；"
                + "超宽取值会在 SaveChangesAsync 抛 22001，而该消费者接不住 DbUpdateException。"));

        // 改前读数：缺陷确实存在过，且行为变化只发生在「> 改前列宽」的那一段。
        Assert.True(
            PreChangeMesReceivingWidth < producerWidth,
            $"改前 MES 承载列宽 {PreChangeMesReceivingWidth} 未低于 Quality 产出列宽 {producerWidth}，缺陷描述有误。");
        Assert.All(
            receiving,
            column => Assert.True(column.Value > PreChangeMesReceivingWidth, $"承载列 {column.Key} 没有加宽。"));

        // 守卫用的常量必须就是这些列的共同宽度，而不是另抄的一个数。
        Assert.All(
            receiving,
            column => Assert.Equal(MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength, column.Value));
    }

    [Fact]
    public void First_article_composite_identity_at_the_mes_id_upper_bound_fits_every_receiving_column()
    {
        using var mes = CreateMesModelOnlyDbContext();
        var mesModel = mes.GetService<IDesignTimeModel>().Model;
        var workOrderIdWidth = ColumnWidth(mesModel, "work_orders", "work_order_id");
        var operationTaskIdWidth = ColumnWidth(mesModel, "operation_tasks", "operation_task_id");

        // 拼法取自生产代码，不在测试里手抄 "{a}:{b}"。
        var composite = FirstArticleInspection.SourceDocumentId(
            new string('w', workOrderIdWidth),
            new string('o', operationTaskIdWidth));

        Assert.True(
            composite.Length > workOrderIdWidth && composite.Length > operationTaskIdWidth,
            "首件复合身份必须严格长于任一段，否则本用例退化为同义反复。");
        Assert.True(
            composite.Length > PreChangeMesReceivingWidth,
            $"首件复合身份上界 {composite.Length} 未超过改前列宽 {PreChangeMesReceivingWidth}，缺陷描述有误。");
        AssertFitsEveryReceivingColumn(composite, "首件复合来源身份");
    }

    [Fact]
    public void Periodic_source_line_identity_at_its_structural_upper_bound_fits_every_receiving_column()
    {
        using var quality = CreateQualityModelOnlyDbContext();
        var qualityModel = quality.GetService<IDesignTimeModel>().Model;
        var operationIdWidth = ColumnWidth(qualityModel, "periodic_inspection_runtime_contexts", "operation_id");

        var longestKind = new[] { PeriodicInspectionSourceLine.TimeKind, PeriodicInspectionSourceLine.QuantityKind }
            .OrderByDescending(kind => kind.Length)
            .ThenBy(kind => kind, StringComparer.Ordinal)
            .First();

        // 拼法取自生产代码；三个可变段各取结构上界：工序 id 取列宽，Guid 取 "D" 格式，窗口序号取 long 上界。
        var lineId = PeriodicInspectionSourceLine.LineId(
            new string('o', operationIdWidth),
            longestKind,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            long.MaxValue);

        Assert.True(
            lineId.Length > PreChangeMesReceivingWidth,
            $"周期检来源行身份上界 {lineId.Length} 未超过改前列宽 {PreChangeMesReceivingWidth}；"
            + "若这条不再成立，说明周期检那一支已不在本缺陷射程内，需要重述而不是删掉断言。");
        AssertFitsEveryReceivingColumn(lineId, "周期检复合来源行身份");
    }

    [Fact]
    public void Quality_task_side_identity_columns_never_exceed_the_record_side_producer_column()
    {
        using var quality = CreateQualityModelOnlyDbContext();
        var qualityModel = quality.GetService<IDesignTimeModel>().Model;
        var producerWidth = ColumnWidth(qualityModel, QualityProducerTable, SourceDocumentIdColumn);

        // InspectionTask.InspectionRecordSourceDocumentId() 会把这两列之一变成检验记录的来源身份，
        // 于是它们的宽度共同决定了产出侧的实际上界。
        Assert.True(
            ColumnWidth(qualityModel, "inspection_tasks", SourceDocumentIdColumn) <= producerWidth,
            "inspection_tasks.source_document_id 宽于 inspection_records.source_document_id：任务侧取值搬到记录侧时会自己溢出。");
        Assert.True(
            ColumnWidth(qualityModel, "inspection_tasks", "source_document_line_id") <= producerWidth,
            "inspection_tasks.source_document_line_id 宽于 inspection_records.source_document_id：周期检来源行搬到记录侧时会自己溢出。");
    }

    private static void AssertFitsEveryReceivingColumn(string identity, string label)
    {
        var producerWidth = QualityProducerColumnWidth();
        Assert.True(
            identity.Length <= producerWidth,
            $"{label} 上界 {identity.Length} 超出 Quality 产出列宽 {producerWidth}。");
        foreach (var column in MesReceivingColumns())
        {
            Assert.True(
                identity.Length <= column.Value,
                $"{label} 上界 {identity.Length} 超出 MES 承载列 {column.Key} 的 {column.Value}。");
        }
    }

    private static int QualityProducerColumnWidth()
    {
        using var quality = CreateQualityModelOnlyDbContext();
        return ColumnWidth(
            quality.GetService<IDesignTimeModel>().Model,
            QualityProducerTable,
            SourceDocumentIdColumn);
    }

    /// <summary>
    /// MES 侧承载该值的列，从 EF 模型**闭集枚举**（不是手写白名单）：新增一张带
    /// <c>source_document_id</c> 的表会自动进值域并让计数断言红，而不是静默漏掉。
    /// </summary>
    private static IReadOnlyDictionary<string, int> MesReceivingColumns()
    {
        using var mes = CreateMesModelOnlyDbContext();
        var model = mes.GetService<IDesignTimeModel>().Model;
        var columns = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => string.Equals(property.GetColumnName(), SourceDocumentIdColumn, StringComparison.Ordinal))
            .Select(property => (Table: property.DeclaringType.GetTableName() ?? string.Empty, Property: property))
            .ToArray();
        Assert.Equal(MesSourceDocumentIdColumnCount, columns.Length);

        var exempted = columns
            .Where(column => string.Equals(column.Table, MesExemptedTable, StringComparison.Ordinal))
            .ToArray();
        Assert.Single(exempted);

        var governed = columns.Except(exempted).ToArray();
        // 豁免 + 受管 == 闭集总数：不许出现「既不在豁免名单、也没被受管断言覆盖」的第三类。
        Assert.Equal(MesSourceDocumentIdColumnCount, governed.Length + exempted.Length);
        return governed.ToDictionary(
            column => $"{column.Table}.{SourceDocumentIdColumn}",
            column => column.Property.GetMaxLength()
                ?? throw new InvalidOperationException($"{column.Table}.{SourceDocumentIdColumn} 没有声明列宽。"),
            StringComparer.Ordinal);
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
                    "Host=localhost;Database=nerv_iip_source_document_id_contract;Username=nerv;Password=nerv",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema))
                .Options,
            CrossServiceContractNoopMediator.Instance);

    private static QualityDbContext CreateQualityModelOnlyDbContext() =>
        new(
            new DbContextOptionsBuilder<QualityDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=nerv_iip_source_document_id_contract;Username=nerv;Password=nerv",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", QualityFacts.Schema))
                .Options,
            CrossServiceContractNoopMediator.Instance);

    private sealed class CrossServiceContractNoopMediator : IMediator
    {
        internal static readonly CrossServiceContractNoopMediator Instance = new();

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
