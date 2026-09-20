using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Prometheus;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 「工单发布事实缺失」读数在真实 PostgreSQL 上的翻译与读数（#2983）。
///
/// 合同来源：`ProviderBehavior` + `Regression` —— 判据里两处集合谓词
/// （<c>ProductionReports.Any(...)</c> 与嵌套 <c>Min(...)</c>）必须被真实 provider 翻译成 SQL；
/// EF Core InMemory 对不可翻译的写法一律放行，因此 InMemory 用例证不到这条查询在真库上跑得动。
/// 回填后归零那一段复现的是 #2983 收窄后的验收条件本身。
///
/// 边界：本类**不**证明 CAP 传输、消费组路由或 <c>/metrics</c> 的 HTTP 路由；
/// 回填事件是直接投给 Quality 侧消费者的，不经 broker。
/// </summary>
[Collection(QualityPostgresLaneDatabase.CollectionName)]
public sealed class WorkOrderReleaseFactBacklogPostgresTests : PeriodicInspectionPostgresTestHarness
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    [QualityPostgresFact(Timeout = 30_000)]
    public async Task Backlog_gauges_count_only_stuck_operations_and_return_to_zero_after_the_backfill_on_postgres()
    {
        await QualityPostgresLaneDatabase.ResetSchemaAsync();
        var options = CreateOptions();
        await using (var setup = CreateContext(options))
        {
            QualityPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
        }

        // 目标行：报工事实先到、发布事实未到。
        await HandleReportAsync(
            options,
            WorkOrderReleaseFactBacklogFixture.ProductionReport(
                "WO-STUCK", "OP-001", "RPT-STUCK", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc));

        // 对照 ①：报工事实与发布事实都到了。
        await HandleReportAsync(
            options,
            WorkOrderReleaseFactBacklogFixture.ProductionReport(
                "WO-RELEASED", "OP-001", "RPT-RELEASED", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc));
        await ApplyReleaseAsync(
            options,
            WorkOrderReleaseFactBacklogFixture.WorkOrderReleased("WO-RELEASED", "OP-001"));

        // 对照 ②：完工事件建出的 pending 行——同样没有发布事实，但没有报工事实。
        await ApplyCompletionAsync(
            options,
            WorkOrderReleaseFactBacklogFixture.OperationCompleted("WO-PENDING", "OP-001"));

        // 对照 ③：报工刚到，仍在正常传播窗口内。
        await HandleReportAsync(
            options,
            WorkOrderReleaseFactBacklogFixture.ProductionReport(
                "WO-FRESH", "OP-001", "RPT-FRESH", WorkOrderReleaseFactBacklogFixture.FreshReportedAtUtc));

        // 对照 ④：形状与目标行相同，只是换了一个环境。
        await HandleReportAsync(
            options,
            WorkOrderReleaseFactBacklogFixture.ProductionReport(
                "WO-STUCK", "OP-001", "RPT-OTHER-ENV", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc,
                environmentId: "env-prod"));

        var registry = Metrics.NewCustomRegistry();
        var metrics = new WorkOrderReleaseFactBacklogMetrics(registry);
        var stuckReading = await RefreshAsync(options, metrics);
        var stuckSamples = await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry);

        // 恢复路径：#3000 的回填端点发布的补投事件在 Quality 侧落地。
        await ApplyBackfillAsync(options, Backfill("evt-backfill-release-fact-backlog"));
        var clearedReading = await RefreshAsync(options, metrics);
        var clearedSamples = await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry);

        Assert.Equal(1, stuckReading.Operations);
        Assert.Equal(
            WorkOrderReleaseFactBacklogFixture.ScanAtUtc - WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc,
            stuckReading.OldestAge);
        Assert.Equal(1d, stuckSamples[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric)]);
        Assert.Equal(5400d, stuckSamples[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.OldestBacklogAgeMetric)]);

        // 回填确实写进了那一行，而不是靠别的谓词把它滤掉。
        await using (var assertion = CreateContext(options))
        {
            var repaired = await assertion.PeriodicInspectionOperations.AsNoTracking()
                .SingleAsync(x => x.WorkOrderId == "WO-STUCK" && x.EnvironmentId == WorkOrderReleaseFactBacklogFixture.EnvironmentId);
            Assert.Equal("SKU-FG-1000", repaired.SkuCode);
            Assert.NotNull(repaired.ReleasedAtUtc);
        }

        Assert.Equal(0, clearedReading.Operations);
        Assert.Equal(TimeSpan.Zero, clearedReading.OldestAge);
        Assert.Equal(0d, clearedSamples[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric)]);
        Assert.Equal(0d, clearedSamples[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.OldestBacklogAgeMetric)]);
    }

    private static async Task<WorkOrderReleaseFactBacklogReading> RefreshAsync(
        DbContextOptions<ApplicationDbContext> options,
        WorkOrderReleaseFactBacklogMetrics metrics)
    {
        await using var db = CreateContext(options);
        return await metrics.RefreshAsync(
            db,
            WorkOrderReleaseFactBacklogFixture.OrganizationId,
            WorkOrderReleaseFactBacklogFixture.EnvironmentId,
            WorkOrderReleaseFactBacklogFixture.ScanAtUtc.UtcDateTime,
            StaleAfter,
            CancellationToken.None);
    }

    private static async Task ApplyReleaseAsync(
        DbContextOptions<ApplicationDbContext> options,
        WorkOrderReleasedIntegrationEvent integrationEvent)
    {
        await using var db = CreateContext(options);
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task ApplyCompletionAsync(
        DbContextOptions<ApplicationDbContext> options,
        MesOperationTaskCompletedIntegrationEvent integrationEvent)
    {
        await using var db = CreateContext(options);
        await new MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task ApplyBackfillAsync(
        DbContextOptions<ApplicationDbContext> options,
        WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent)
    {
        await using var db = CreateContext(options);
        await new WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            db,
            new PeriodicInspectionOperationScopeCoordinator(db),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static WorkOrderReleaseProjectionBackfilledIntegrationEvent Backfill(string eventId) => new(
        eventId,
        MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled,
        MesIntegrationEventVersions.V1,
        WorkOrderReleaseFactBacklogFixture.ScanAtUtc,
        MesIntegrationEventSources.BusinessMes,
        "mes:work-order-release-projection-backfill:org-001:env-dev:WO-STUCK",
        "WO-STUCK",
        WorkOrderReleaseFactBacklogFixture.OrganizationId,
        WorkOrderReleaseFactBacklogFixture.EnvironmentId,
        "system:mes",
        "mes:work-order-release-projection-backfill:org-001:env-dev:WO-STUCK",
        new WorkOrderReleasedPayload(
            "WO-STUCK",
            "SKU-FG-1000",
            1000m,
            WorkOrderReleaseFactBacklogFixture.ReleasedAtUtc,
            [new ReleasedOperationPayload("OP-001", 10, "WC-001")]));
}
