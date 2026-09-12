using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Npgsql;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 回填在**真实约束**下的写入路径（#3000）。InMemory 不校验 check constraint，也不跑迁移，
/// 下面两条写入因此只有在真库上才有读数：
/// <list type="number">
/// <item><c>SkipWindowsAccruedBefore</c> 同时写 <c>last_generated_time_window_sequence</c> 与
/// <c>time_schedule_anchor_at_utc</c>，这两列受 <c>ck_periodic_inspection_runtime_time_watermark</c> 配对约束。</item>
/// <item>**被拒工序**留下的 release 快照是**整组 NULL** 那一支，受
/// <c>ck_periodic_inspection_operations_release_snapshot</c> 约束（四列全 NULL 或四列全有值，二选一）。
/// #3286 之前这一格写的是「让位取权威 SKU」那一支，见下面用例注释。</item>
/// </list>
/// </summary>
[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class WorkOrderReleaseProjectionBackfillPostgresTests : PeriodicInspectionPostgresTestHarness
{
    private static readonly DateTimeOffset ReleasedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
    private static readonly DateTimeOffset BackfilledAtUtc = DateTimeOffset.Parse("2026-09-01T00:00:00Z");

    /// <summary>
    /// <b>本用例的 SKU 那一半已按 #3286 改写口径，原断言不再成立。</b>
    ///
    /// <para>改写前它断言的是「补投为 SKU **让位**」：OP-002 的完工事实带着工单号形状的 SKU，
    /// 补投把 <c>sku_code</c> 写成权威完工值 <c>WO-001</c>，据此证明让位那一支满足
    /// <c>ck_periodic_inspection_operations_release_snapshot</c>。#3286 按属性收缩掉了 SKU 那条 substitution
    /// （回落来源已随 #3112 消除，剩下的只能是上游传错，不该由投影层静默顶平），
    /// **那条写入路径已不存在** —— 原断言证的事已经没有了，不是它被放宽了。</para>
    ///
    /// <para>改写后它在真实约束下证的是：SKU 不一致的工序被**按工序粒度**拒掉之后，
    /// ① 它留下的是 release 快照**整组 NULL** 那一支，check constraint 放行（InMemory 不校验约束，证不了）；
    /// ② 完工事实原样留着，不是把这行清掉；③ 同一封事件里的 OP-001 照常提交，
    /// 被拒工序不牵连它，也不多建运行上下文；④ 拒绝有**待处理**留痕，不是静默丢弃。</para>
    /// </summary>
    [QualityPostgresFact(Timeout = 30_000)]
    public async Task Backfill_commits_the_window_baseline_and_rejects_the_disagreeing_sku_under_real_constraints_on_postgres()
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
        // OP-002：完工事实先到，且 SKU 与工单 SKU 不符（#3112 之前由回落产生，之后只能由上游传错产生）。
        // #3286 起补投不再为它让位；它被拒，但**不得**让整封事件死信、也不得牵连 OP-001。
        await HandleCompletionAsync(options, CompletedWithJunkSku());

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await HandleBackfillAsync(options, Backfill("evt-backfill-pg-1"), deadLetters);

        await using (var assertion = CreateContext(options))
        {
            var baselined = await assertion.PeriodicInspectionRuntimeContexts.AsNoTracking()
                .SingleAsync(x => x.OperationId == "OP-001");
            // 配对约束在真实库上放行：seq > 0 时锚点必须同时非空。
            Assert.True(baselined.LastGeneratedTimeWindowSequence > 0);
            Assert.NotNull(baselined.TimeScheduleAnchorAtUtc);
            Assert.Equal(2, baselined.LastGeneratedQuantityWindowSequence);
            Assert.Empty(await assertion.InspectionTasks.AsNoTracking().ToArrayAsync());

            // 被拒工序：release 快照四列整组 NULL —— 这一支由 check constraint 明文允许，
            // 四列里任何一列单独落值都会被 ck_periodic_inspection_operations_release_snapshot 拒掉。
            var rejected = await assertion.PeriodicInspectionOperations.AsNoTracking()
                .SingleAsync(x => x.OperationId == "OP-002");
            Assert.Null(rejected.SkuCode);
            Assert.Null(rejected.OperationSequence);
            Assert.Null(rejected.WorkCenterId);
            Assert.Null(rejected.ReleasedAtUtc);
            // 被拒的是补投的**发布事实**，完工事实原样留着（不是把这行清掉）。
            Assert.Equal("WO-001", rejected.CompletionSkuCode);
            // 失败粒度是工序：OP-002 被拒不牵连 OP-001，也不给自己建运行上下文。
            Assert.Single(await assertion.PeriodicInspectionRuntimeContexts.AsNoTracking().ToArrayAsync());
        }

        // 拒绝不静默，且落**待处理**队列（上游传错要有人去改），不是「已按权威事实处置」。
        var notice = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("backfill-operation-rejected", notice.FailureCode);
        Assert.Equal(IntegrationEventDeadLetterStatus.Pending, notice.Status);
        Assert.Contains("OP-002", notice.FailureMessage, StringComparison.Ordinal);

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
        WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent,
        InMemoryIntegrationEventDeadLetterStore? deadLetters = null)
    {
        await using var db = CreateContext(options);
        await new WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            deadLetters ?? new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(integrationEvent, CancellationToken.None);
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
