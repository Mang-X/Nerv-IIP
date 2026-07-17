using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

public sealed record ListMesWorkOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? WorkCenterIds = null,
    string? DeviceAssetIds = null) : IQuery<ListMesWorkOrdersResponse>;

public sealed record ListMesWorkOrdersResponse(
    IReadOnlyCollection<MesWorkOrderExecutionFact> Items,
    int Total);

public sealed record MesWorkOrderExecutionFact(
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    string? UomCode,
    decimal CompletedQuantity,
    int Priority,
    DateTimeOffset DueUtc,
    string Status,
    IReadOnlyCollection<MesOperationTaskExecutionFact> OperationTasks,
    string? WorkOrderNo = null,
    string? SkuCode = null,
    // 工单当前是否存在活跃质量保留(quality hold);供列表锁定图标标记。与工单生命周期 Status 无关
    // (质量保留不改工单状态),故用独立标志而非从 Status 推断。
    bool HasActiveQualityHold = false);

public sealed record MesOperationTaskExecutionFact(
    string OperationTaskId,
    string Status,
    int OperationSequence,
    string WorkCenterId,
    IReadOnlyCollection<string> AlternativeWorkCenterIds,
    DateTimeOffset EarliestStartUtc,
    long DurationTicks,
    DateTimeOffset? ExistingStartUtc,
    DateTimeOffset? ExistingEndUtc,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null);

public sealed class ListMesWorkOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMesWorkOrdersQuery, ListMesWorkOrdersResponse>
{
    public async Task<ListMesWorkOrdersResponse> Handle(ListMesWorkOrdersQuery request, CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 500);
        var workOrdersQuery = dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            workOrdersQuery = workOrdersQuery.Where(x => x.Status.ToLower() == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            workOrdersQuery = workOrdersQuery.Where(x =>
                x.WorkOrderIdValue.ToLower().Contains(keyword) ||
                x.SkuId.ToLower().Contains(keyword) ||
                (x.ProductionVersionId != null && x.ProductionVersionId.ToLower().Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId) ||
            !string.IsNullOrWhiteSpace(request.WorkCenterIds) ||
            !string.IsNullOrWhiteSpace(request.ShiftId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetIds))
        {
            var workCenterId = request.WorkCenterId?.Trim();
            var workCenterIds = SplitCsv(request.WorkCenterIds);
            var shiftId = request.ShiftId?.Trim();
            var deviceAssetId = request.DeviceAssetId?.Trim();
            var deviceAssetIds = SplitCsv(request.DeviceAssetIds);
            workOrdersQuery = workOrdersQuery.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                task.WorkOrderId == x.WorkOrderIdValue &&
                (workCenterId == null || task.WorkCenterId == workCenterId) &&
                (workCenterIds.Count == 0 || workCenterIds.Contains(task.WorkCenterId)) &&
                (shiftId == null || task.ShiftId == shiftId) &&
                (deviceAssetId == null || task.DeviceAssetId == deviceAssetId) &&
                (deviceAssetIds.Count == 0 || deviceAssetIds.Contains(task.DeviceAssetId))));
        }

        var total = await workOrdersQuery.CountAsync(cancellationToken);
        var workOrders = await workOrdersQuery
            .OrderBy(x => x.DueUtc)
            .ThenBy(x => x.WorkOrderIdValue)
            .Skip(skip)
            .Take(take)
            .Select(x => new
            {
                x.WorkOrderIdValue,
                x.SkuId,
                x.ProductionVersionId,
                x.Quantity,
                x.UomCode,
                x.CompletedQuantity,
                x.Priority,
                x.DueUtc,
                x.Status,
            })
            .ToListAsync(cancellationToken);

        // Keep this IN-list bounded by the clamped `take` value above; this endpoint returns a
        // compact execution snapshot for scheduling/acceptance flows, not an unbounded export.
        var workOrderIds = workOrders.Select(x => x.WorkOrderIdValue).ToArray();
        var tasks = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .OrderBy(x => x.OperationSequence)
            .ThenBy(x => x.OperationTaskIdValue)
            .Select(x => new
            {
                x.WorkOrderId,
                x.OperationTaskIdValue,
                x.Status,
                x.OperationSequence,
                x.WorkCenterId,
                x.AlternativeWorkCenterIds,
                x.EarliestStartUtc,
                x.DurationTicks,
                x.ExistingStartUtc,
                x.ExistingEndUtc,
            })
            .ToListAsync(cancellationToken);

        // 活跃质量保留的工单集合(锁定图标)。质量保留按 WorkOrderId 去规范化,只需该批工单是否命中,
        // 故用 EXISTS 语义投影出集合,避免逐行子查询。
        var heldWorkOrderIds = await dbContext.QualityHoldContexts
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.Active &&
                workOrderIds.Contains(x.WorkOrderId))
            .Select(x => x.WorkOrderId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var heldWorkOrderIdSet = heldWorkOrderIds.ToHashSet(StringComparer.Ordinal);

        var tasksByWorkOrder = tasks
            .GroupBy(x => x.WorkOrderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.Select(task => new MesOperationTaskExecutionFact(
                    task.OperationTaskIdValue,
                    task.Status.ToString(),
                    task.OperationSequence,
                    task.WorkCenterId,
                    SplitAlternatives(task.AlternativeWorkCenterIds),
                    task.EarliestStartUtc,
                    task.DurationTicks,
                    task.ExistingStartUtc,
                    task.ExistingEndUtc,
                    task.OperationTaskIdValue,
                    task.WorkCenterId,
                    null)).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var items = workOrders.Select(x => new MesWorkOrderExecutionFact(
            x.WorkOrderIdValue,
            x.SkuId,
            x.ProductionVersionId,
            x.Quantity,
            x.UomCode,
            x.CompletedQuantity,
            x.Priority,
            x.DueUtc,
            x.Status,
            tasksByWorkOrder.GetValueOrDefault(x.WorkOrderIdValue, []),
            x.WorkOrderIdValue,
            x.SkuId,
            heldWorkOrderIdSet.Contains(x.WorkOrderIdValue))).ToArray();

        return new ListMesWorkOrdersResponse(items, total);
    }

    private static IReadOnlyCollection<string> SplitAlternatives(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyCollection<string> SplitCsv(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
    }
}
