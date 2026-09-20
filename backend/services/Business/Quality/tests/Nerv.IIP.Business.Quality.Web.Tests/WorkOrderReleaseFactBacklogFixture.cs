using System.Globalization;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;
using Prometheus;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 「有报工事实、无发布事实」这一稳态的共享夹具（#2983）。
///
/// 行一律由**生产路径的消费者**写出，不手工 <c>Add</c> 实体：卡住的那一行正是
/// <c>ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection</c> 的
/// <c>LoadOrCreateAsync</c> → <c>CreatePending</c> 建出来的，手工构造会绕开这条来路，
/// 也就证不到被观测的是同一个状态。
/// </summary>
internal static class WorkOrderReleaseFactBacklogFixture
{
    internal const string OrganizationId = "org-001";
    internal const string EnvironmentId = "env-dev";

    /// <summary>工单发布时刻。报工事实必须不早于它，否则 <c>ApplyRelease</c> 判为冲突事实。</summary>
    internal static readonly DateTimeOffset ReleasedAtUtc = DateTimeOffset.Parse(
        "2026-08-24T01:00:00Z",
        CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal);

    /// <summary>卡住那一行的最早报工时刻。</summary>
    internal static readonly DateTimeOffset StaleReportedAtUtc = DateTimeOffset.Parse(
        "2026-08-24T01:30:00Z",
        CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal);

    /// <summary>仍在正常传播窗口内的报工时刻（晚于 <see cref="ScanAtUtc"/> 减一小时的下限）。</summary>
    internal static readonly DateTimeOffset FreshReportedAtUtc = DateTimeOffset.Parse(
        "2026-08-24T02:30:00Z",
        CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal);

    /// <summary>巡检时刻；与一小时的年龄下限一起把下限边界压在 <c>02:00:00Z</c>。</summary>
    internal static readonly DateTimeOffset ScanAtUtc = DateTimeOffset.Parse(
        "2026-08-24T03:00:00Z",
        CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal);

    /// <summary>工序完工时刻，用于「有行、无报工事实」的对照行。</summary>
    internal static readonly DateTimeOffset CompletedAtUtc = DateTimeOffset.Parse(
        "2026-08-24T02:00:00Z",
        CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal);

    internal const string BacklogOperationsMetric = "nerv_iip_quality_release_fact_backlog_operations";
    internal const string OldestBacklogAgeMetric = "nerv_iip_quality_release_fact_backlog_oldest_age_seconds";

    internal static string Sample(string metric, string organizationId = OrganizationId, string environmentId = EnvironmentId) =>
        $"{metric}{{organization=\"{organizationId}\",environment=\"{environmentId}\"}}";

    /// <summary>
    /// 同名库固定共享同一个 <see cref="InMemoryDatabaseRoot"/>：巡检 Service 每个 scope 都新建一个
    /// <see cref="ApplicationDbContext"/>，若各自落到不同的内部 root，夹具写进去的行巡检根本读不到，
    /// 用例会因为"读到 0"而假绿。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, InMemoryDatabaseRoot> DatabaseRoots =
        new(StringComparer.Ordinal);

