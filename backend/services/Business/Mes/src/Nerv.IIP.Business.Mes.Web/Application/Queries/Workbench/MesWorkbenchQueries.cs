using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

public sealed record GetMesFoundationReadinessAreaQuery(
    string OrganizationId,
    string EnvironmentId,
    string AreaCode,
    string? SiteCode,
    string? LineCode,
    string? WorkCenterCode,
    string? SkuId,
    string? ProductionVersionId,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedEndUtc) : IQuery<MesReadinessArea>;

public sealed record MesReadinessArea(
    string AreaCode,
    string Status,
    IReadOnlyCollection<MesReadinessIssue> Issues);

public sealed record MesReadinessIssue(
    string Code,
    string Severity,
    string Message,
    string? SourceSystem,
    string? ReferenceType,
    string? ReferenceId,
    string? ReferenceDisplayName,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string? Version,
    string? FixHint);

public sealed class GetMesFoundationReadinessAreaQueryHandler
    : IQueryHandler<GetMesFoundationReadinessAreaQuery, MesReadinessArea>
{
    public Task<MesReadinessArea> Handle(
        GetMesFoundationReadinessAreaQuery request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new MesReadinessArea(request.AreaCode, "Ready", []));
    }
}

public sealed record ListProductionPlansQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Take = 100) : IQuery<MesProductionPlanListResponse>;

public sealed record MesProductionPlanListResponse(IReadOnlyCollection<MesProductionPlanRow> Items);

public sealed record MesProductionPlanRow(
    string ProductionPlanId,
    string SourceSystem,
    string SourceDocumentId,
    string SkuId,
    decimal PlannedQuantity,
    string UomCode,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedEndUtc,
    string Status,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons);

public sealed class ListProductionPlansQueryHandler
    : IQueryHandler<ListProductionPlansQuery, MesProductionPlanListResponse>
{
    public Task<MesProductionPlanListResponse> Handle(ListProductionPlansQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MesProductionPlanListResponse([]));
    }
}

public sealed record GetProductionPlanReadinessQuery(
    string OrganizationId,
    string EnvironmentId,
    string ProductionPlanId) : IQuery<MesProductionPlanReadinessResponse>;

public sealed record MesProductionPlanReadinessResponse(
    string Status,
    IReadOnlyCollection<MesReadinessArea> Areas,
    IReadOnlyCollection<MesReadinessIssue> BlockingIssues,
    IReadOnlyCollection<MesReadinessIssue> WarningIssues);

public sealed class GetProductionPlanReadinessQueryHandler
    : IQueryHandler<GetProductionPlanReadinessQuery, MesProductionPlanReadinessResponse>
{
    public Task<MesProductionPlanReadinessResponse> Handle(GetProductionPlanReadinessQuery request, CancellationToken cancellationToken)
    {
        var areas = new[]
        {
            new MesReadinessArea("master-data", "Ready", []),
            new MesReadinessArea("product-engineering", "Ready", []),
            new MesReadinessArea("supply", "Ready", []),
            new MesReadinessArea("quality", "Ready", []),
            new MesReadinessArea("equipment", "Ready", []),
            new MesReadinessArea("barcode-numbering", "Ready", []),
        };
        return Task.FromResult(new MesProductionPlanReadinessResponse("Ready", areas, [], []));
    }
}

public sealed record GetMesOverviewQuery(
    string OrganizationId,
    string EnvironmentId) : IQuery<MesOverviewResponse>;

public sealed record MesOverviewResponse(
    IReadOnlyCollection<MesCockpitCount> Counts,
    IReadOnlyCollection<MesBlockerSummary> Blockers,
    IReadOnlyCollection<MesPendingWorkItem> PendingWork);

public sealed record MesCockpitCount(string Key, int Count, string Status);

public sealed record MesBlockerSummary(string AreaCode, string Code, string Message, int Count);

public sealed record MesPendingWorkItem(string RoleCode, string WorkType, int Count, string? RouteHint);

