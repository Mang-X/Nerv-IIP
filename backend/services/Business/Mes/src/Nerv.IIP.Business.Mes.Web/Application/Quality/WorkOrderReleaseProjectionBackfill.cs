using DotNetCore.CAP;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Mes;

namespace Nerv.IIP.Business.Mes.Web.Application.Quality;

/// <param name="WorkOrdersScanned">按在制判据选中的工单数。</param>
/// <param name="WorkOrdersPublished">实际补投了发布事实的工单数（存在未完工工序的那些）。</param>
/// <param name="OperationsPublished">补投载荷里携带的工序总数。</param>
public sealed record WorkOrderReleaseProjectionBackfillReport(
    int WorkOrdersScanned,
    int WorkOrdersPublished,
    int OperationsPublished);

public interface IWorkOrderReleaseProjectionBackfillPublisher
{
    Task PublishAsync(WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent);
}

public sealed class CapWorkOrderReleaseProjectionBackfillPublisher(ICapPublisher publisher)
    : IWorkOrderReleaseProjectionBackfillPublisher
{
    public Task PublishAsync(WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent) =>
        publisher.PublishAsync(nameof(WorkOrderReleaseProjectionBackfilledIntegrationEvent), integrationEvent);
}

/// <summary>
/// 存量在制工单的发布事实补投（#3000）。Quality 订阅 <c>mes.WorkOrderReleased</c> 之前发布的工单，
/// 在 Quality 的 <c>PeriodicInspectionOperations</c> 里没有行，首件确认读面恒回 <c>not-synchronized</c>，
/// #2780 的报工门禁会持续拒绝且不靠报工自愈——只能补上发布投影。
/// </summary>
public sealed record BackfillWorkOrderReleaseProjectionCommand
    : IRequest<WorkOrderReleaseProjectionBackfillReport>;

