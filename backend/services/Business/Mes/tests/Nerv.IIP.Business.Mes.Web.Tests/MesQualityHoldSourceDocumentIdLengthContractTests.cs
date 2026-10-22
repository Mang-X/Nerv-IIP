using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using FluentValidation.Results;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// GitHub #3315：MES 侧承载 Quality 来源单据身份的两列，与守卫常量之间的对撞。
///
/// 这个类钉住三件事：
/// <list type="bullet">
/// <item><see cref="Governed_source_document_id_columns_all_match_the_policy_constant"/>：列宽从
/// **EF 模型闭集枚举**（<c>IDesignTimeModel</c>）读，不读迁移文本、也不读第二份常量 →
/// 单边改 <c>HasMaxLength</c>、或新增一张带该列的表，都会红；</item>
/// <item><see cref="Source_identity_at_the_bound_is_captured_and_one_over_the_bound_is_dead_lettered"/>：
/// 跑**真实消费者**，上界那一位落库、+1 位进死信；关键读数是「+1 位**不抛出**」——
/// 改前那一位抛的是 <c>DbUpdateException</c> 且逃逸成 poison message（#877）；</item>
/// <item><see cref="Overlong_failure_message_reports_the_length_without_echoing_the_value"/>：
/// 死信消息只报长度不回显取值，别让 <c>failure_message</c> 自己变成第二个越界写面。</item>
/// </list>
///
/// **合同分类**（<c>docs/governance/testing/validity.md</c>）：<c>ProviderBehavior</c> + <c>Regression</c>。
/// <c>ProviderBehavior</c> 的权威来源是 MES 的 EntityConfiguration / migration 列宽约束；
/// <c>Regression</c> 的权威来源是 GitHub #3315（错误行为「22001 逃逸成 poison message」、
/// 期望行为「同一输入不再卡链」）。
///
/// **证明范围（别读成完备）**：本类跑在 **SQLite** 上，SQLite 不执行 <c>varchar(n)</c> 长度约束，
/// 因此本类的绿**不能**读成「落库不会 22001」——它证明的是**守卫本身**在上界两侧的行为差异。
/// 真库那一面由 <c>Nerv.IIP.Business.Acceptance.Tests.QualityFirstArticleMesQualityHoldAcceptanceTests</c>
/// 的 <c>postgres:18</c> 读数承担；「Quality 侧列宽没被单边改窄」由
/// <c>Nerv.IIP.Business.Acceptance.Tests.QualitySourceDocumentIdCrossServiceWidthContractTests</c> 承担。
/// 本类也**不**证明「没有别的路径把值写进那两列」——那类源码文本扫描在 #3176 / PR #3214 三轮实证不收敛，已按裁定不建。
/// </summary>
public sealed class MesQualityHoldSourceDocumentIdLengthContractTests
{
    private const string SourceDocumentIdColumn = "source_document_id";

    /// <summary>
    /// <c>source_document_id</c> 这一列在 MES 模型里的**具名豁免**：<c>work_orders</c> 的同名列属于
    /// owned type <c>WorkOrderSource</c>，承载 DemandPlanning 计划单溯源身份，不是 Quality 检验来源身份。
    /// 豁免必须具名且被计数封闭——「名单里没有」不构成豁免。
    /// </summary>
    private const string ExemptedTable = "work_orders";

    /// <summary>MES 模型里 <c>source_document_id</c> 列的总数（含上面那条具名豁免）。</summary>
    private const int SourceDocumentIdColumnCount = 3;

    /// <summary>改前两条受管列的列宽；本常量让「缺陷确实存在过」成为可读数的断言。</summary>
    private const int PreChangeColumnMaxLength = 100;

    private const string Org = "org-001";
    private const string Env = "env-dev";

