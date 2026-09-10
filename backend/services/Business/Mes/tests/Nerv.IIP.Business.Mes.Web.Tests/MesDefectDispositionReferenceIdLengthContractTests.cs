using System.Globalization;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// GitHub #3318：MES 侧承载 Quality 处置引用身份的那一列，与守卫常量之间的对撞。
///
/// 这个类钉住四件事：
/// <list type="bullet">
/// <item><see cref="Governed_disposition_reference_id_columns_all_match_the_policy_constant"/>：列宽从
/// **EF 模型闭集枚举**读，不读迁移文本、也不读第二份常量 → 单边改 <c>HasMaxLength</c>、
/// 或新增一张带该列的表，都会红；</item>
/// <item><see cref="Disposition_reference_at_the_bound_is_persisted_and_one_over_the_bound_is_dead_lettered"/>：
/// 跑**真实消费者**，上界那一位落库、+1 位进死信；关键读数是「+1 位**不抛出**」——
/// 改前那一位抛的是 <c>DbUpdateException</c> 且逃逸成 poison message（#877，本票 R0 已在真库实测）；</item>
/// <item><see cref="Overlong_failure_message_reports_the_length_without_echoing_the_value"/>：
/// 死信消息只报长度不回显取值，别让 <c>failure_message</c> 自己变成第二个越界写面；</item>
/// <item><see cref="Truncating_the_disposition_reference_would_silently_forge_a_downstream_reference_so_the_guard_rejects_it"/>：
/// **本处的截断危害与 #3315 不是同一个**——那边是折叠（去重键含该列），本处该列不参与任何索引
/// （从 EF 模型枚举索引成员证出来），危害是静默伪造下游引用。这条把两件事都写成断言。</item>
/// </list>
///
/// **合同分类**（<c>docs/governance/testing/validity.md</c>）：<c>ProviderBehavior</c> + <c>Regression</c>。
/// <c>ProviderBehavior</c> 的权威来源是 MES 的 EntityConfiguration / migration 列宽约束；
/// <c>Regression</c> 的权威来源是 GitHub #3318（错误行为「22001 逃逸成 poison message」、
/// 期望行为「同一输入不再卡链」）。
///
/// **证明范围（别读成完备）**：本类跑在 **SQLite** 上，SQLite 不执行 <c>varchar(n)</c> 长度约束，
/// 因此本类的绿**不能**读成「落库不会 22001」——它证明的是**守卫本身**在上界两侧的行为差异。
/// 真库那一面由 <c>Nerv.IIP.Business.Acceptance.Tests.MesDefectDispositionReferenceAcceptanceTests</c>
/// 的 <c>postgres:18</c> 读数承担；「Quality 侧三个产出列没被单边放宽」由
/// <c>Nerv.IIP.Business.Acceptance.Tests.NcrDispositionReferenceCrossServiceWidthContractTests</c> 承担。
/// 本类也**不**证明「没有别的路径把值写进那一列」——那类源码文本扫描在 #3176 / PR #3214 三轮实证不收敛，已按裁定不建。
/// </summary>
public sealed class MesDefectDispositionReferenceIdLengthContractTests
{
    private const string DispositionReferenceIdColumn = "disposition_reference_id";
    private const string DefectRecordsTable = "defect_records";

    /// <summary>
    /// MES 模型里带 <c>disposition_reference_id</c> 列的表的**闭集**。本面**没有**具名豁免：
    /// 该列名在模型里唯一。枚举结果与本集合按序比对（不是「包含」），因此新增一张带同名列的表会红，
    /// 而不是静默漏掉（<c>Assert.All</c> 对空集恒真，护栏会缴械）。
    /// </summary>
    private static readonly string[] DispositionReferenceIdTables = [DefectRecordsTable];

    /// <summary>改前该列的列宽；本常量让「缺陷确实存在过」成为可读数的断言而不是注释。</summary>
    private const int PreChangeColumnMaxLength = 100;

