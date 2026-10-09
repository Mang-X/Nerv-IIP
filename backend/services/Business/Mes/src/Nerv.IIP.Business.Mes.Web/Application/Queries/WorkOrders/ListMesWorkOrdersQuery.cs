using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.Contracts.DemandPlanning;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

public sealed record ListMesWorkOrdersQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = OffsetPage.DefaultTake,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? WorkCenterIds = null,
    string? DeviceAssetIds = null,
    string? Statuses = null,
    string? AssignedUserIds = null,
    string? TeamIds = null,
    string? WorkOrderId = null,
    string? AuthorizedAssignedUserIds = null,
    string? AuthorizedTeamIds = null,
    string? AuthorizedWorkCenterIds = null) : IQuery<ListMesWorkOrdersResponse>;

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
    bool HasActiveQualityHold = false,
    string WorkOrderType = WorkOrder.StandardType,
    string? SourceWorkOrderId = null,
    string? SourceNcrId = null,
    string? SourceNcrCode = null,
    bool HasChangedDemand = false,
    bool HasCancelledDemand = false,
    IReadOnlyCollection<string>? AssemblyParentWorkOrderIds = null,
    bool IsRush = false,
    MesSourcePlanReferenceResponse? SourcePlanReference = null);

/// <summary>
/// MES 工单列表公开的工序执行事实。<paramref name="OperationTaskId"/> 是 MES 持久化工序身份，
/// Scheduling 等跨服务消费者必须原样沿用；<paramref name="OperationSequence"/> 只表达同一工单内的路线顺序。
/// </summary>
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
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        var page = OffsetPage.From(request.Skip, request.Take);
        var keyword = SearchTerm.From(request.Keyword).Value;
        var workOrdersQuery = dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId && x.EnvironmentId == tenant.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.WorkOrderId))
        {
            var workOrderId = request.WorkOrderId.Trim();
            workOrdersQuery = workOrdersQuery.Where(x => x.WorkOrderIdValue == workOrderId);
        }

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

        if (keyword is not null)
        {
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
        var authorizedUsers = SplitCsv(request.AuthorizedAssignedUserIds);
        var authorizedTeams = SplitCsv(request.AuthorizedTeamIds);
        var authorizedWorkCenters = SplitCsv(request.AuthorizedWorkCenterIds);
        var hasAuthorizationScope = request.AuthorizedAssignedUserIds is not null ||
            request.AuthorizedTeamIds is not null || request.AuthorizedWorkCenterIds is not null;
        // 同一查询同时用于工单 EXISTS 和返回工序；授权维度 OR，业务筛选仍为 AND。
        var visibleTasks = dbContext.OperationTasks.AsNoTracking().Where(task =>
            task.OrganizationId == tenant.OrganizationId &&
            task.EnvironmentId == tenant.EnvironmentId &&
            (!hasTaskFilters ||
                ((workCenterId == null || task.WorkCenterId == workCenterId) &&
                 (!hasWorkCenterScope || workCenterIds.Contains(task.WorkCenterId)) &&
                 (shiftId == null || task.ShiftId == shiftId) &&
                 (deviceAssetId == null || task.DeviceAssetId == deviceAssetId) &&
                 (deviceAssetIds.Count == 0 || deviceAssetIds.Contains(task.DeviceAssetId)) &&
                 (!hasAssignedUserScope || assignedUserIds.Contains(task.AssignedUserId)) &&
                 (!hasTeamScope || teamIds.Contains(task.TeamId)))) &&
            (!hasAuthorizationScope ||
                authorizedUsers.Contains(task.AssignedUserId) ||
                authorizedTeams.Contains(task.TeamId) ||
                authorizedWorkCenters.Contains(task.WorkCenterId)));
        if (hasTaskFilters || hasAuthorizationScope)
        {
            workOrdersQuery = workOrdersQuery.Where(x =>
                visibleTasks.Any(task => task.WorkOrderId == x.WorkOrderIdValue));
        }

        var total = await workOrdersQuery.CountAsync(cancellationToken);
        var workOrders = await workOrdersQuery
            .OrderBy(x => x.DueUtc)
            .ThenBy(x => x.WorkOrderIdValue)
            .Skip(page.Skip)
            .Take(page.Take)
            .Select(x => new
            {
                x.WorkOrderIdValue,
                x.SkuId,
                x.ProductionVersionId,
                x.Quantity,
                x.UomCode,
                x.CompletedQuantity,
                x.Priority,
                x.IsRush,
                x.DueUtc,
                x.Status,
                x.WorkOrderType,
                x.SourceWorkOrderId,
                x.SourceNcrId,
                x.SourceNcrCode,
                x.SourcePlanReference,
            })
            .ToListAsync(cancellationToken);

        var parentSuggestionIds = workOrders
            .SelectMany(x => x.SourcePlanReference?.AssemblyParentSuggestionIds ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var parentWorkOrders = await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x => x.OrganizationId == tenant.OrganizationId &&
                x.EnvironmentId == tenant.EnvironmentId &&
                x.SourcePlanReference != null &&
                x.SourcePlanReference.SourceSystem == DemandPlanningSourceReferences.DemandPlanning &&
                x.SourcePlanReference.SourceDocumentType == DemandPlanningSourceReferences.PlanningSuggestion &&
                parentSuggestionIds.Contains(x.SourcePlanReference.SourceDocumentId))
            .Select(x => new { x.WorkOrderIdValue, x.SourcePlanReference!.SourceDocumentId })
            .ToListAsync(cancellationToken);
        var parentIdsBySuggestion = parentWorkOrders
            .GroupBy(x => x.SourceDocumentId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Select(order => order.WorkOrderIdValue).ToArray(), StringComparer.Ordinal);

        // Keep this IN-list bounded by the clamped `take` value above; this endpoint returns a
        // compact execution snapshot for scheduling/acceptance flows, not an unbounded export.
        var workOrderIds = workOrders.Select(x => x.WorkOrderIdValue).ToArray();
        var tasks = await visibleTasks
            .Where(x => workOrderIds.Contains(x.WorkOrderId))
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
                x.OrganizationId == tenant.OrganizationId &&
                x.EnvironmentId == tenant.EnvironmentId &&
                x.Active &&
                workOrderIds.Contains(x.WorkOrderId))
            .Select(x => x.WorkOrderId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var heldWorkOrderIdSet = heldWorkOrderIds.ToHashSet(StringComparer.Ordinal);

        var demandChanges = await dbContext.WorkOrderDemandChanges
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == tenant.OrganizationId &&
                x.EnvironmentId == tenant.EnvironmentId &&
                workOrderIds.Contains(x.WorkOrderId))
            .Select(x => new { x.WorkOrderId, x.Cancelled })
            .ToListAsync(cancellationToken);
        var changedDemandIds = demandChanges.Where(x => !x.Cancelled)
            .Select(x => x.WorkOrderId).ToHashSet(StringComparer.Ordinal);
        var cancelledDemandIds = demandChanges.Where(x => x.Cancelled)
            .Select(x => x.WorkOrderId).ToHashSet(StringComparer.Ordinal);

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
            heldWorkOrderIdSet.Contains(x.WorkOrderIdValue),
            x.WorkOrderType,
            x.SourceWorkOrderId,
            x.SourceNcrId,
            x.SourceNcrCode,
            changedDemandIds.Contains(x.WorkOrderIdValue),
            cancelledDemandIds.Contains(x.WorkOrderIdValue),
            (x.SourcePlanReference?.AssemblyParentSuggestionIds ?? [])
                .SelectMany(id => parentIdsBySuggestion.GetValueOrDefault(id, []))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            x.IsRush,
            x.SourcePlanReference is null ? null : new MesSourcePlanReferenceResponse(
                x.SourcePlanReference.SourceSystem, x.SourcePlanReference.SourceDocumentType,
                x.SourcePlanReference.SourceDocumentId, x.SourcePlanReference.SourceDemandReference,
                x.SourcePlanReference.SourceDemandReferences))).ToArray();

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
