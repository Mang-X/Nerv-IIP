using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Scheduling;
using Prometheus;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 「工单发布事实缺失」读数的判据用例（#2983）。
///
/// 合同来源：`Regression` —— #2983 记录的缺陷是该状态**零信号**，读面判据
/// （`GetFirstArticleConfirmationQuery.ResolveMissingTaskStatusAsync` 按 `SkuCode` 为空回
/// `not-synchronized`）与稳态形状（第二个消费组 `CreatePending` 建行）都写在票面上，本类按该形状
/// 构造最小可复现夹具，并逐条钉住三个判据分支各自的鉴别力。
///
/// provider 边界：EF Core InMemory —— 只证明谓词的判据取舍，**不证明** SQL 翻译；真实
/// PostgreSQL 上的翻译与读数由 <see cref="WorkOrderReleaseFactBacklogPostgresTests"/> 负责。
/// </summary>
public sealed class WorkOrderReleaseFactBacklogMetricsTests
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    [Fact]
    public async Task Only_operations_with_report_facts_and_no_release_facts_beyond_the_floor_are_counted()
    {
        await using var dbContext = WorkOrderReleaseFactBacklogFixture.CreateInMemoryContext(
            $"release-fact-backlog-{Guid.CreateVersion7():N}");

        // 目标行：报工事实到了、发布事实没到，且报工已过年龄下限。
        await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
            dbContext, "WO-STUCK", "OP-001", "RPT-STUCK", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc);

        // 对照 ①「发布事实已到」：除 released_at_utc 外满足其余全部谓词。
        await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
            dbContext, "WO-RELEASED", "OP-001", "RPT-RELEASED", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc);
        await WorkOrderReleaseFactBacklogFixture.ApplyWorkOrderReleaseAsync(dbContext, "WO-RELEASED", "OP-001");

        // 对照 ②「有行但无报工事实」：工序完工事件同样经 LoadOrCreateAsync 建 pending 行，
        // 它没有报工事实，操作员也没被门禁拦住，不该计入。
        await WorkOrderReleaseFactBacklogFixture.RecordOperationCompletionAsync(dbContext, "WO-PENDING", "OP-001");

        // 对照 ③「仍在正常传播窗口内」：报工刚到、发布事件可能还在路上。
        await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
            dbContext, "WO-FRESH", "OP-001", "RPT-FRESH", WorkOrderReleaseFactBacklogFixture.FreshReportedAtUtc);

        // 对照 ④⑤「别的租户/环境」：形状与目标行完全相同，只有 scope 不同。
        await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
            dbContext, "WO-STUCK", "OP-001", "RPT-OTHER-ORG", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc,
            organizationId: "org-002");
        await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
            dbContext, "WO-STUCK", "OP-001", "RPT-OTHER-ENV", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc,
            environmentId: "env-prod");

        var reading = await ReadAsync(dbContext);

        Assert.Equal(1, reading.Operations);
        Assert.Equal(
            WorkOrderReleaseFactBacklogFixture.ScanAtUtc - WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc,
            reading.OldestAge);
    }

    [Fact]
    public async Task Backlog_age_reports_the_earliest_report_fact_of_the_earliest_stuck_operation()
    {
        await using var dbContext = WorkOrderReleaseFactBacklogFixture.CreateInMemoryContext(
            $"release-fact-backlog-{Guid.CreateVersion7():N}");

        // 同一道工序报了两次：卡住的起点是第一次报工，不是最近一次。
        await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
            dbContext, "WO-OLD", "OP-001", "RPT-OLD-1", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc);
        await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
            dbContext, "WO-OLD", "OP-001", "RPT-OLD-2", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc.AddMinutes(20));

        // 另一道更晚卡住的工序：跨行取的是最早那一条，不是最晚那一条。
        await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
            dbContext, "WO-NEW", "OP-001", "RPT-NEW", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc.AddMinutes(25));

        var reading = await ReadAsync(dbContext);

        Assert.Equal(2, reading.Operations);
        Assert.Equal(
            WorkOrderReleaseFactBacklogFixture.ScanAtUtc - WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc,
            reading.OldestAge);
    }

    [Fact]
    public async Task Backlog_reading_returns_to_zero_once_the_release_facts_land()
    {
        await using var dbContext = WorkOrderReleaseFactBacklogFixture.CreateInMemoryContext(
            $"release-fact-backlog-{Guid.CreateVersion7():N}");
        await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
            dbContext, "WO-STUCK", "OP-001", "RPT-STUCK", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc);

        var before = await ReadAsync(dbContext);
        await WorkOrderReleaseFactBacklogFixture.ApplyWorkOrderReleaseAsync(dbContext, "WO-STUCK", "OP-001");
        var after = await ReadAsync(dbContext);

        Assert.Equal(1, before.Operations);
        Assert.True(before.OldestAge > TimeSpan.Zero, $"expected a positive backlog age, observed {before.OldestAge}");
        Assert.Equal(0, after.Operations);
        Assert.Equal(TimeSpan.Zero, after.OldestAge);
    }

    [Fact]
    public async Task Refresh_publishes_both_gauges_under_the_scope_labels_on_the_exposition_endpoint()
    {
        await using var dbContext = WorkOrderReleaseFactBacklogFixture.CreateInMemoryContext(
            $"release-fact-backlog-{Guid.CreateVersion7():N}");
        await WorkOrderReleaseFactBacklogFixture.RecordProductionReportAsync(
            dbContext, "WO-STUCK", "OP-001", "RPT-STUCK", WorkOrderReleaseFactBacklogFixture.StaleReportedAtUtc);
        var registry = Metrics.NewCustomRegistry();
        var metrics = new WorkOrderReleaseFactBacklogMetrics(registry);

        await metrics.RefreshAsync(
            dbContext,
            WorkOrderReleaseFactBacklogFixture.OrganizationId,
            WorkOrderReleaseFactBacklogFixture.EnvironmentId,
            WorkOrderReleaseFactBacklogFixture.ScanAtUtc.UtcDateTime,
            StaleAfter,
            CancellationToken.None);
        var stuck = await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry);

        await WorkOrderReleaseFactBacklogFixture.ApplyWorkOrderReleaseAsync(dbContext, "WO-STUCK", "OP-001");
        await metrics.RefreshAsync(
            dbContext,
            WorkOrderReleaseFactBacklogFixture.OrganizationId,
            WorkOrderReleaseFactBacklogFixture.EnvironmentId,
            WorkOrderReleaseFactBacklogFixture.ScanAtUtc.UtcDateTime,
            StaleAfter,
            CancellationToken.None);
        var cleared = await WorkOrderReleaseFactBacklogFixture.ScrapeAsync(registry);

        Assert.Equal(1d, stuck[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric)]);
        Assert.Equal(5400d, stuck[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.OldestBacklogAgeMetric)]);
        // 回填跑完后这条 label 必须被重新写成 0，而不是停在最后一次非零读数上。
        Assert.Equal(0d, cleared[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.BacklogOperationsMetric)]);
        Assert.Equal(0d, cleared[WorkOrderReleaseFactBacklogFixture.Sample(WorkOrderReleaseFactBacklogFixture.OldestBacklogAgeMetric)]);
    }

    private static Task<WorkOrderReleaseFactBacklogReading> ReadAsync(ApplicationDbContext dbContext) =>
        WorkOrderReleaseFactBacklogMetrics.ReadAsync(
            dbContext,
            WorkOrderReleaseFactBacklogFixture.OrganizationId,
            WorkOrderReleaseFactBacklogFixture.EnvironmentId,
            WorkOrderReleaseFactBacklogFixture.ScanAtUtc.UtcDateTime,
            StaleAfter,
            CancellationToken.None);

}