    private const string Org = "org-001";
    private const string Env = "env-dev";
    private const string WorkOrderId = "WO-3318";
    private const string DefectNo = "DEF-3318";

    [Fact]
    public void Governed_disposition_reference_id_columns_all_match_the_policy_constant()
    {
        var governed = GovernedColumns();

        Assert.Equal(
            DispositionReferenceIdTables,
            governed.Keys.OrderBy(table => table, StringComparer.Ordinal).ToArray());
        Assert.All(
            governed,
            column => Assert.Equal(MesDefectDispositionReferenceIdPolicy.ColumnMaxLength, column.Value));
        Assert.All(
            governed,
            column => Assert.True(
                column.Value > PreChangeColumnMaxLength,
                $"{column.Key}.{DispositionReferenceIdColumn} 宽 {column.Value}，没有超过改前的 {PreChangeColumnMaxLength}。"));
    }

    [Fact]
    public async Task Disposition_reference_at_the_bound_is_persisted_and_one_over_the_bound_is_dead_lettered()
    {
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        await SeedDefectAsync(dbContext, DefectNo);

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(dbContext, deadLetters);

        // 上界那一位：正常回写。长度从列宽常量派生，不手抄数字。
        var atBound = new string('a', MesDefectDispositionReferenceIdPolicy.ColumnMaxLength);
        await consumer.HandleAsync(DispositionEvent("evt-3318-at-bound", DefectNo, atBound), CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(atBound, (await dbContext.DefectRecords.AsNoTracking().SingleAsync()).DispositionReferenceId);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));

        // 上界加一位：进死信，**不抛出**。改前这一位抛 DbUpdateException(22001) 并逃逸出 HandleAsync。
        var overBound = new string('b', MesDefectDispositionReferenceIdPolicy.ColumnMaxLength + 1);
        await consumer.HandleAsync(DispositionEvent("evt-3318-over-bound", DefectNo, overBound), CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        // 越界那一条没有改动缺陷记录的处置引用。
        Assert.Equal(atBound, (await dbContext.DefectRecords.AsNoTracking().SingleAsync()).DispositionReferenceId);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal(MesDefectDispositionReferenceIdPolicy.OverlongFailureCode, deadLetter.FailureCode);
        Assert.Equal("evt-3318-over-bound", deadLetter.EventId);
    }