    [Fact]
    public void Governed_source_document_id_columns_all_match_the_policy_constant()
    {
        var governed = GovernedColumns();

        Assert.Equal(2, governed.Count);
        Assert.Contains("quality_hold_contexts", governed.Keys);
        Assert.Contains("quality_hold_transitions", governed.Keys);
        Assert.All(
            governed,
            column => Assert.Equal(MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength, column.Value));
        Assert.All(
            governed,
            column => Assert.True(
                column.Value > PreChangeColumnMaxLength,
                $"{column.Key}.{SourceDocumentIdColumn} 宽 {column.Value}，没有超过改前的 {PreChangeColumnMaxLength}。"));
    }

    [Fact]
    public async Task Source_identity_at_the_bound_is_captured_and_one_over_the_bound_is_dead_lettered()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.Parse("2026-09-10T04:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(Org, Env, "WO-3315-BOUND", "FG-3315", "PV-3315", 10m, 20, now.AddHours(8)));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = new QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext(dbContext, deadLetters);

        // 上界那一位：正常建保留上下文。长度从列宽常量派生，不手抄数字。
        var atBound = new string('a', MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength);
        await consumer.HandleAsync(RejectedEvent("evt-3315-at-bound", "QI-3315-1", atBound, now), CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        var hold = await dbContext.QualityHoldContexts.AsNoTracking().SingleAsync();
        Assert.Equal(atBound, hold.SourceDocumentId);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));

        // 上界加一位：进死信，**不抛出**。改前这一位抛 DbUpdateException 并逃逸出 HandleAsync。
        var overBound = new string('b', MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength + 1);
        await consumer.HandleAsync(RejectedEvent("evt-3315-over-bound", "QI-3315-2", overBound, now.AddMinutes(1)), CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        // 越界那一条既没建上下文，也没写时间线。
        Assert.Equal(atBound, (await dbContext.QualityHoldContexts.AsNoTracking().SingleAsync()).SourceDocumentId);
        Assert.All(
            await dbContext.QualityHoldTransitions.AsNoTracking().ToListAsync(),
            transition => Assert.Equal(atBound, transition.SourceDocumentId));
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal(MesQualityHoldSourceDocumentIdPolicy.OverlongFailureCode, deadLetter.FailureCode);
        Assert.Equal("evt-3315-over-bound", deadLetter.EventId);
    }