    internal static ApplicationDbContext CreateInMemoryContext(string databaseName) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(
                    databaseName,
                    DatabaseRoots.GetOrAdd(databaseName, _ => new InMemoryDatabaseRoot()))
                .Options,
            new NoopMediator());

    /// <summary>报工事实先到：投影行被建出来，但 <c>sku_code</c>/<c>released_at_utc</c> 仍为空。</summary>
    internal static async Task RecordProductionReportAsync(
        ApplicationDbContext dbContext,
        string workOrderId,
        string operationId,
        string reportNo,
        DateTimeOffset reportedAtUtc,
        string organizationId = OrganizationId,
        string environmentId = EnvironmentId)
    {
        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
            ProductionReport(workOrderId, operationId, reportNo, reportedAtUtc, organizationId, environmentId),
            CancellationToken.None);
    }

    /// <summary>发布事实补上：等价于 #3000 的回填事件在 Quality 侧落地后的终态。</summary>
    internal static async Task ApplyWorkOrderReleaseAsync(
        ApplicationDbContext dbContext,
        string workOrderId,
        string operationId,
        string organizationId = OrganizationId,
        string environmentId = EnvironmentId)
    {
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
            WorkOrderReleased(workOrderId, operationId, organizationId, environmentId),
            CancellationToken.None);
    }

    /// <summary>
    /// 工序完工事件走的也是 <c>LoadOrCreateAsync</c>：取不到行就建一行 pending。这条路径产出的行
    /// 同样没有发布事实，但**没有报工事实**——操作员没被门禁拦住，不是本指标要观测的状态。
    /// </summary>
    internal static async Task RecordOperationCompletionAsync(
        ApplicationDbContext dbContext,
        string workOrderId,
        string operationId,
        string organizationId = OrganizationId,
        string environmentId = EnvironmentId)
    {
        await new MesOperationTaskCompletedIntegrationEventHandlerForClosePeriodicInspection(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
            OperationCompleted(workOrderId, operationId, organizationId, environmentId),
            CancellationToken.None);
    }

    internal static MesOperationTaskCompletedIntegrationEvent OperationCompleted(
        string workOrderId,
        string operationId,
        string organizationId = OrganizationId,
        string environmentId = EnvironmentId) => new(
        $"evt-complete-{organizationId}-{environmentId}-{workOrderId}-{operationId}",
        MesIntegrationEventTypes.OperationTaskCompleted,
        MesIntegrationEventVersions.V1,
        CompletedAtUtc,
        MesIntegrationEventSources.BusinessMes,
        $"corr-complete-{workOrderId}",
        workOrderId,
        organizationId,
        environmentId,
        "system:mes",
        $"mes:operation-completed:{organizationId}:{environmentId}:{workOrderId}:{operationId}",
        new OperationTaskCompletedPayload(
            workOrderId, operationId, "SKU-FG-1000", 10, "WC-001", 1000m, "EA", false, CompletedAtUtc));

    internal static ProductionReportRecordedIntegrationEvent ProductionReport(
        string workOrderId,
        string operationId,
        string reportNo,
        DateTimeOffset reportedAtUtc,
        string organizationId = OrganizationId,
        string environmentId = EnvironmentId) => new(
        $"evt-report-{organizationId}-{environmentId}-{reportNo}",
        MesIntegrationEventTypes.ProductionReportRecorded,
        MesIntegrationEventVersions.V1,
        reportedAtUtc,
        MesIntegrationEventSources.BusinessMes,
        $"corr-report-{reportNo}",
        workOrderId,
        organizationId,
        environmentId,
        "system:mes",
        $"mes:production-report-recorded:{organizationId}:{environmentId}:{reportNo}",
        new ProductionReportRecordedPayload(
            reportNo, workOrderId, operationId, "WC-001", null, 25m, 0m, 0m, "EA", null, reportedAtUtc, false));

    internal static WorkOrderReleasedIntegrationEvent WorkOrderReleased(
        string workOrderId,
        string operationId,
        string organizationId = OrganizationId,
        string environmentId = EnvironmentId) => new(
        $"evt-release-{organizationId}-{environmentId}-{workOrderId}",
        MesIntegrationEventTypes.WorkOrderReleased,
        MesIntegrationEventVersions.V1,
        ReleasedAtUtc,
        MesIntegrationEventSources.BusinessMes,
        $"corr-release-{workOrderId}",
        workOrderId,
        organizationId,
        environmentId,
        "system:mes",
        $"mes:work-order-released:{organizationId}:{environmentId}:{workOrderId}",
        new WorkOrderReleasedPayload(
            workOrderId,
            "SKU-FG-1000",
            1000m,
            ReleasedAtUtc,
            [new ReleasedOperationPayload(operationId, 10, "WC-001")]));

    /// <summary>
    /// 从 <c>/metrics</c> 走的同一条导出路径取样本值。断言指标对象的内存字段只能证明「Set 被调过」，
    /// 证不到那个值真的会出现在导出面上。
    /// </summary>
    internal static async Task<IReadOnlyDictionary<string, double>> ScrapeAsync(CollectorRegistry registry)
    {
        using var buffer = new MemoryStream();
        await registry.CollectAndExportAsTextAsync(buffer, CancellationToken.None);
        var samples = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var line in Encoding.UTF8.GetString(buffer.ToArray()).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            var separator = trimmed.LastIndexOf(' ');
            if (separator <= 0)
            {
                continue;
            }

            samples[trimmed[..separator]] = double.Parse(
                trimmed[(separator + 1)..],
                CultureInfo.InvariantCulture);
        }

        return samples;
    }

    internal sealed class NoopMediator : IMediator
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
