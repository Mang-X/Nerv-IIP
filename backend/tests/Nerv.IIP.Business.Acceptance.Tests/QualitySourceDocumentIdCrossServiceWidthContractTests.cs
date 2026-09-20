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
/// <item><see cref="Periodic_source_line_identity_at_its_structural_upper_bound_fits_its_own_carrier_columns"/>：
/// 周期检来源行身份的上界由 **Quality 工序 id 列宽 + 最长 kind + Guid "D" 位数 + long 最大位数**派生，
/// 拼法取自生产代码 <c>PeriodicInspectionSourceLine.LineId</c>；</item>
/// <item><see cref="Quality_task_side_identity_columns_never_exceed_their_record_side_counterparts"/>：
/// 任务侧那两列的取值会**逐列**搬到检验记录的同名列上；来源单据列要求任务侧不宽于记录侧，
/// 来源行列要求两侧**等宽**（记录侧那一列的唯一非空写入者就是任务侧的拷贝）。</item>
/// </list>
///
/// <para><b>#3319 改写了后两条的前提，没有删掉它们。</b>改前任务侧的来源单据与来源行两列会经
/// <c>InspectionTask.InspectionRecordSourceDocumentId()</c> 二选一地变成检验记录的**同一列**
/// （<c>inspection_records.source_document_id</c>），周期检因此把复合来源行送过服务边界。
/// 该二分已整段退休：检验记录多了 <c>source_document_line_id</c>，两列各搬各的。
/// 于是——
/// <list type="bullet">
/// <item>第 4 条从「两列都 ≤ 记录侧来源单据列」改写成「按列配对」。
/// <b>改写后与改写前不可比，不是它的超集</b>：改前那条的额外强度绑在已经不存在的二分上
/// （来源行会被搬进记录侧的**来源单据**列），今天两列同为 250 才使两个形态恰好等价。
/// 把记录侧来源行列单边放宽到 500、任务侧放到 300，改前那条必红（300 &gt; 250）而「逐列 ≤」会绿——
/// 那正是改写**放弃**的那一段耦合。为了不把这段强度白丢，来源行那一列改成**等宽**断言：
/// 记录侧更窄 = 任务侧取值搬过去时溢出，记录侧更宽 = 一段永远到不了的幽灵宽度
/// （该列的非空取值只有一个来源：<c>CreateInspectionRecordFromTaskCommand</c> 拷贝任务侧那一列；
/// 直录录入命令恒写 null，复检拷贝上一条）。等宽在上面那格变异下报红。</item>
/// <item>第 3 条的承接列从 MES 侧那几列改写成 Quality 侧的来源行两列——周期检来源行不再进
/// <c>payload.SourceDocumentId</c>（那一列现在是工单公开 id），它**不再跨服务**，
/// 因此对 MES 列宽的要求已不是它的约束。放弃的正是这一段，且是因为它真的不成立了，
/// 不是因为不方便断言。首件复合身份仍进 <c>payload.SourceDocumentId</c>，第 2 条原样保留。</item>
/// </list></para>
///
/// **合同分类**（<c>docs/governance/testing/validity.md</c>）：<c>ProviderBehavior</c> + <c>Regression</c>。
/// <c>ProviderBehavior</c> 的权威来源是两侧的 migration / EntityConfiguration 列宽约束；
/// <c>Regression</c> 的权威来源是 GitHub #3315 的期望第 2、3 项。
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

    /// <summary>#3319 起来源行有自己的列，任务侧与记录侧同名。</summary>
    private const string SourceDocumentLineIdColumn = "source_document_line_id";

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

    /// <summary>
    /// #3319 后周期检来源行留在 Quality 自己的两列里（任务侧与记录侧的 <c>source_document_line_id</c>），
    /// 不再经 <c>payload.SourceDocumentId</c> 过界，因此它的承接列就是这两列。
    /// </summary>
    [Fact]
    public void Periodic_source_line_identity_at_its_structural_upper_bound_fits_its_own_carrier_columns()
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
            lineId.Length > operationIdWidth,
            "周期检来源行身份必须严格长于工序 id 段，否则本用例退化为同义反复。");
        foreach (var table in new[] { "inspection_tasks", QualityProducerTable })
        {
            var width = ColumnWidth(qualityModel, table, SourceDocumentLineIdColumn);
            Assert.True(
                lineId.Length <= width,
                $"周期检复合来源行身份上界 {lineId.Length} 超出承接列 {table}.{SourceDocumentLineIdColumn} 的 {width}。");
        }
    }

    /// <summary>
    /// 任务侧的来源单据与来源行两列，自 #3319 起**逐列**搬到检验记录的同名列上
    /// （改前是二选一地搬进记录侧的来源单据那一列）。因此约束按列配对，不再共用一个上界。
    ///
    /// <para>两列的约束强度不同，因为写入者数量不同：</para>
    /// <list type="bullet">
    /// <item><b>来源单据列只能要求「不宽于」</b>：记录侧那一列另有写入者（直录录入命令、
    /// 首件的复合来源身份），把它钉成等宽会在那些写入者需要更宽时报**假红**。</item>
    /// <item><b>来源行列要求「等宽」</b>：记录侧那一列的非空取值只有一个来源——
    /// <c>CreateInspectionRecordFromTaskCommand</c> 原样拷贝任务侧那一列（直录录入命令恒写 null，
    /// 复检拷贝上一条，种子两侧同源）。因此记录侧更宽的那一段是**永远到不了的幽灵宽度**，
    /// 更窄则是搬运时溢出，两个方向都该红。</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Quality_task_side_identity_columns_never_exceed_their_record_side_counterparts()
    {
        using var quality = CreateQualityModelOnlyDbContext();
        var qualityModel = quality.GetService<IDesignTimeModel>().Model;

        var taskDocumentWidth = ColumnWidth(qualityModel, "inspection_tasks", SourceDocumentIdColumn);
        var recordDocumentWidth = ColumnWidth(qualityModel, QualityProducerTable, SourceDocumentIdColumn);
        Assert.True(
            taskDocumentWidth <= recordDocumentWidth,
            $"inspection_tasks.{SourceDocumentIdColumn} 宽 {taskDocumentWidth}，宽于 "
            + $"{QualityProducerTable}.{SourceDocumentIdColumn} 的 {recordDocumentWidth}："
            + "任务侧取值原样搬到记录侧时会自己溢出。");

        Assert.Equal(
            ColumnWidth(qualityModel, "inspection_tasks", SourceDocumentLineIdColumn),
            ColumnWidth(qualityModel, QualityProducerTable, SourceDocumentLineIdColumn));
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