    [Fact]
    public void Overlong_failure_message_reports_the_length_without_echoing_the_value()
    {
        var overBound = new string('c', MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength + 1);

        Assert.False(MesQualityHoldSourceDocumentIdPolicy.ExceedsColumn(
            new string('c', MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength)));
        Assert.True(MesQualityHoldSourceDocumentIdPolicy.ExceedsColumn(overBound));

        var message = MesQualityHoldSourceDocumentIdPolicy.OverlongFailureMessage(overBound);
        Assert.Contains(overBound.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), message, StringComparison.Ordinal);
        Assert.Contains(
            MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(overBound, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 工作台**手工强制放行**这条同步写面的入口上界必须跟着列宽走（#3315 复审必改 1）。
    ///
    /// 改前它手抄 <c>MaximumLength(100)</c>，那时是**自洽的**：201/225 的复合来源身份根本存不下，
    /// 手工放行也就没有对象。列宽加宽之后这些上下文能存在了，若这条仍是 100，
    /// <c>ForceReleaseQualityHoldEndpoint</c> 会对**每一条复合来源身份**回 400 ——
    /// 自动放行仍走消费者不全堵，但手工那条路被这个裸字面量封死，
    /// 即「可达但手工放不掉」。这条用例把「入口上界 == 承载列宽」写成断言。
    /// </summary>
    [Fact]
    public void Manual_force_release_accepts_every_source_identity_the_consumer_can_persist()
    {
        var validator = new ForceReleaseQualityHoldCommandValidator();

        var atBound = validator.Validate(
            ForceReleaseCommand(new string('a', MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength)));
        Assert.True(
            atBound.IsValid,
            $"承载列宽那一位必须放行，实际错误：{string.Join("；", atBound.Errors)}");

        var overBound = validator.Validate(
            ForceReleaseCommand(new string('a', MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength + 1)));
        Assert.False(overBound.IsValid, "超出承载列宽那一位必须拒绝。");
        // PropertyName 用 OrdinalIgnoreCase 比对：app.UseFastEndpoints(...) 启动时会把
        // ValidatorOptions.Global.PropertyNameResolver 换成 camelCase 解析器且不还原，同程序集里只要有
        // 用例先启动过 host，这里拿到的就是 camelCase 名（#3342）。容忍的只有这一维进程级大小写差异——
        // 成员名写成别的成员或不存在的名字照样红（#3342 的变异矩阵为此各跑了一格）。
        // 这几条规则用的是 FluentValidation 默认文案，文案里嵌的正是同一个受解析器影响的展示名，
        // 所以这里不能像 #3342 其余位点那样改断 ErrorMessage。
        Assert.Contains(
            overBound.Errors,
            error => string.Equals(error.PropertyName, nameof(ForceReleaseQualityHoldCommand.SourceDocumentId), StringComparison.OrdinalIgnoreCase));

        // 改前上界在新列宽下会把合法的首件/周期检来源身份拒在门外——这条读数说明缺陷不是假想的。
        Assert.True(
            PreChangeColumnMaxLength < MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength,
            "改前入口上界没有低于承载列宽，本用例失去针对性。");
    }

    /// <summary>
    /// **别把两个同名字段当成同一个面**（#3315 复审的显式禁令）：
    /// <c>ConvertPlanToWorkOrderCommandValidator</c> 的 <c>SourceDocumentId</c> 落到
    /// <c>work_orders.source_document_id</c>（DemandPlanning 计划单溯源，具名豁免面），
    /// 它的上界必须继续跟着**那一列**走，不得被「顺手一起改成 250」。
    /// 上界从 EF 模型读，不手抄。
    /// </summary>
    [Fact]
    public void Work_order_planning_source_face_keeps_its_own_narrower_bound()
    {
        var exemptedWidth = ExemptedColumnWidth();
        Assert.True(
            exemptedWidth < MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength,
            "豁免面与受管面若同宽，本用例退化为同义反复，需要换取值。");

        var validator = new ConvertPlanToWorkOrderCommandValidator();
        Assert.True(
            validator.Validate(ConvertPlanCommand(new string('p', exemptedWidth))).IsValid,
            "豁免面列宽那一位必须放行。");
        var overBound = validator.Validate(ConvertPlanCommand(new string('p', exemptedWidth + 1)));
        Assert.False(overBound.IsValid, "豁免面不得被放宽到受管面的列宽。");
        // PropertyName 用 OrdinalIgnoreCase 比对：app.UseFastEndpoints(...) 启动时会把
        // ValidatorOptions.Global.PropertyNameResolver 换成 camelCase 解析器且不还原，同程序集里只要有
        // 用例先启动过 host，这里拿到的就是 camelCase 名（#3342）。容忍的只有这一维进程级大小写差异——
        // 成员名写成别的成员或不存在的名字照样红（#3342 的变异矩阵为此各跑了一格）。
        // 这几条规则用的是 FluentValidation 默认文案，文案里嵌的正是同一个受解析器影响的展示名，
        // 所以这里不能像 #3342 其余位点那样改断 ErrorMessage。
        Assert.Contains(
            overBound.Errors,
            error => string.Equals(error.PropertyName, nameof(ConvertPlanToWorkOrderCommand.SourceDocumentId), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// **为什么守卫只能拒绝、不能截断**（#3315 复审必改 2：折叠此前只是设计推理，这里把它证出来）。
    ///
    /// 证据链四段，全部可核：
    /// <list type="number">
    /// <item>两条来源身份**互异**；</item>
    /// <item>把它们**截断到承载列宽**之后**变成同一个字符串**（纯函数事实）；</item>
    /// <item>保留上下文的去重键 <c>ux_quality_hold_contexts_scope_source</c> **含 <c>source_document_id</c>**
    /// ——从 EF 模型读索引成员，不是从注释推断；</item>
    /// <item>当前实现对这两条**都拒绝**（各一条死信、零行保留上下文），因此不存在折叠。</item>
    /// </list>
    /// ①②③ 合起来说明：一旦改成截断，这两条互异身份就会命中同一条去重键 ⇒ 后判定的那道工序
    /// 静默复用前一道的结论。变异读数见 PR 正文 M12（截断变异下两条互异身份只落 1 行）。
    /// </summary>
    [Fact]
    public async Task Truncating_to_the_column_width_would_fold_two_distinct_identities_so_the_guard_rejects_both()
    {
        var sharedPrefix = new string('a', MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength);
        var alpha = sharedPrefix + "-alpha";
        var beta = sharedPrefix + "-beta";

        // ① 互异；② 截断到列宽后同键。
        Assert.NotEqual(alpha, beta);
        Assert.Equal(
            alpha[..MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength],
            beta[..MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength]);

        // ③ 去重键确实含这一列。
        Assert.Contains(
            SourceDocumentIdColumn,
            ScopeSourceUniqueIndexColumns(),
            StringComparer.Ordinal);

        // ④ 当前实现两条都拒绝，零折叠。
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.Parse("2026-09-10T06:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(Org, Env, "WO-3315-FOLD", "FG-3315", "PV-3315", 10m, 20, now.AddHours(8)));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = new QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext(dbContext, deadLetters);
        await consumer.HandleAsync(RejectedEvent("evt-3315-fold-alpha", "QI-3315-A", alpha, now, "WO-3315-FOLD"), CancellationToken.None);
        await consumer.HandleAsync(RejectedEvent("evt-3315-fold-beta", "QI-3315-B", beta, now.AddMinutes(1), "WO-3315-FOLD"), CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        // 截断实现下这里会读到 **1** 行（两条互异身份折叠成一条）；拒绝实现下是 0 行。
        // 失败消息里直接把折叠事实打出来，别让后来人只看到「集合非空」而读不出成因。
        var holds = await dbContext.QualityHoldContexts.AsNoTracking().ToListAsync();
        Assert.True(
            holds.Count == 0,
            holds.Count == 1
                ? $"两条互异来源身份（长度 {alpha.Length} / {beta.Length}）只落了 1 行，"
                    + $"该行来源身份长度 {holds[0].SourceDocumentId.Length}、与两者的公共前缀"
                    + $"{(string.Equals(holds[0].SourceDocumentId, alpha[..MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength], StringComparison.Ordinal) ? "相同" : "不同")}"
                    + " —— 两条身份被折叠成同一条去重键。"
                : $"两条互异来源身份必须都被拒绝，实际落了 {holds.Count} 行。");
        var letters = await deadLetters.ListAsync(null, null, CancellationToken.None);
        Assert.Equal(2, letters.Count);
        Assert.All(
            letters,
            letter => Assert.Equal(MesQualityHoldSourceDocumentIdPolicy.OverlongFailureCode, letter.FailureCode));
    }

    /// <summary>
    /// 受管的 <c>source_document_id</c> 列，从 EF 模型**闭集枚举**：新增一张带该列的表会自动进值域
    /// 并让计数断言红，而不是静默漏掉（<c>Assert.All</c> 对空集恒真，护栏会缴械）。
    /// </summary>
    private static IReadOnlyDictionary<string, int> GovernedColumns()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var model = dbContext.Model;
        var columns = model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => string.Equals(property.GetColumnName(), SourceDocumentIdColumn, StringComparison.Ordinal))
            .Select(property => (Table: property.DeclaringType.GetTableName() ?? string.Empty, Property: property))
            .ToArray();
        Assert.Equal(SourceDocumentIdColumnCount, columns.Length);

        var exempted = columns
            .Where(column => string.Equals(column.Table, ExemptedTable, StringComparison.Ordinal))
            .ToArray();
        Assert.Single(exempted);

        var governed = columns.Except(exempted).ToArray();
        // 豁免 + 受管 == 闭集总数：不许出现「既不在豁免名单、也没被受管断言覆盖」的第三类。
        Assert.Equal(SourceDocumentIdColumnCount, governed.Length + exempted.Length);
        return governed.ToDictionary(
            column => column.Table,
            column => column.Property.GetMaxLength()
                ?? throw new InvalidOperationException($"{column.Table}.{SourceDocumentIdColumn} 没有声明列宽。"),
            StringComparer.Ordinal);
    }

    private static InspectionResultIntegrationEvent RejectedEvent(
        string eventId,
        string inspectionRecordId,
        string sourceDocumentId,
        DateTimeOffset occurredAtUtc,
        string workOrderId = "WO-3315-BOUND") =>
        MesInspectionResultEventFactory.Create(
            eventId,
            QualityIntegrationEventTypes.InspectionRejected,
            inspectionRecordId,
            sourceDocumentId,
            occurredAtUtc,
            "PLAN-3315",
            "FG-3315",
            QualityInspectionSourceServices.Mes,
            sourceType: QualityInspectionSourceTypes.FirstArticle,
            workOrderId: workOrderId);

    private static ForceReleaseQualityHoldCommand ForceReleaseCommand(string sourceDocumentId) =>
        new(
            Org,
            Env,
            "business-mes",
            sourceDocumentId,
            "supervisor override",
            "supervisor-001",
            "corr-3315",
            "idem-3315",
            DateTimeOffset.Parse("2026-09-10T06:30:00Z"));

    private static ConvertPlanToWorkOrderCommand ConvertPlanCommand(string sourceDocumentId) =>
        new(
            Org,
            Env,
            "PP-3315",
            null,
            DateTimeOffset.Parse("2026-09-10T06:30:00Z"),
            "FG-3315",
            null,
            10m,
            "kg",
            DateTimeOffset.Parse("2026-09-11T06:30:00Z"),
            null,
            SourceDocumentId: sourceDocumentId);

    /// <summary>具名豁免面 <c>work_orders.source_document_id</c> 的列宽，从 EF 模型读，不手抄。</summary>
    private static int ExemptedColumnWidth()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var property = Assert.Single(
            dbContext.Model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()),
            candidate =>
                string.Equals(candidate.DeclaringType.GetTableName(), ExemptedTable, StringComparison.Ordinal)
                && string.Equals(candidate.GetColumnName(), SourceDocumentIdColumn, StringComparison.Ordinal));
        return property.GetMaxLength()
            ?? throw new InvalidOperationException($"{ExemptedTable}.{SourceDocumentIdColumn} 没有声明列宽。");
    }

    /// <summary>保留上下文去重键 <c>ux_quality_hold_contexts_scope_source</c> 的列成员，从 EF 模型读。</summary>
    private static string[] ScopeSourceUniqueIndexColumns()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var index = Assert.Single(
            dbContext.Model.GetEntityTypes().SelectMany(entityType => entityType.GetIndexes()),
            candidate => string.Equals(
                candidate.GetDatabaseName(),
                "ux_quality_hold_contexts_scope_source",
                StringComparison.Ordinal));
        Assert.True(index.IsUnique, "该索引必须是唯一索引，否则它不是去重键，本用例的推理不成立。");
        return index.Properties.Select(property => property.GetColumnName()).ToArray();
    }

    private static async Task<SqliteConnection> CreateOpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static ApplicationDbContext CreateSqliteDbContext(SqliteConnection connection) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options,
            SourceDocumentIdContractNoopMediator.Instance);

    private static ApplicationDbContext CreateModelOnlyDbContext() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=nerv_iip_mes_source_document_id_contract;Username=nerv;Password=nerv",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema))
                .Options,
            SourceDocumentIdContractNoopMediator.Instance);

    private sealed class SourceDocumentIdContractNoopMediator : IMediator
    {
        internal static readonly SourceDocumentIdContractNoopMediator Instance = new();

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