    [Fact]
    public void Overlong_failure_message_reports_the_length_without_echoing_the_value()
    {
        var overBound = new string('c', MesDefectDispositionReferenceIdPolicy.ColumnMaxLength + 1);

        Assert.False(MesDefectDispositionReferenceIdPolicy.ExceedsColumn(
            new string('c', MesDefectDispositionReferenceIdPolicy.ColumnMaxLength)));
        Assert.True(MesDefectDispositionReferenceIdPolicy.ExceedsColumn(overBound));

        var message = MesDefectDispositionReferenceIdPolicy.OverlongFailureMessage(overBound);
        Assert.Contains(overBound.Length.ToString(CultureInfo.InvariantCulture), message, StringComparison.Ordinal);
        Assert.Contains(
            MesDefectDispositionReferenceIdPolicy.ColumnMaxLength.ToString(CultureInfo.InvariantCulture),
            message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(overBound, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// **为什么守卫只能拒绝、不能截断——本处的理由与 #3315 不是同一个，别照抄那句话。**
    ///
    /// #3315 的截断危害是**折叠**：<c>ux_quality_hold_contexts_scope_source</c> 按来源身份去重，
    /// 两道工序的首件结论会被折叠成同一条。这条用例先把「本处有没有那个风险」实测出来：
    /// <list type="number">
    /// <item>从 EF 模型枚举 <c>defect_records</c> 的**全部**索引成员（不只唯一索引），
    /// 断言 <c>disposition_reference_id</c> **不在任何一条里** ⇒ **本处不存在折叠风险**；</item>
    /// <item>反同义反复：同一次枚举必须真的读得到成员——<c>defect_no</c> 必须出现在某条**唯一**索引里。
    /// 少了这一段，「枚举结果为空」和「该列不参与索引」读起来一样。</item>
    /// </list>
    ///
    /// 于是本处截断的真实危害是另一件事：两条互异的处置引用被截到同一个前缀之后，
    /// 落库的值既**不等于** Quality 发来的引用（静默伪造一个指不到任何对象的下游引用），
    /// 行上也没有任何字段记录它被截断过。第 ③ 段把「当前实现两条都拒绝、一个字节都不改」写成断言，
    /// 失败消息直接自述截断事实。
    /// </summary>
    [Fact]
    public async Task Truncating_the_disposition_reference_would_silently_forge_a_downstream_reference_so_the_guard_rejects_it()
    {
        var sharedPrefix = new string('a', MesDefectDispositionReferenceIdPolicy.ColumnMaxLength);
        var alpha = sharedPrefix + "-alpha";
        var beta = sharedPrefix + "-beta";

        // ① 互异；截断到列宽后是同一个字符串（纯函数事实）。
        Assert.NotEqual(alpha, beta);
        Assert.Equal(
            alpha[..MesDefectDispositionReferenceIdPolicy.ColumnMaxLength],
            beta[..MesDefectDispositionReferenceIdPolicy.ColumnMaxLength]);

        // ② 本处没有以该列为成员的去重键 —— 与 #3315 相反，这里如实证出来而不是照抄那边的理由。
        var indexes = DefectRecordIndexColumns();
        Assert.DoesNotContain(
            indexes,
            index => index.Columns.Contains(DispositionReferenceIdColumn, StringComparer.Ordinal));
        // 反同义反复：这次枚举确实读得到索引成员，否则上面那条对空集恒真。
        Assert.Contains(
            indexes,
            index => index.IsUnique && index.Columns.Contains("defect_no", StringComparer.Ordinal));

        // ③ 当前实现对两条都拒绝，缺陷记录上的处置引用一个字节都没被改写。
        await using var connection = await CreateOpenSqliteConnectionAsync();
        await using var dbContext = CreateSqliteDbContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        await SeedDefectAsync(dbContext, "DEF-3318-A");
        await SeedDefectAsync(dbContext, "DEF-3318-B");

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(dbContext, deadLetters);
        await consumer.HandleAsync(DispositionEvent("evt-3318-fold-alpha", "DEF-3318-A", alpha), CancellationToken.None);
        await consumer.HandleAsync(DispositionEvent("evt-3318-fold-beta", "DEF-3318-B", beta), CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var persisted = await dbContext.DefectRecords
            .AsNoTracking()
            .OrderBy(x => x.DefectNo)
            .Select(x => new { x.DefectNo, x.DispositionReferenceId })
            .ToListAsync();
        Assert.All(
            persisted,
            row => Assert.True(
                row.DispositionReferenceId is null,
                row.DispositionReferenceId is null
                    ? string.Empty
                    : $"{row.DefectNo} 落了处置引用（长度 {row.DispositionReferenceId.Length}），"
                        + $"而 Quality 发来的是长度 {alpha.Length} / {beta.Length} 的两条互异引用；"
                        + (string.Equals(row.DispositionReferenceId, sharedPrefix, StringComparison.Ordinal)
                            ? "落库值恰好等于两者截断到列宽后的公共前缀 —— 引用被截断成了一个指不到任何对象的伪引用，"
                                + "且行上没有任何字段记录这件事。"
                            : "落库值不是截断值 —— 守卫根本没有拦住这条越界引用，真库上这一位就是 22001。")));
        var letters = await deadLetters.ListAsync(null, null, CancellationToken.None);
        Assert.Equal(2, letters.Count);
        Assert.All(
            letters,
            letter => Assert.Equal(MesDefectDispositionReferenceIdPolicy.OverlongFailureCode, letter.FailureCode));
    }

    /// <summary>
    /// 受管的 <c>disposition_reference_id</c> 列，从 EF 模型**闭集枚举**（不是手写白名单）：
    /// 新增一张带该列的表会自动进值域并让计数断言红。
    /// </summary>
    private static IReadOnlyDictionary<string, int> GovernedColumns()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var columns = dbContext.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => string.Equals(property.GetColumnName(), DispositionReferenceIdColumn, StringComparison.Ordinal))
            .Select(property => (Table: property.DeclaringType.GetTableName() ?? string.Empty, Property: property))
            .ToArray();
        Assert.Equal(
            DispositionReferenceIdTables,
            columns.Select(column => column.Table).OrderBy(table => table, StringComparer.Ordinal).ToArray());
        return columns.ToDictionary(
            column => column.Table,
            column => column.Property.GetMaxLength()
                ?? throw new InvalidOperationException($"{column.Table}.{DispositionReferenceIdColumn} 没有声明列宽。"),
            StringComparer.Ordinal);
    }

    /// <summary><c>defect_records</c> 的全部索引及其列成员，从 EF 模型读，不从注释或迁移文本推断。</summary>
    private static IReadOnlyList<(bool IsUnique, string[] Columns)> DefectRecordIndexColumns()
    {
        using var dbContext = CreateModelOnlyDbContext();
        var entityType = Assert.Single(
            dbContext.Model.GetEntityTypes(),
            candidate => string.Equals(candidate.GetTableName(), DefectRecordsTable, StringComparison.Ordinal));
        return entityType.GetIndexes()
            .Select(index => (
                index.IsUnique,
                Columns: index.Properties.Select(property => property.GetColumnName()).ToArray()))
            .ToArray();
    }

    private static async Task SeedDefectAsync(ApplicationDbContext dbContext, string defectNo)
    {
        var now = DateTimeOffset.Parse("2026-09-10T04:00:00Z");
        if (!await dbContext.WorkOrders.AnyAsync())
        {
            dbContext.WorkOrders.Add(WorkOrder.Create(Org, Env, WorkOrderId, "FG-3318", "PV-3318", 10m, 20, now.AddHours(8)));
        }

        dbContext.DefectRecords.Add(DefectRecord.Create(Org, Env, defectNo, WorkOrderId, "OP-10", "SURFACE", 1m, now));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static NcrDispositionDecidedIntegrationEvent DispositionEvent(
        string eventId,
        string defectNo,
        string reworkWorkOrderId) =>
        new(
            eventId,
            QualityIntegrationEventTypes.DispositionDecided,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-09-10T05:00:00Z"),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-3318",
            $"cause-{eventId}",
            Org,
            Env,
            "quality",
            $"quality:disposition:{eventId}",
            new NcrDispositionDecidedPayload(
                "NCR-3318",
                "NCR-2026-3318",
                "FG-3318",
                1m,
                QualityNcrDispositionTypes.Rework,
                "approval-3318",
                reworkWorkOrderId,
                null,
                null,
                DateTimeOffset.Parse("2026-09-10T05:00:00Z"))
            {
                SourceDocumentId = defectNo,
            });

    private static async Task<SqliteConnection> CreateOpenSqliteConnectionAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static ApplicationDbContext CreateSqliteDbContext(SqliteConnection connection) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options,
            DispositionReferenceContractNoopMediator.Instance);

    private static ApplicationDbContext CreateModelOnlyDbContext() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(
                    "Host=localhost;Database=nerv_iip_mes_disposition_reference_contract;Username=nerv;Password=nerv",
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema))
                .Options,
            DispositionReferenceContractNoopMediator.Instance);

    private sealed class DispositionReferenceContractNoopMediator : IMediator
    {
        internal static readonly DispositionReferenceContractNoopMediator Instance = new();

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