public sealed class GetMesOverviewQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetMesOverviewQuery, MesOverviewResponse>
{
    public async Task<MesOverviewResponse> Handle(GetMesOverviewQuery request, CancellationToken cancellationToken)
    {
        var workOrderCount = await dbContext.WorkOrders
            .AsNoTracking()
            .CountAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId, cancellationToken);
        var operationCount = await dbContext.OperationTasks
            .AsNoTracking()
            .CountAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId, cancellationToken);

        return new MesOverviewResponse(
            [
                new MesCockpitCount("work-orders", workOrderCount, "Ready"),
                new MesCockpitCount("operation-tasks", operationCount, "Ready"),
            ],
            [],
            []);
    }
}

public sealed record GetMesWorkOrderDetailQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId) : IQuery<MesWorkOrderDetailResponse>;

public sealed record MesWorkOrderDetailResponse(
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    string Status,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons,
    IReadOnlyCollection<MesOperationTaskRow> OperationTasks);

public sealed record MesOperationTaskRow(
    string OperationTaskId,
    string WorkOrderId,
    string Status,
    int OperationSequence,
    string WorkCenterId,
    string? DeviceAssetId,
    string? ShiftId,
    string? AssignedUserId,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? StartedAtUtc,
    string QualityStatus);

public sealed class GetMesWorkOrderDetailQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetMesWorkOrderDetailQuery, MesWorkOrderDetailResponse>
{
    public async Task<MesWorkOrderDetailResponse> Handle(GetMesWorkOrderDetailQuery request, CancellationToken cancellationToken)
    {
        var workOrder = await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId)
            .Select(x => new
            {
                x.WorkOrderIdValue,
                x.SkuId,
                x.ProductionVersionId,
                x.Quantity,
                x.Status,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");

        var tasks = await QueryOperationTasks(dbContext, request.OrganizationId, request.EnvironmentId, request.WorkOrderId, null, 500)
            .ToArrayAsync(cancellationToken);

        return new MesWorkOrderDetailResponse(
            workOrder.WorkOrderIdValue,
            workOrder.SkuId,
            workOrder.ProductionVersionId,
            workOrder.Quantity,
            workOrder.Status,
            "Ready",
            [],
            tasks);
    }

    internal static IQueryable<MesOperationTaskRow> QueryOperationTasks(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string? workOrderId,
        string? status,
        int take)
    {
        var query = dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId);

        if (!string.IsNullOrWhiteSpace(workOrderId))
        {
            query = query.Where(x => x.WorkOrderId == workOrderId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status.ToString() == status);
        }

        return query
            .OrderBy(x => x.EarliestStartUtc)
            .ThenBy(x => x.OperationSequence)
            .ThenBy(x => x.OperationTaskIdValue)
            .Take(Math.Clamp(take, 1, 500))
            .Select(x => new MesOperationTaskRow(
                x.OperationTaskIdValue,
                x.WorkOrderId,
                x.Status.ToString(),
                x.OperationSequence,
                x.WorkCenterId,
                null,
                null,
                null,
                x.EarliestStartUtc,
                x.ExistingStartUtc,
                "Ready"));
    }
}

public sealed record ListOperationTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Take = 100) : IQuery<MesOperationTaskListResponse>;

public sealed record MesOperationTaskListResponse(IReadOnlyCollection<MesOperationTaskRow> Items);

public sealed class ListOperationTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListOperationTasksQuery, MesOperationTaskListResponse>
{
    public async Task<MesOperationTaskListResponse> Handle(ListOperationTasksQuery request, CancellationToken cancellationToken)
    {
        var items = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTasks(dbContext, request.OrganizationId, request.EnvironmentId, null, request.Status, request.Take)
            .ToArrayAsync(cancellationToken);
        return new MesOperationTaskListResponse(items);
    }
}

public sealed record ListMaterialIssueRequestsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Take = 100) : IQuery<MesMaterialIssueRequestListResponse>;

