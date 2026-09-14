using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.Mes.Domain;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

/// <summary>
/// GitHub #3318：Quality 的处置引用身份逐字写进 MES <c>defect_records.disposition_reference_id</c>。
///
/// **为什么必须跑真实 PostgreSQL**：本类要钉住的失败是**列宽溢出**——改前该列是 <c>varchar(100)</c>，
/// 而 Quality 三个产出列 <c>nonconformance_reports.{rework_work_order_id, scrap_movement_id, return_document_id}</c>
/// 都是 <c>varchar(150)</c>。EF InMemory / SQLite 不带列长约束，在它们上面这条永远是绿的，不承重。
/// 改前实测读数（<c>postgres:18</c>，PR 正文 R0，150 字符的返修工单引用）：
/// <c>DbUpdateException ---- Npgsql.PostgresException : 22001: value too long for type character varying(100)</c>，
/// 且**逃逸出** <c>HandleAsync</c>（栈顶经 <c>IntegrationEventConsumerGuard</c> 直达用例）——
/// 该 handler 函数体内一条 <c>catch</c> 都没有，即命中 #877 的 poison message 形状。
///
/// **合同分类**（<c>docs/governance/testing/validity.md</c> 六类合同来源）：
/// <list type="bullet">
/// <item><c>ProviderBehavior</c>：权威来源是 migration / schema 约束
/// （<c>defect_records.disposition_reference_id</c> 的物理类型）与 PostgreSQL 对超宽赋值抛 <c>22001</c>
/// 的官方行为。预期值由该来源推导，不是从当前实现输出反抄的。</item>
/// <item><c>Regression</c>：权威来源是 GitHub #3318 正文（错误行为「22001 逃逸成 poison message」、
/// 期望行为「同一输入不再卡链」），本类三条用例即最小复现，改前实测失败。</item>
/// </list>
///
/// **证明范围**：本类只跑 MES 一侧的消费者 + 真实 PostgreSQL，**不能**证明 CAP 传输、重投或完整服务拓扑；
/// 「Quality 侧三个产出列没被单边放宽」由
/// <see cref="NcrDispositionReferenceCrossServiceWidthContractTests"/> 承担，不由本类看守。
/// </summary>
[Collection(AcceptancePostgresLaneDatabase.CollectionName)]
public sealed class MesDefectDispositionReferenceAcceptanceTests
{
    private const string Org = "org-001";
    private const string Env = "env-dev";
    private const string Sku = "SKU-FG-3318";
    private const string WorkOrderId = "WO-3318";
    private const string DefectNo = "DEF-3318";

    /// <summary>改前该列的列宽；让「缺陷确实存在过」成为可读数的断言而不是注释。</summary>
    private const int PreChangeColumnMaxLength = 100;

    /// <summary>
    /// 本票产出的迁移。用例按它定位「改前那一版 schema」，因此这个名字必须在迁移清单里存在——
    /// 重命名/删除迁移会红在 <see cref="Postgres_widened_column_keeps_updating_a_defect_row_written_before_the_migration"/>，
    /// 而不是让那条用例静默退化成「在新 schema 上写了一行又读回来」。
    /// </summary>
    private const string WidenMigrationId =
        "20260910084403_WidenMesDefectDispositionReferenceIdForQualityProducerWidth";

    [RealPostgresFact]
    public async Task Postgres_disposition_reference_at_the_quality_producer_width_is_persisted_without_escaping_the_consumer()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync(MesFacts.Schema);
        await using var dbContext = CreateMesDbContext();
        await dbContext.Database.MigrateAsync();
        AcceptancePostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await SeedDefectAsync(dbContext, DefectNo);

        // 长度取自 **Quality EF 模型**里 nonconformance_reports.rework_work_order_id 的列宽——
        // 这就是生产者能发出来的最长返修工单引用，也就是改前 varchar(100) 的直接溢出输入。
        // 这里不手抄 150：Quality 改了那一列的宽度，下面两条断言都会红。
        var producerWidth = QualityProducerWidth(nameof(NonconformanceReport.ReworkWorkOrderId));
        Assert.True(
            producerWidth > PreChangeColumnMaxLength,
            $"Quality 产出列宽 {producerWidth} 未超过改前 MES 列宽 {PreChangeColumnMaxLength}，本用例失去针对性。");
        var referenceId = Identity("RW-WO-", producerWidth);

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(dbContext, deadLetters);

