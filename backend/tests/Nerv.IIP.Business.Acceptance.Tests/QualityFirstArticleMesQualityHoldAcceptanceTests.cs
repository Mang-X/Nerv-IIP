using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// GitHub #3315：Quality 的复合来源身份逐字写进 MES 保留上下文/时间线两列。
///
/// **为什么必须跑真实 PostgreSQL**：本类要钉住的失败是**列宽溢出**——改前
/// <c>quality_hold_contexts.source_document_id</c> 与 <c>quality_hold_transitions.source_document_id</c>
/// 都是 <c>varchar(100)</c>，而首件复合身份 <c>{workOrderId}:{operationTaskId}</c> 上界 201。
/// EF InMemory / SQLite 不带列长约束，在它们上面这条永远是绿的，不承重。
/// 改前实测读数（<c>postgres:18</c>，PR 正文 R0）：
/// <c>DbUpdateException ---- Npgsql.PostgresException : 22001: value too long for type character varying(100)</c>，
/// 且**逃逸出** <c>HandleAsync</c>（栈顶经 <c>IntegrationEventConsumerGuard</c> 直达用例），
/// 即命中 #877 的 poison message 形状。
///
/// **合同分类**（<c>docs/governance/testing/validity.md</c> 六类合同来源）：
/// <list type="bullet">
/// <item><c>ProviderBehavior</c>：权威来源是 migration / schema 约束
/// （<c>quality_hold_contexts.source_document_id</c> 的物理类型）与 PostgreSQL 对超宽赋值抛
/// <c>22001</c> 的官方行为。预期值由该来源推导，不是从当前实现输出反抄的。</item>
/// <item><c>Regression</c>：权威来源是 GitHub #3315 正文（错误行为「22001 逃逸成 poison message」、
/// 期望行为「同一输入不再卡链」），本类三条用例即最小复现，改前实测失败。</item>
/// </list>
///
/// **证明范围**：本类只跑 MES 一侧的消费者 + 真实 PostgreSQL，**不能**证明 CAP 传输、重投或完整服务拓扑；
/// 「Quality 侧列宽没被单边改窄」由
/// <see cref="QualitySourceDocumentIdCrossServiceWidthContractTests"/> 承担，不由本类看守。
/// </summary>
[Collection(AcceptancePostgresLaneDatabase.CollectionName)]
public sealed class QualityFirstArticleMesQualityHoldAcceptanceTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";
    private const string Sku = "SKU-FG-3315";

    /// <summary>
    /// 本票产出的迁移。用例按它定位「改前那一版 schema」，因此这个名字必须在迁移清单里存在——
    /// 重命名/删除迁移会红在 <see cref="Postgres_widened_column_keeps_matching_a_hold_row_written_before_the_migration"/>，
    /// 而不是让那条用例静默退化成「在新 schema 上写了一行又读回来」。
    /// </summary>
    private const string WidenMigrationId =
        "20260910072329_WidenMesQualityHoldSourceDocumentIdForQualityCompositeIdentity";

    [RealPostgresFact]
    public async Task Postgres_first_article_hold_context_at_the_mes_identity_upper_bound_is_persisted_without_escaping_the_consumer()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync(MesFacts.Schema);
        await using var dbContext = CreateMesDbContext();
        await dbContext.Database.MigrateAsync();
        AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);

        // 两段长度取自 **MES EF 模型**里工单 id / 工序任务 id 的列宽（各 100，#2976 已坐实该上界可构造），
        // 复合后即改前 varchar(100) 的直接溢出输入。这里不手抄 97 / 201：Quality 改了复合拼法、
        // 或 MES 改了这两列的宽度，下面的等式都会红。
        var workOrderIdWidth = ColumnWidth(dbContext, "work_orders", "work_order_id");
        var operationTaskIdWidth = ColumnWidth(dbContext, "operation_tasks", "operation_task_id");
        var workOrderId = Identity("WO-", workOrderIdWidth);
        var operationTaskId = Identity("OP-", operationTaskIdWidth);
        await SeedMesExecutionAsync(dbContext, workOrderId, operationTaskId);

        var integrationEvent = FirstArticleRejectedEvent(workOrderId, operationTaskId);
        // 分隔符长度也从生产代码测出来，不写 ":"。
        var separatorLength = FirstArticleInspection.SourceDocumentId("a", "b").Length - 2;
        Assert.Equal(
            workOrderIdWidth + separatorLength + operationTaskIdWidth,
            integrationEvent.Payload.SourceDocumentId.Length);
        Assert.True(
            integrationEvent.Payload.SourceDocumentId.Length > PreChangeColumnMaxLength,
            $"首件复合来源身份 {integrationEvent.Payload.SourceDocumentId.Length} 未超过改前列宽 {PreChangeColumnMaxLength}，本用例失去针对性。");

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = new QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext(dbContext, deadLetters);

        await consumer.HandleAsync(integrationEvent, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var hold = await dbContext.QualityHoldContexts.AsNoTracking().SingleAsync();
        Assert.Equal(integrationEvent.Payload.SourceDocumentId, hold.SourceDocumentId);
        Assert.True(hold.Active);
        // 时间线那一列同样是 100 宽的写面，被 hold-applied 这一条覆盖到。
        var transition = await dbContext.QualityHoldTransitions.AsNoTracking().SingleAsync();
        Assert.Equal(integrationEvent.Payload.SourceDocumentId, transition.SourceDocumentId);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    /// <summary>
    /// 守卫这一层的真库读数：**超出新列宽**的来源身份走死信，而不是 22001 逃逸。
    /// 用**持久化**死信存储而非内存桩——否则「fail-closed 那条路自己会不会炸库」没有被证明。
    /// </summary>
    [RealPostgresFact]
    public async Task Postgres_source_identity_beyond_the_column_width_is_dead_lettered_instead_of_escaping_the_consumer()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync(MesFacts.Schema);
        await using var dbContext = CreateMesDbContext();
        await dbContext.Database.MigrateAsync();

        // 上界那一位由上一条用例的 201 覆盖（真实可达输入）；这里构造的是「上界 + 1」，
        // 两段长度都从列宽常量派生，不手抄数字。
        var workOrderSegment = Identity("WO-", ColumnWidth(dbContext, "work_orders", "work_order_id"));
        var operationSegment = new string(
            'O',
            MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength - workOrderSegment.Length);

        // **必须让这条事件的执行对象真的存在**，否则删掉守卫时它会落到既有的 unknown-source-document
        // 分支，本用例就变成「守卫和那条分支都能挡住」——挡不住的那一支（22001 逃逸）反而测不到。
        // 工序段有 150 字符、超过 operation_tasks.operation_task_id 的 100，无法落库；工单段恰好 100，
        // 因此只播工单：ResolveMesSourceAsync 先按工序找不到、再按工单命中，源头解析成功后照旧去写那两列。
        await SeedMesExecutionAsync(dbContext, workOrderSegment, "OP-3315-GUARD-10");
        var integrationEvent = FirstArticleRejectedEvent(workOrderSegment, operationSegment);
        Assert.Equal(
            MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength + 1,
            integrationEvent.Payload.SourceDocumentId.Length);

        var deadLetters = new PersistentIntegrationEventDeadLetterStore<MesDbContext>(dbContext);
        var consumer = new QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext(dbContext, deadLetters);

        // 不抛出：这一句本身就是「不再是 poison message」的读数。
        await consumer.HandleAsync(integrationEvent, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        Assert.Empty(await dbContext.QualityHoldContexts.AsNoTracking().ToListAsync());
        Assert.Empty(await dbContext.QualityHoldTransitions.AsNoTracking().ToListAsync());
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext.ConsumerName,
            null,
            CancellationToken.None));
        Assert.Equal(MesQualityHoldSourceDocumentIdPolicy.OverlongFailureCode, deadLetter.FailureCode);
        // 死信消息只报长度、不回显取值：越界取值天然无上界，回显会让 failure_message 也变成越界写面。
        Assert.Contains(
            (MesQualityHoldSourceDocumentIdPolicy.ColumnMaxLength + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            deadLetter.FailureMessage,
            StringComparison.Ordinal);
        Assert.DoesNotContain(integrationEvent.Payload.SourceDocumentId, deadLetter.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 去重语义不得漂移：<c>varchar(100) → varchar(250)</c> 之后，**迁移前**写下的那一行
    /// 仍然被 <c>QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext</c>
    /// 按 <c>(org, env, source_service, source_document_id)</c> 命中并更新，而不是新增第二行。
    ///
    /// 这条必须跨迁移跑：在新 schema 上写一行再读回来，证明不了「旧行还找得到」。
    /// </summary>
    [RealPostgresFact]
    public async Task Postgres_widened_column_keeps_matching_a_hold_row_written_before_the_migration()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync(MesFacts.Schema);
        await using var dbContext = CreateMesDbContext();

        var migrations = dbContext.Database.GetMigrations().ToArray();
        var widenIndex = Array.IndexOf(migrations, WidenMigrationId);
        Assert.True(widenIndex > 0, $"迁移 {WidenMigrationId} 必须存在于迁移清单中，否则本用例失去跨迁移语义。");
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[widenIndex - 1]);

        var workOrderId = "WO-3315-LEGACY";
        var operationTaskId = "OP-3315-LEGACY-10";
        await SeedMesExecutionAsync(dbContext, workOrderId, operationTaskId);

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = new QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext(dbContext, deadLetters);
        var rejected = FirstArticleRejectedEvent(workOrderId, operationTaskId);
        await consumer.HandleAsync(rejected, CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        var legacyHold = await dbContext.QualityHoldContexts.AsNoTracking().SingleAsync();
        Assert.True(legacyHold.Active);

        // 加宽迁移在既有行之上执行。
        await migrator.MigrateAsync();
        dbContext.ChangeTracker.Clear();

        var passed = FirstArticlePassedEvent(workOrderId, operationTaskId);
        Assert.Equal(rejected.Payload.SourceDocumentId, passed.Payload.SourceDocumentId);
        await consumer.HandleAsync(passed, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        // 命中既有行：仍然只有一行，且 id 未变、状态被更新——不是新插了一行。
        var updated = await dbContext.QualityHoldContexts.AsNoTracking().SingleAsync();
        Assert.Equal(legacyHold.Id, updated.Id);
        Assert.False(updated.Active);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    /// <summary>改前两条承载列的列宽；让「缺陷确实存在过」成为可读数的断言而不是注释。</summary>
    private const int PreChangeColumnMaxLength = 100;

    /// <summary>把身份补齐到指定列宽，前缀只是为了失败消息可读。</summary>
    private static string Identity(string prefix, int width) => prefix + new string('9', width - prefix.Length);

    /// <summary>列宽从 EF 模型读，不手抄。</summary>
    private static int ColumnWidth(MesDbContext dbContext, string table, string column)
    {
        var property = Assert.Single(
            dbContext.Model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()),
            candidate =>
                string.Equals(candidate.DeclaringType.GetTableName(), table, StringComparison.Ordinal)
                && string.Equals(candidate.GetColumnName(), column, StringComparison.Ordinal));
        return property.GetMaxLength()
            ?? throw new InvalidOperationException($"{table}.{column} 没有声明列宽。");
    }

    private static async Task SeedMesExecutionAsync(MesDbContext dbContext, string workOrderId, string operationTaskId)
    {
        var now = DateTimeOffset.Parse("2026-09-10T02:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(Org, Env, workOrderId, Sku, "PV-3315-1", 10m, 20, now.AddHours(8)));
        dbContext.OperationTasks.Add(OperationTask.Create(
            Org,
            Env,
            workOrderId,
            operationTaskId,
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-3315",
            [],
            now,
            TimeSpan.FromMinutes(45),
            null,
            null,
            Sku));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }

    private static InspectionResultIntegrationEvent FirstArticleRejectedEvent(string workOrderId, string operationTaskId)
    {
        var record = FirstArticleRecord(
            workOrderId,
            operationTaskId,
            new InspectionResultLineInput("appearance", "scratch", null, InspectionLineResults.Failed, "surface-defect", 1m, []),
            "critical-defect");
        return new InspectionRejectedIntegrationEventConverter(new FixedFirstArticleQualityContextAccessor())
            .Convert(new InspectionRejectedDomainEvent(record));
    }

    private static InspectionResultIntegrationEvent FirstArticlePassedEvent(string workOrderId, string operationTaskId)
    {
        var record = FirstArticleRecord(
            workOrderId,
            operationTaskId,
            new InspectionResultLineInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, []),
            null);
        return new InspectionPassedIntegrationEventConverter(new FixedFirstArticleQualityContextAccessor())
            .Convert(new InspectionPassedDomainEvent(record));
    }

    /// <summary>
    /// 来源单据身份由**生产代码**拼（<c>FirstArticleInspection.SourceDocumentId</c>，#2779），
    /// 不在测试里手抄 <c>{a}:{b}</c>，否则 Quality 改了拼法本类不会红。
    /// </summary>
    private static InspectionRecord FirstArticleRecord(
        string workOrderId,
        string operationTaskId,
        InspectionResultLineInput line,
        string? dispositionReason) =>
        InspectionRecord.Create(
            Org,
            Env,
            null,
            FirstArticleInspection.SourceType,
            FirstArticleInspection.SourceService,
            FirstArticleInspection.SourceDocumentId(workOrderId, operationTaskId),
            sourceDocumentLineId: null,
            Sku,
            1m,
            null,
            null,
            [line],
            dispositionReason,
            []);

    private static MesDbContext CreateMesDbContext() =>
        new(
            new DbContextOptionsBuilder<MesDbContext>()
                .UseNpgsql(
                    AcceptancePostgresLaneDatabase.ConnectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema))
                .Options,
            FirstArticleHoldNoopMediator.Instance);

    private sealed class FixedFirstArticleQualityContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext() => new("corr-3315", "cause-3315", "system:quality");
    }

    private sealed class FirstArticleHoldNoopMediator : IMediator
    {
        internal static readonly FirstArticleHoldNoopMediator Instance = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This acceptance test mediator only supports publish.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException("This acceptance test mediator only supports publish.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This acceptance test mediator only supports publish.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This acceptance test mediator only supports publish.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This acceptance test mediator only supports publish.");
    }
}