public sealed record MesMaterialIssueRequestListResponse(IReadOnlyCollection<MesMaterialIssueRequestRow> Items);

public sealed record MesMaterialIssueRequestRow(
    string RequestId,
    string WorkOrderId,
    string? OperationTaskId,
    string Status,
    DateTimeOffset RequestedAtUtc);

public sealed class ListMaterialIssueRequestsQueryHandler
    : IQueryHandler<ListMaterialIssueRequestsQuery, MesMaterialIssueRequestListResponse>
{
    public Task<MesMaterialIssueRequestListResponse> Handle(ListMaterialIssueRequestsQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MesMaterialIssueRequestListResponse([]));
    }
}

public sealed record ListDispatchTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Take = 100) : IQuery<MesDispatchTaskListResponse>;

public sealed record MesDispatchTaskListResponse(IReadOnlyCollection<MesDispatchTaskRow> Items);

public sealed record MesDispatchTaskRow(
    string OperationTaskId,
    string WorkOrderId,
    string Status,
    string WorkCenterId,
    string? DeviceAssetId,
    string? ShiftId,
    string? AssignedUserId,
    DateTimeOffset? PlannedStartUtc,
    IReadOnlyCollection<string> BlockingReasons);

public sealed class ListDispatchTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListDispatchTasksQuery, MesDispatchTaskListResponse>
{
    public async Task<MesDispatchTaskListResponse> Handle(ListDispatchTasksQuery request, CancellationToken cancellationToken)
    {
        var tasks = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTasks(dbContext, request.OrganizationId, request.EnvironmentId, null, request.Status, request.Take)
            .Select(x => new MesDispatchTaskRow(
                x.OperationTaskId,
                x.WorkOrderId,
                x.Status,
                x.WorkCenterId,
                x.DeviceAssetId,
                x.ShiftId,
                x.AssignedUserId,
                x.PlannedStartUtc,
                Array.Empty<string>()))
            .ToArrayAsync(cancellationToken);
        return new MesDispatchTaskListResponse(tasks);
    }
}

public sealed record GetMaterialReadinessQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId) : IQuery<MesMaterialReadinessResponse>;

public sealed record MesMaterialReadinessResponse(
    string WorkOrderId,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons,
    IReadOnlyCollection<MesMaterialReadinessRow> Items);

public sealed record MesMaterialReadinessRow(
    string MaterialId,
    string? MaterialLotId,
    decimal RequiredQuantity,
    decimal AvailableQuantity,
    decimal RequestedQuantity,
    decimal StagedQuantity,
    decimal ReceivedQuantity,
    decimal ShortageQuantity,
    string Status);

public sealed class GetMaterialReadinessQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetMaterialReadinessQuery, MesMaterialReadinessResponse>
{
    public async Task<MesMaterialReadinessResponse> Handle(GetMaterialReadinessQuery request, CancellationToken cancellationToken)
    {
        var exists = await dbContext.WorkOrders
            .AsNoTracking()
            .AnyAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId,
                cancellationToken);
        if (!exists)
        {
            throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");
        }

        return new MesMaterialReadinessResponse(request.WorkOrderId, "Ready", [], []);
    }
}

public sealed record GetWipSummaryQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Take = 100) : IQuery<MesWipSummaryResponse>;

public sealed record MesWipSummaryResponse(IReadOnlyCollection<MesWipSummaryRow> Items);

public sealed record MesWipSummaryRow(
    string WorkOrderId,
    string OperationTaskId,
    string WorkCenterId,
    string Status,
    decimal PlannedQuantity,
    decimal GoodQuantity,
    decimal ScrapQuantity,
    IReadOnlyCollection<string> BlockingReasons);