        // 不抛出：这一句本身就是「不再是 poison message」的读数（改前这里是 22001 逃逸）。
        await consumer.HandleAsync(DispositionEvent("evt-3318-at-producer-width", DefectNo, referenceId), CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var defect = await dbContext.DefectRecords.AsNoTracking().SingleAsync();
        Assert.Equal(referenceId, defect.DispositionReferenceId);
        Assert.Equal(DefectRecord.ReworkPendingStatus, defect.Status);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    /// <summary>
    /// 守卫这一层的真库读数：**超出新列宽**的处置引用走死信，而不是 22001 逃逸。
    /// 用**持久化**死信存储而非内存桩——否则「fail-closed 那条路自己会不会炸库」没有被证明。
    /// </summary>
    [RealPostgresFact]
    public async Task Postgres_disposition_reference_beyond_the_column_width_is_dead_lettered_instead_of_escaping_the_consumer()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync(MesFacts.Schema);
        await using var dbContext = CreateMesDbContext();
        await dbContext.Database.MigrateAsync();
        await SeedDefectAsync(dbContext, DefectNo);

        // 上界那一位由上一条用例覆盖（真实可达输入）；这里构造的是「上界 + 1」，长度从列宽常量派生。
        var overBound = Identity("RW-WO-", MesDefectDispositionReferenceIdPolicy.ColumnMaxLength + 1);

        var deadLetters = new PersistentIntegrationEventDeadLetterStore<MesDbContext>(dbContext);
        var consumer = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(dbContext, deadLetters);

        // 不抛出。
        await consumer.HandleAsync(DispositionEvent("evt-3318-over-bound", DefectNo, overBound), CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        // 缺陷记录一个字节都没被改写：既没落越界值，也没落截断值。
        var defect = await dbContext.DefectRecords.AsNoTracking().SingleAsync();
        Assert.Null(defect.DispositionReferenceId);
        Assert.Null(defect.DispositionType);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.ConsumerName,
            null,
            CancellationToken.None));
        Assert.Equal(MesDefectDispositionReferenceIdPolicy.OverlongFailureCode, deadLetter.FailureCode);
        // 死信消息只报长度、不回显取值：越界取值天然无上界，回显会让 failure_message 也变成越界写面。
        Assert.Contains(
            overBound.Length.ToString(CultureInfo.InvariantCulture),
            deadLetter.FailureMessage,
            StringComparison.Ordinal);
        Assert.DoesNotContain(overBound, deadLetter.FailureMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 查找语义不得漂移：<c>varchar(100) → varchar(150)</c> 之后，**迁移前**写下的那一行
    /// 仍然被 <c>NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect</c> 按
    /// <c>(org, env, defect_no)</c> 命中并更新，而不是新增第二行。
    ///
    /// 这条必须跨迁移跑：在新 schema 上写一行再读回来，证明不了「旧行还找得到」，
    /// 也证明不了加宽迁移真的作用在了既有数据上（第二次回写的是 150 字符，在旧 schema 上必然 22001）。
    /// </summary>
    [RealPostgresFact]
    public async Task Postgres_widened_column_keeps_updating_a_defect_row_written_before_the_migration()
    {
        await AcceptancePostgresLaneDatabase.ResetSchemaAsync(MesFacts.Schema);
        await using var dbContext = CreateMesDbContext();

        var migrations = dbContext.Database.GetMigrations().ToArray();
        var widenIndex = Array.IndexOf(migrations, WidenMigrationId);
        Assert.True(widenIndex > 0, $"迁移 {WidenMigrationId} 必须存在于迁移清单中，否则本用例失去跨迁移语义。");
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[widenIndex - 1]);

        await SeedDefectAsync(dbContext, DefectNo);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var consumer = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(dbContext, deadLetters);

        // 旧 schema 上写得下的短引用。
        var legacyReference = "RW-WO-LEGACY-3318";
        Assert.True(legacyReference.Length <= PreChangeColumnMaxLength, "迁移前那一条必须在旧列宽内，否则它自己就会 22001。");
        await consumer.HandleAsync(DispositionEvent("evt-3318-legacy", DefectNo, legacyReference), CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        var legacyDefect = await dbContext.DefectRecords.AsNoTracking().SingleAsync();
        Assert.Equal(legacyReference, legacyDefect.DispositionReferenceId);

        // 加宽迁移在既有行之上执行。
        await migrator.MigrateAsync();
        dbContext.ChangeTracker.Clear();

        // 同一条缺陷身份再发一次，这次带的是加宽后才存得下的引用。
        var widenedReference = Identity("RW-WO-", MesDefectDispositionReferenceIdPolicy.ColumnMaxLength);
        await consumer.HandleAsync(DispositionEvent("evt-3318-after-widen", DefectNo, widenedReference), CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        // 命中既有行：仍然只有一行，且 id 未变、引用被更新——不是新插了一行。
        var updated = await dbContext.DefectRecords.AsNoTracking().SingleAsync();
        Assert.Equal(legacyDefect.Id, updated.Id);
        Assert.Equal(widenedReference, updated.DispositionReferenceId);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    /// <summary>把身份补齐到指定长度，前缀只是为了失败消息可读。</summary>
    private static string Identity(string prefix, int width) => prefix + new string('9', width - prefix.Length);

    /// <summary>Quality 侧产出列宽，从 Quality EF 模型按 payload 字段同名属性读，不手抄。</summary>
    private static int QualityProducerWidth(string propertyName)
    {
        using var quality = CreateQualityModelOnlyDbContext();
        var entityType = quality.Model.FindEntityType(typeof(NonconformanceReport))
            ?? throw new InvalidOperationException("Quality 模型里找不到 NonconformanceReport 实体。");
        return (entityType.FindProperty(propertyName)
                ?? throw new InvalidOperationException($"NonconformanceReport 上没有 {propertyName} 属性。"))
            .GetMaxLength()
            ?? throw new InvalidOperationException($"NonconformanceReport.{propertyName} 没有声明列宽。");
    }

    private static async Task SeedDefectAsync(MesDbContext dbContext, string defectNo)
    {
        var now = DateTimeOffset.Parse("2026-09-10T02:00:00Z");
        dbContext.WorkOrders.Add(WorkOrder.Create(Org, Env, WorkOrderId, Sku, "PV-3318-1", 10m, 20, now.AddHours(8)));
        dbContext.DefectRecords.Add(DefectRecord.Create(Org, Env, defectNo, WorkOrderId, "OP-3318-10", "SURFACE", 1m, now));
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
            DateTimeOffset.Parse("2026-09-10T03:00:00Z"),
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
                Sku,
                1m,
                QualityNcrDispositionTypes.Rework,
                "approval-3318",
                reworkWorkOrderId,
                null,
                null,
                DateTimeOffset.Parse("2026-09-10T03:00:00Z"))
            {
                SourceDocumentId = defectNo,
            });

    private static MesDbContext CreateMesDbContext() =>
        new(
            new DbContextOptionsBuilder<MesDbContext>()
                .UseNpgsql(
                    AcceptancePostgresLaneDatabase.ConnectionString,
                    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MesFacts.Schema))
                .Options,
            DispositionReferenceNoopMediator.Instance);

    private static Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext CreateQualityModelOnlyDbContext() =>
        new(
            new DbContextOptionsBuilder<Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext>()
                .UseNpgsql(
                    AcceptancePostgresLaneDatabase.ConnectionString,
                    npgsql => npgsql.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        Nerv.IIP.Business.Quality.Domain.QualityFacts.Schema))
                .Options,
            DispositionReferenceNoopMediator.Instance);

    private sealed class DispositionReferenceNoopMediator : IMediator
    {
        internal static readonly DispositionReferenceNoopMediator Instance = new();

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