public sealed class BackfillWorkOrderReleaseProjectionCommandHandler(
    ApplicationDbContext dbContext,
    IWorkOrderReleaseProjectionBackfillPublisher publisher,
    TimeProvider timeProvider)
    : IRequestHandler<BackfillWorkOrderReleaseProjectionCommand, WorkOrderReleaseProjectionBackfillReport>
{
    private const int WorkOrderPageSize = 200;

    /// <summary>
    /// 在制工单的可复算判据（不靠人工名单）。逐个状态的取舍：
    /// <list type="bullet">
    /// <item><c>created</c>：尚未发布，还没有发布事实；真正发布时由直投事件覆盖。不回填。</item>
    /// <item><c>released</c>：已发布未开工，后续会报工。回填。</item>
    /// <item><c>started</c>：在制。回填。</item>
    /// <item><c>hold</c>：暂挂但仍在制，解挂后继续报工。回填。</item>
    /// <item><c>completed</c> / <c>closed</c> / <c>cancelled</c> / <c>scrapped</c>：终态，不再报工。不回填。</item>
    /// <item><c>split</c> / <c>merged</c>：本单不再生产，产量转到拆分出的子工单或合并后的目标工单，
    /// 那些工单自己按本判据参与回填。不回填。</item>
    /// </list>
    /// </summary>
    private static readonly string[] InFlightWorkOrderStatuses =
    [
        WorkOrder.ReleasedStatus,
        WorkOrder.StartedStatus,
        WorkOrder.HoldStatus,
    ];

    /// <summary>
    /// 门禁拦的是「还会再报工」的工序，因此只补未完工工序。逐个状态的取舍：
    /// <list type="bullet">
    /// <item><see cref="OperationTaskLifecycleStatus.Queued"/> / <see cref="OperationTaskLifecycleStatus.InProgress"/>
    /// / <see cref="OperationTaskLifecycleStatus.Paused"/>：还会报工。回填。</item>
    /// <item><see cref="OperationTaskLifecycleStatus.ScheduleInvalidated"/>：排程失效待重排，工序本身没完工，
    /// 重排后继续报工。回填。</item>
    /// <item><see cref="OperationTaskLifecycleStatus.Completed"/> / <see cref="OperationTaskLifecycleStatus.Cancelled"/>：
    /// 终态，不会再有报工撞门禁。不回填。</item>
    /// </list>
    /// </summary>
    private static readonly OperationTaskLifecycleStatus[] UnfinishedOperationStatuses =
    [
        OperationTaskLifecycleStatus.Queued,
        OperationTaskLifecycleStatus.InProgress,
        OperationTaskLifecycleStatus.Paused,
        OperationTaskLifecycleStatus.ScheduleInvalidated,
    ];

    public async Task<WorkOrderReleaseProjectionBackfillReport> Handle(
        BackfillWorkOrderReleaseProjectionCommand request,
        CancellationToken cancellationToken)
    {
        var scanned = 0;
        var published = 0;
        var operationsPublished = 0;
        var occurredAtUtc = timeProvider.GetUtcNow();

        for (var skip = 0; ; skip += WorkOrderPageSize)
        {
            var page = await dbContext.WorkOrders
                .AsNoTracking()
                .Where(x => InFlightWorkOrderStatuses.Contains(x.Status))
                .OrderBy(x => x.OrganizationId)
                .ThenBy(x => x.EnvironmentId)
                .ThenBy(x => x.WorkOrderIdValue)
                .Skip(skip)
                .Take(WorkOrderPageSize)
                .Select(x => new
                {
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.WorkOrderIdValue,
                    x.SkuId,
                    x.Quantity,
                })
                .ToArrayAsync(cancellationToken);
            if (page.Length == 0)
            {
                break;
            }

            scanned += page.Length;
            var workOrderIds = page.Select(x => x.WorkOrderIdValue).Distinct(StringComparer.Ordinal).ToArray();
            var operationTasks = await dbContext.OperationTasks
                .AsNoTracking()
                .Where(x => workOrderIds.Contains(x.WorkOrderId))
                .Select(x => new
                {
                    x.OrganizationId,
                    x.EnvironmentId,
                    x.WorkOrderId,
                    x.OperationTaskIdValue,
                    x.OperationSequence,
                    x.WorkCenterId,
                    x.Status,
                    x.CreatedAtUtc,
                })
                .ToArrayAsync(cancellationToken);
            var earliestReports = await dbContext.ProductionReports
                .AsNoTracking()
                .Where(x => workOrderIds.Contains(x.WorkOrderId))
                .GroupBy(x => new { x.OrganizationId, x.EnvironmentId, x.WorkOrderId })
                .Select(group => new
                {
                    group.Key,
                    EarliestReportedAtUtc = group.Min(x => x.ReportedAtUtc),
                })
                .ToArrayAsync(cancellationToken);

            foreach (var workOrder in page)
            {
                var tasks = operationTasks
                    .Where(x => x.OrganizationId == workOrder.OrganizationId
                        && x.EnvironmentId == workOrder.EnvironmentId
                        && string.Equals(x.WorkOrderId, workOrder.WorkOrderIdValue, StringComparison.Ordinal))
                    .ToArray();
                var unfinished = tasks
                    .Where(x => UnfinishedOperationStatuses.Contains(x.Status))
                    .OrderBy(x => x.OperationSequence)
                    .ThenBy(x => x.OperationTaskIdValue, StringComparer.Ordinal)
                    .ToArray();
                if (unfinished.Length == 0)
                {
                    continue;
                }

                // 存量数据里没有留下「当初那一次发布」的时刻（工单聚合不存发布时间，发布事件的
                // ReleasedAtUtc 是发布那一刻的 UtcNow，早已丢失）。Quality 的聚合要求发布时刻不晚于
                // 它已掌握的任何一条报工——报工时刻由调用方填，可以早于工序建单时刻——所以这里取
                // 「该工单最早的工序建单时刻」与「该工单最早的报工时刻」中更早的那个作为发布时刻下界。
                // Quality 收到的报工是 MES 这批报工的子集，因此该下界对它一定成立。
                var earliestTaskCreatedAtUtc = tasks.Min(x => x.CreatedAtUtc);
                var earliestReportedAtUtc = earliestReports
                    .Where(x => x.Key.OrganizationId == workOrder.OrganizationId
                        && x.Key.EnvironmentId == workOrder.EnvironmentId
                        && string.Equals(x.Key.WorkOrderId, workOrder.WorkOrderIdValue, StringComparison.Ordinal))
                    .Select(x => (DateTimeOffset?)x.EarliestReportedAtUtc)
                    .SingleOrDefault();
                var releasedAtUtc = earliestReportedAtUtc.HasValue && earliestReportedAtUtc.Value < earliestTaskCreatedAtUtc
                    ? earliestReportedAtUtc.Value
                    : earliestTaskCreatedAtUtc;

                var idempotencyKey = EventIds.Idempotency(
                    "work-order-release-projection-backfill",
                    workOrder.OrganizationId,
                    workOrder.EnvironmentId,
                    workOrder.WorkOrderIdValue);
                await publisher.PublishAsync(new WorkOrderReleaseProjectionBackfilledIntegrationEvent(
                    $"evt-{Guid.CreateVersion7():N}",
                    MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled,
                    MesIntegrationEventVersions.V1,
                    occurredAtUtc,
                    MesIntegrationEventSources.BusinessMes,
                    idempotencyKey,
                    workOrder.WorkOrderIdValue,
                    workOrder.OrganizationId,
                    workOrder.EnvironmentId,
                    "system:mes",
                    idempotencyKey,
                    new WorkOrderReleasedPayload(
                        workOrder.WorkOrderIdValue,
                        workOrder.SkuId,
                        workOrder.Quantity,
                        releasedAtUtc,
                        unfinished
                            .Select(x => new ReleasedOperationPayload(
                                x.OperationTaskIdValue,
                                x.OperationSequence,
                                x.WorkCenterId))
                            .ToArray())));
                published++;
                operationsPublished += unfinished.Length;
            }

            if (page.Length < WorkOrderPageSize)
            {
                break;
            }
        }

        return new WorkOrderReleaseProjectionBackfillReport(scanned, published, operationsPublished);
    }
}
