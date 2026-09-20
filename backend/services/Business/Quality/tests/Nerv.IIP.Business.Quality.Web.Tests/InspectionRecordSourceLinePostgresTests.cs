using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Contracts.Quality;
using Npgsql;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// GitHub #3319 的**真库**那一面。<see cref="InspectionRecordSourceLineIdentityTests"/> 用 InMemory
/// provider 看守「命中既有记录」的判据，但它证不到唯一索引：InMemory 与 SQLite 都不执行唯一索引，
/// SQLite 更不认 <c>NULLS NOT DISTINCT</c> 注解——把索引断言写在那些 provider 上是假绿。
///
/// <para>这里钉住三件只有 PostgreSQL 能证的事：</para>
/// <list type="number">
/// <item>新唯一键真的把同一工单的两道工序分开（去掉键里的来源行维度，第二条记录会撞 23505）；</item>
/// <item>迁移对存量行的处置：新列为 NULL，且这些行原有的去重保护**没有随之失效**
/// ——靠 <c>NULLS NOT DISTINCT</c>，PG 默认的「NULL 互不相等」会让整组去重静默失效；</item>
/// <item>索引的 <c>indnullsnotdistinct</c> 在库里确实是 true（模型注解到物理索引这一段没有断链）。</item>
/// </list>
/// </summary>
[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class InspectionRecordSourceLinePostgresTests
{
    private const string UniqueIndexName = "ux_inspection_records_source_attempt";

    /// <summary>本票之前的最后一个迁移；存量行造在它之上。</summary>
    private const string PreviousMigration = "20260910092001_WidenInspectionTaskTriggerIdempotencyKey";

    [QualityPostgresFact]
    public async Task Two_operations_of_the_same_work_order_and_sku_persist_as_independent_records_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        InspectionTaskId firstTaskId;
        InspectionTaskId secondTaskId;

        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            var plan = ActivePlan("PLAN-OP-PG-1000");
            var first = OperationTask(plan.Id, "WO-PG-001", "OP-10");
            var second = OperationTask(plan.Id, "WO-PG-001", "OP-20");
            setup.InspectionPlans.Add(plan);
            setup.InspectionTasks.AddRange(first, second);
            await setup.SaveChangesAsync();
            firstTaskId = first.Id;
            secondTaskId = second.Id;
        }

        var firstRecordId = await SubmitAsync(options, firstTaskId, "pg-submit-op-10");
        // 改前这一句就是缺陷本身：第二道工序命中第一道的记录，直接 Complete，两道工序共用一条结论。
        var secondRecordId = await SubmitAsync(options, secondTaskId, "pg-submit-op-20");

        Assert.NotEqual(firstRecordId, secondRecordId);
        await using var assertion = new ApplicationDbContext(options, new NoopMediator());
        var records = await assertion.InspectionRecords
            .AsNoTracking()
            .OrderBy(x => x.SourceDocumentLineId)
            .ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal("WO-PG-001", record.SourceDocumentId));
        Assert.Equal(["OP-10", "OP-20"], records.Select(x => x.SourceDocumentLineId));
        Assert.All(records, record => Assert.Equal(1, record.AttemptNumber));
    }

    [QualityPostgresFact]
    public async Task Migration_leaves_existing_records_without_a_source_line_and_keeps_their_dedup_group_protected()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        var legacyRecordId = Guid.CreateVersion7();

        await using var db = new ApplicationDbContext(options, new NoopMediator());
        QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await db.Database.ExecuteSqlRawAsync(LegacyRecordInsert(legacyRecordId, "WO-LEGACY-001"));

        await migrator.MigrateAsync();

        await using var connection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();

        // ① 存量行不回填：新列为 NULL。策略与存量分布无关，因此不需要现场读数。
        Assert.Null(await ScalarAsync(
            connection,
            $"SELECT source_document_line_id FROM quality.inspection_records WHERE id = '{legacyRecordId}'"));

        // ② 索引在库里确实是 NULLS NOT DISTINCT。EntityConfiguration 上的注解到物理索引之间会断链，
        //    而断链的方向是**静默放行**（PG 默认 NULL 互不相等），只能在真库上读。
        Assert.Equal(
            true,
            await ScalarAsync(
                connection,
                $"""
                SELECT i.indnullsnotdistinct
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indexrelid
                WHERE c.relname = '{UniqueIndexName}'
                """));

        // ③ 存量去重组没有失去保护：同键、来源行同为 NULL 的第二条必须被挡下。
        //    这一格就是 AreNullsDistinct(false) 的鉴别力所在——去掉它，本条会被静默接受。
        var duplicate = await CaptureViolationAsync(
            connection,
            LegacyRecordInsert(Guid.CreateVersion7(), "WO-LEGACY-001"));
        Assert.Equal("23505", duplicate?.SqlState);
        Assert.Equal(UniqueIndexName, duplicate?.ConstraintName);

        // ④ 但带上不同来源行的新记录必须被放行——否则「保护」是靠把整组锁死换来的。
        await ExecuteAsync(
            connection,
            LegacyRecordInsert(Guid.CreateVersion7(), "WO-LEGACY-001", sourceDocumentLineId: "OP-20"));
        Assert.Equal(
            2L,
            await ScalarAsync(
                connection,
                "SELECT count(*) FROM quality.inspection_records WHERE source_document_id = 'WO-LEGACY-001'"));

        // ⑤ 查询侧的 NULL 语义：`x.SourceDocumentLineId == null` 必须被翻译成 IS NULL 而不是 `= @p`。
        //    翻译错的方向是静默的——直录检验（来源行恒为 NULL）的幂等回读会永远查不到既有记录，
        //    于是每次重放都新建一条。InMemory provider 不走 SQL，只有真库能证。
        await using var reader = new ApplicationDbContext(options, new NoopMediator());
        var repository = new InspectionRecordRepository(reader);
        var documentLevel = await repository.FindBySourceDocumentAsync(
            "org-001", "env-dev", "operation", "mes", "SKU-FG-1000", "WO-LEGACY-001", null, CancellationToken.None);
        Assert.NotNull(documentLevel);
        Assert.Null(documentLevel.SourceDocumentLineId);

        var lineScoped = await repository.FindBySourceDocumentAsync(
            "org-001", "env-dev", "operation", "mes", "SKU-FG-1000", "WO-LEGACY-001", "OP-20", CancellationToken.None);
        Assert.NotNull(lineScoped);
        Assert.Equal("OP-20", lineScoped.SourceDocumentLineId);
        Assert.NotEqual(documentLevel.Id, lineScoped.Id);
    }

    private static string LegacyRecordInsert(Guid id, string sourceDocumentId, string? sourceDocumentLineId = null)
    {
        var lineColumn = sourceDocumentLineId is null ? string.Empty : ", source_document_line_id";
        var lineValue = sourceDocumentLineId is null ? string.Empty : $", '{sourceDocumentLineId}'";
        return $"""
            INSERT INTO quality.inspection_records (
                id, organization_id, environment_id, source_type, source_service, source_document_id,
                sku_code, attempt_number, inspected_quantity, result, disposition_attachment_file_ids,
                created_at_utc, updated_at_utc{lineColumn})
            VALUES (
                '{id}', 'org-001', 'env-dev', 'operation', 'mes', '{sourceDocumentId}',
                'SKU-FG-1000', 1, 10, 'passed', ARRAY[]::text[],
                TIMESTAMPTZ '2026-09-01T08:00:00Z', TIMESTAMPTZ '2026-09-01T08:00:00Z'{lineValue})
            """;
    }

    private static async Task<PostgresException?> CaptureViolationAsync(NpgsqlConnection connection, string sql)
    {
        try
        {
            await ExecuteAsync(connection, sql);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception;
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task<InspectionRecordId> SubmitAsync(
        DbContextOptions<ApplicationDbContext> options,
        InspectionTaskId taskId,
        string idempotencyKey)
    {
        await using var db = new ApplicationDbContext(options, new NoopMediator());
        var task = await db.InspectionTasks.SingleAsync(x => x.Id == taskId);
        task.Assign("qa-user-001", null, task.Version, DateTimeOffset.Parse("2026-07-05T08:10:00Z"));
        task.Claim("qa-user-001", [], task.Version, DateTimeOffset.Parse("2026-07-05T08:20:00Z"));
        var handler = new CreateInspectionRecordFromTaskCommandHandler(
            new InspectionTaskRepository(db),
            new InspectionRecordRepository(db),
            new InspectionPlanRepository(db),
            new NonconformanceReportRepository(db),
            new NonconformanceReportCodeGenerator(),
            db);
        var result = await handler.Handle(
            new CreateInspectionRecordFromTaskCommand(
                taskId,
                "qa-user-001",
                [new InspectionResultLineCommandInput("appearance", "ok", null, InspectionLineResults.Passed, null, null, [])],
                null,
                [],
                idempotencyKey,
                "org-001",
                "env-dev"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        return result.InspectionRecordId;
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                QualityPostgresLaneDatabase.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", QualityFacts.Schema))
            .Options;

    private static InspectionTask OperationTask(InspectionPlanId planId, string workOrderId, string operationTaskId)
    {
        return InspectionTask.CreatePending(
            "org-001",
            "env-dev",
            planId,
            QualityInspectionSourceTypes.Operation,
            QualityInspectionSourceServices.Mes,
            workOrderId,
            operationTaskId,
            "SKU-FG-1000",
            10m,
            "pcs",
            null,
            null,
            DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            $"mes:operation-inspection:{workOrderId}:{operationTaskId}");
    }

    private static InspectionPlan ActivePlan(string planCode)
    {
        var plan = InspectionPlan.Create(
            "org-001",
            "env-dev",
            planCode,
            QualityInspectionSourceTypes.Operation,
            "SKU-FG-1000",
            null,
            null,
            null,
            null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