public sealed class GetWipSummaryQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetWipSummaryQuery, MesWipSummaryResponse>
{
    public async Task<MesWipSummaryResponse> Handle(GetWipSummaryQuery request, CancellationToken cancellationToken)
    {
        var tasks = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTasks(dbContext, request.OrganizationId, request.EnvironmentId, null, request.Status, request.Take)
            .ToArrayAsync(cancellationToken);
        var workOrderIds = tasks.Select(x => x.WorkOrderId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var operationTaskIds = tasks.Select(x => x.OperationTaskId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var quantities = await dbContext.WorkOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                workOrderIds.Contains(x.WorkOrderIdValue))
            .Select(x => new { x.WorkOrderIdValue, x.Quantity })
            .ToDictionaryAsync(x => x.WorkOrderIdValue, x => x.Quantity, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var reports = await dbContext.ProductionReports
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                operationTaskIds.Contains(x.OperationTaskId))
            .GroupBy(x => x.OperationTaskId)
            .Select(x => new
            {
                OperationTaskId = x.Key,
                GoodQuantity = x.Sum(report => report.GoodQuantity),
                ScrapQuantity = x.Sum(report => report.ScrapQuantity),
            })
            .ToDictionaryAsync(x => x.OperationTaskId, x => x, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var items = tasks.Select(task =>
        {
            reports.TryGetValue(task.OperationTaskId, out var report);
            return new MesWipSummaryRow(
                task.WorkOrderId,
                task.OperationTaskId,
                task.WorkCenterId,
                task.Status,
                quantities.GetValueOrDefault(task.WorkOrderId),
                report?.GoodQuantity ?? 0m,
                report?.ScrapQuantity ?? 0m,
                []);
        }).ToArray();

        return new MesWipSummaryResponse(items);
    }
}

public sealed record ListRelatedQualityItemsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string? OperationTaskId,
    int Take = 100) : IQuery<MesRelatedQualityItemListResponse>;

public sealed record MesRelatedQualityItemListResponse(IReadOnlyCollection<MesRelatedQualityItemRow> Items);

public sealed record MesRelatedQualityItemRow(
    string ItemId,
    string ItemType,
    string WorkOrderId,
    string? OperationTaskId,
    string Status,
    string Summary);

public sealed class ListRelatedQualityItemsQueryHandler
    : IQueryHandler<ListRelatedQualityItemsQuery, MesRelatedQualityItemListResponse>
{
    public Task<MesRelatedQualityItemListResponse> Handle(ListRelatedQualityItemsQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MesRelatedQualityItemListResponse([]));
    }
}

public sealed record ListDowntimeEventsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkCenterId,
    string? DeviceAssetId,
    int Take = 100) : IQuery<MesDowntimeEventListResponse>;

public sealed record MesDowntimeEventListResponse(IReadOnlyCollection<MesDowntimeEventRow> Items);

public sealed record MesDowntimeEventRow(
    string DowntimeEventId,
    string WorkOrderId,
    string? OperationTaskId,
    string? DeviceAssetId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? RecoveredAtUtc,
    string WorkCenterId,
    string ReasonCode);

public sealed class ListDowntimeEventsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListDowntimeEventsQuery, MesDowntimeEventListResponse>
{
    public async Task<MesDowntimeEventListResponse> Handle(ListDowntimeEventsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.WorkCenterUnavailabilities
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId))
        {
            query = query.Where(x => x.WorkCenterId == request.WorkCenterId);
        }

        if (!string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            query = query.Where(x => x.DeviceAssetId == request.DeviceAssetId);
        }

        var items = await query
            .OrderByDescending(x => x.FromUtc)
            .Take(Math.Clamp(request.Take, 1, 500))
            .Select(x => new MesDowntimeEventRow(
                x.Id.Id.ToString(),
                x.WorkCenterId,
                null,
                x.DeviceAssetId,
                x.ToUtc == null ? "Open" : "Recovered",
                x.FromUtc,
                x.ToUtc,
                x.WorkCenterId,
                x.Reason))
            .ToArrayAsync(cancellationToken);
        return new MesDowntimeEventListResponse(items);
    }
}

public sealed record ListShiftHandoversQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ShiftId,
    int Take = 100) : IQuery<MesShiftHandoverListResponse>;

