using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Npgsql;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 回填在**真实约束**下的写入路径（#3000）。InMemory 不校验 check constraint，也不跑迁移：
/// <c>SkipWindowsAccruedBefore</c> 同时写 <c>last_generated_time_window_sequence</c> 与
/// <c>time_schedule_anchor_at_utc</c>，这两列受 <c>ck_periodic_inspection_runtime_time_watermark</c> 配对约束；
/// 让位路径写 <c>sku_code</c> 又受 <c>ck_periodic_inspection_operations_release_snapshot</c> 整组约束。
/// 本 PR 之前这两条写入从未在真实约束下执行过。
/// </summary>
[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class WorkOrderReleaseProjectionBackfillPostgresTests : PeriodicInspectionPostgresTestHarness
{
    private static readonly DateTimeOffset ReleasedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
    private static readonly DateTimeOffset BackfilledAtUtc = DateTimeOffset.Parse("2026-09-01T00:00:00Z");

    [QualityPostgresFact]
    public async Task Backfill_commits_the_window_baseline_and_the_yielded_sku_under_real_constraints_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.InspectionPlans.Add(NewPeriodicPlan());
            await setup.SaveChangesAsync();
        }

        // 本 PR 两个注释迁移确实应用到了物理列上（迁移只改注释，除此之外无可观察产物）。
        Assert.Contains("composite by source", await ReadColumnCommentAsync("periodic_inspection_operations", "sku_code"));
        Assert.Contains("composite by source", await ReadColumnCommentAsync("periodic_inspection_operations", "released_at_utc"));
        Assert.Contains("composite meaning", await ReadColumnCommentAsync("periodic_inspection_runtime_contexts", "sku_code"));
        Assert.Contains("composite meaning", await ReadColumnCommentAsync("periodic_inspection_runtime_contexts", "released_at_utc"));

        // OP-001：报工先到（首次活动锚点落在 2026-08-24），补投后不得追认历史时间窗口。
        await HandleReportAsync(options, ProductionReport("RPT-PG-BF-001", 250m, false, null, "2026-08-24T01:30:00Z"));
        // OP-002：完工事实先到，且 SKU 是回落成工单号的 junk 值，补投必须让位而不是整封死信。
        await HandleCompletionAsync(options, CompletedWithJunkSku());

        await HandleBackfillAsync(options, Backfill("evt-backfill-pg-1"));

        await using (var assertion = CreateContext(options))
        {
            var baselined = await assertion.PeriodicInspectionRuntimeContexts.AsNoTracking()
                .SingleAsync(x => x.OperationId == "OP-001");
            // 配对约束在真实库上放行：seq > 0 时锚点必须同时非空。
            Assert.True(baselined.LastGeneratedTimeWindowSequence > 0);
            Assert.NotNull(baselined.TimeScheduleAnchorAtUtc);
            Assert.Equal(2, baselined.LastGeneratedQuantityWindowSequence);
            Assert.Empty(await assertion.InspectionTasks.AsNoTracking().ToArrayAsync());

            var yielded = await assertion.PeriodicInspectionOperations.AsNoTracking()
                .SingleAsync(x => x.OperationId == "OP-002");
            Assert.Equal("WO-001", yielded.SkuCode);
            Assert.Equal(ReleasedAtUtc.UtcDateTime, yielded.ReleasedAtUtc);
        }

        // 重跑：行内容与任务数不变（票面验收标准 2 在真实库上的复现）。
        var before = await SnapshotAsync(options);
        await HandleBackfillAsync(options, Backfill("evt-backfill-pg-2"));
        Assert.Equal(before, await SnapshotAsync(options));
        await using var rerun = CreateContext(options);
        Assert.Empty(await rerun.InspectionTasks.AsNoTracking().ToArrayAsync());
    }

    private static async Task<string[]> SnapshotAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = CreateContext(options);
        return await db.PeriodicInspectionOperations.AsNoTracking()
            .OrderBy(x => x.OperationId)
            .Select(x => x.OperationId + "|" + x.SkuCode + "|" + x.OperationSequence + "|" + x.WorkCenterId
                + "|" + x.ReleasedAtUtc)
            .ToArrayAsync();
    }

    private static async Task<string> ReadColumnCommentAsync(string table, string column)
    {
        await using var connection = new NpgsqlConnection(QualityPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT col_description(('quality.' || @table)::regclass::oid, attnum)
            FROM pg_attribute
            WHERE attrelid = ('quality.' || @table)::regclass AND attname = @column
            """;
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        return (string?)await command.ExecuteScalarAsync() ?? string.Empty;
    }

    private static async Task HandleBackfillAsync(
        DbContextOptions<ApplicationDbContext> options,
        WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent)
    {
        await using var db = CreateContext(options);
        await new WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleCompletionAsync(
        DbContextOptions<ApplicationDbContext> options,
        MesOperationTaskCompletedIntegrationEvent integrationEvent)
    {
        await using var db = CreateContext(options);
        await new MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static MesOperationTaskCompletedIntegrationEvent CompletedWithJunkSku() => new(
        "evt-complete-pg-op-002",
        MesIntegrationEventTypes.OperationTaskCompleted,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "corr-complete-pg-op-002",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:operation-completed:org-001:env-dev:WO-001:OP-002",
        new OperationTaskCompletedPayload(
            "WO-001", "OP-002", "WO-001", 20, "WC-001", 1000m, "EA", false,
            DateTimeOffset.Parse("2026-08-25T00:00:00Z")));

    private static WorkOrderReleaseProjectionBackfilledIntegrationEvent Backfill(string eventId) => new(
        eventId,
        MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled,
        MesIntegrationEventVersions.V1,
        BackfilledAtUtc,
        MesIntegrationEventSources.BusinessMes,
        "mes:work-order-release-projection-backfill:org-001:env-dev:WO-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:work-order-release-projection-backfill:org-001:env-dev:WO-001",
        new WorkOrderReleasedPayload(
            "WO-001",
            "SKU-FG-1000",
            1000m,
            ReleasedAtUtc,
            [
                new ReleasedOperationPayload("OP-001", 10, "WC-001"),
                new ReleasedOperationPayload("OP-002", 20, "WC-001"),
            ]));
}
