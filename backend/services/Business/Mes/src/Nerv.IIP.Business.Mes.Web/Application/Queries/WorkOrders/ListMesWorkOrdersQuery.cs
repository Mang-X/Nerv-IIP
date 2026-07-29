using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;

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
    string? DeviceAssetIds = null,
    string? Statuses = null,
    string? AssignedUserIds = null,
    string? TeamIds = null) : IQuery<ListMesWorkOrdersResponse>;

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
    string? WorkCenterName = null)
{
    public IReadOnlyCollection<string> AllowedActions { get; init; } = [];

    public IReadOnlyCollection<string> BlockReasons { get; init; } = [];

    public DateTimeOffset EvaluatedAtUtc { get; init; }
}

public sealed class ListMesWorkOrdersQueryHandler(
    ApplicationDbContext dbContext,
    TimeProvider? timeProvider = null)
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

        // 多状态过滤(CSV,与 WorkCenterIds/DeviceAssetIds 同一约定)。排产工作台等消费方需要
        // 一次取回全部非终态工单;单值 Status 只能取一种,而默认排序按 DueUtc 升序会把交期最早的
        // 历史关单排在前面,分页窗口内全是终态、真正可排的工单永远取不到。
        var statuses = SplitCsv(request.Statuses).Select(x => x.ToLowerInvariant()).ToArray();
        if (statuses.Length > 0)
        {
            workOrdersQuery = workOrdersQuery.Where(x => statuses.Contains(x.Status.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            workOrdersQuery = workOrdersQuery.Where(x =>
                x.WorkOrderIdValue.ToLower().Contains(keyword) ||
                x.SkuId.ToLower().Contains(keyword) ||
                (x.ProductionVersionId != null && x.ProductionVersionId.ToLower().Contains(keyword)));
        }

        var workCenterId = request.WorkCenterId?.Trim();
        var workCenterIds = SplitCsv(request.WorkCenterIds);
        var hasWorkCenterScope = request.WorkCenterIds is not null;
        var shiftId = request.ShiftId?.Trim();
        var deviceAssetId = request.DeviceAssetId?.Trim();
        var deviceAssetIds = SplitCsv(request.DeviceAssetIds);
        var assignedUserIds = SplitCsv(request.AssignedUserIds);
        var hasAssignedUserScope = request.AssignedUserIds is not null;
        var teamIds = SplitCsv(request.TeamIds);
        var hasTeamScope = request.TeamIds is not null;
        var hasTaskFilters = !string.IsNullOrWhiteSpace(request.WorkCenterId) ||
            request.WorkCenterIds is not null ||
            !string.IsNullOrWhiteSpace(request.ShiftId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetIds) ||
            request.AssignedUserIds is not null ||
            request.TeamIds is not null;
        if (hasTaskFilters)
        {
            workOrdersQuery = workOrdersQuery.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                task.WorkOrderId == x.WorkOrderIdValue &&
                (workCenterId == null || task.WorkCenterId == workCenterId) &&
                (!hasWorkCenterScope || workCenterIds.Contains(task.WorkCenterId)) &&
                (shiftId == null || task.ShiftId == shiftId) &&
                (deviceAssetId == null || task.DeviceAssetId == deviceAssetId) &&
                (deviceAssetIds.Count == 0 || deviceAssetIds.Contains(task.DeviceAssetId)) &&
                (!hasAssignedUserScope || assignedUserIds.Contains(task.AssignedUserId)) &&
                (!hasTeamScope || teamIds.Contains(task.TeamId))));
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
                workOrderIds.Contains(x.WorkOrderId) &&
                (!hasTaskFilters ||
                    ((workCenterId == null || x.WorkCenterId == workCenterId) &&
                     (!hasWorkCenterScope || workCenterIds.Contains(x.WorkCenterId)) &&
                     (shiftId == null || x.ShiftId == shiftId) &&
                     (deviceAssetId == null || x.DeviceAssetId == deviceAssetId) &&
                     (deviceAssetIds.Count == 0 || deviceAssetIds.Contains(x.DeviceAssetId)) &&
                     (!hasAssignedUserScope || assignedUserIds.Contains(x.AssignedUserId)) &&
                     (!hasTeamScope || teamIds.Contains(x.TeamId)))))
            .OrderBy(x => x.OperationSequence)
            .ThenBy(x => x.OperationTaskIdValue)
            .ToListAsync(cancellationToken);
        var evaluatedAtUtc = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var taskReadiness = await new MesOperationTaskActionReadinessEvaluator(dbContext)
            .EvaluateManyAsync(tasks, evaluatedAtUtc, cancellationToken);

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
                x => x.Select(task =>
                {
                    var readiness = taskReadiness[task.OperationTaskIdValue];
                    return new MesOperationTaskExecutionFact(
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
                        null)
                    {
                        AllowedActions = readiness.AllowedActions,
                        BlockReasons = readiness.BlockReasons,
                        EvaluatedAtUtc = readiness.EvaluatedAtUtc,
                    };
                }).ToArray(),
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