public sealed record MesShiftHandoverListResponse(IReadOnlyCollection<MesShiftHandoverRow> Items);

public sealed record MesShiftHandoverRow(
    string HandoverId,
    string ShiftId,
    string TeamId,
    string HandoverStatus,
    int OpenIssueCount,
    DateTimeOffset CreatedAtUtc);

public sealed class ListShiftHandoversQueryHandler
    : IQueryHandler<ListShiftHandoversQuery, MesShiftHandoverListResponse>
{
    public Task<MesShiftHandoverListResponse> Handle(ListShiftHandoversQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MesShiftHandoverListResponse([]));
    }
}

public sealed record GetWorkOrderTraceabilityQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId) : IQuery<MesTraceabilityResponse>;

public sealed record MesTraceabilityResponse(
    IReadOnlyCollection<MesTraceabilityNode> Nodes,
    IReadOnlyCollection<MesTraceabilityEdge> Edges);

public sealed record MesTraceabilityNode(string NodeId, string NodeType, string DisplayName, string Status);

public sealed record MesTraceabilityEdge(string FromNodeId, string ToNodeId, string RelationType);

public sealed class GetWorkOrderTraceabilityQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetWorkOrderTraceabilityQuery, MesTraceabilityResponse>
{
    public async Task<MesTraceabilityResponse> Handle(GetWorkOrderTraceabilityQuery request, CancellationToken cancellationToken)
    {
        var detail = await new GetMesWorkOrderDetailQueryHandler(dbContext).Handle(
            new GetMesWorkOrderDetailQuery(request.OrganizationId, request.EnvironmentId, request.WorkOrderId),
            cancellationToken);
        var reports = await dbContext.ProductionReports
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId)
            .Select(x => new { Id = x.Id.ToString(), x.OperationTaskId })
            .ToArrayAsync(cancellationToken);

        var nodes = new List<MesTraceabilityNode>
        {
            new(detail.WorkOrderId, "WorkOrder", detail.WorkOrderId, detail.Status),
        };
        var edges = new List<MesTraceabilityEdge>();

        foreach (var task in detail.OperationTasks)
        {
            nodes.Add(new MesTraceabilityNode(task.OperationTaskId, "OperationTask", task.OperationTaskId, task.Status));
            edges.Add(new MesTraceabilityEdge(detail.WorkOrderId, task.OperationTaskId, "has-operation"));
        }

        foreach (var report in reports)
        {
            nodes.Add(new MesTraceabilityNode(report.Id, "ProductionReport", report.Id, "Reported"));
            edges.Add(new MesTraceabilityEdge(report.OperationTaskId, report.Id, "has-report"));
        }

        return new MesTraceabilityResponse(nodes, edges);
    }
}

public sealed record GetBatchTraceabilityQuery(
    string OrganizationId,
    string EnvironmentId,
    string BatchOrSerial) : IQuery<MesTraceabilityResponse>;

public sealed class GetBatchTraceabilityQueryHandler
    : IQueryHandler<GetBatchTraceabilityQuery, MesTraceabilityResponse>
{
    public Task<MesTraceabilityResponse> Handle(GetBatchTraceabilityQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MesTraceabilityResponse(
            [new MesTraceabilityNode(request.BatchOrSerial, "BatchOrSerial", request.BatchOrSerial, "Unknown")],
            []));
    }
}

public sealed record GetMaterialLotTraceabilityQuery(
    string OrganizationId,
    string EnvironmentId,
    string MaterialLotId) : IQuery<MesTraceabilityResponse>;

public sealed class GetMaterialLotTraceabilityQueryHandler
    : IQueryHandler<GetMaterialLotTraceabilityQuery, MesTraceabilityResponse>
{
    public Task<MesTraceabilityResponse> Handle(GetMaterialLotTraceabilityQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MesTraceabilityResponse(
            [new MesTraceabilityNode(request.MaterialLotId, "MaterialLot", request.MaterialLotId, "Unknown")],
            []));
    }
}
