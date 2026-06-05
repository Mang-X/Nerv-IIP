using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;

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

public sealed class GetMesFoundationReadinessAreaQueryHandler(MesFoundationReadinessService readinessService)
    : IQueryHandler<GetMesFoundationReadinessAreaQuery, MesReadinessArea>
{
    public async Task<MesReadinessArea> Handle(
        GetMesFoundationReadinessAreaQuery request,
        CancellationToken cancellationToken)
    {
        return await readinessService.GetAreaAsync(request, cancellationToken);
    }
}

public sealed class MesFoundationReadinessService(ApplicationDbContext dbContext)
{
    public async Task<MesReadinessArea> GetAreaAsync(
        GetMesFoundationReadinessAreaQuery request,
        CancellationToken cancellationToken)
    {
        var normalizedAreaCode = NormalizeAreaCode(request.AreaCode);
        var issues = normalizedAreaCode switch
        {
            "quality" => BuildQualityIssues(request),
            "equipment" => await BuildEquipmentIssuesAsync(request, cancellationToken),
            _ => [],
        };

        return new MesReadinessArea(normalizedAreaCode, StatusFromIssues(issues), issues);
    }

    private static string NormalizeAreaCode(string areaCode) =>
        string.IsNullOrWhiteSpace(areaCode) ? "unknown" : areaCode.Trim().ToLowerInvariant();

    private static IReadOnlyCollection<MesReadinessIssue> BuildQualityIssues(GetMesFoundationReadinessAreaQuery request)
    {
        if (string.IsNullOrWhiteSpace(request.SkuId) && string.IsNullOrWhiteSpace(request.ProductionVersionId))
        {
            // No execution context supplied; context-specific checks are handled by the execution workbench.
            return [];
        }

        return
        [
            NewIssue(
                MesReadinessReasonCodes.QualityPlanMissing,
                "未解析到已发布的 SKU/工序检验方案，首检、巡检和终检要求不能放行。",
                "Quality",
                "InspectionPlan",
                request.ProductionVersionId ?? request.SkuId,
                "维护并启用对应 SKU 与工序的检验方案"),
        ];
    }

    private async Task<IReadOnlyCollection<MesReadinessIssue>> BuildEquipmentIssuesAsync(
        GetMesFoundationReadinessAreaQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.WorkCenterCode))
        {
            // No execution context supplied; context-specific checks are handled by the execution workbench.
            return [];
        }

        var windowStart = request.PlannedStartUtc ?? DateTimeOffset.UtcNow;
        var windowEnd = request.PlannedEndUtc ?? windowStart;
        if (windowEnd < windowStart)
        {
            (windowStart, windowEnd) = (windowEnd, windowStart);
        }

        var workCenterCode = request.WorkCenterCode.Trim();
        var unavailabilities = await dbContext.WorkCenterUnavailabilities
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkCenterId == workCenterCode &&
                x.FromUtc <= windowEnd &&
                (x.ToUtc == null || x.ToUtc >= windowStart))
            .OrderBy(x => x.FromUtc)
            .Select(x => new
            {
                x.DowntimeEventNo,
                x.DeviceAssetId,
                x.Reason,
                x.FromUtc,
                x.ToUtc,
            })
            .ToArrayAsync(cancellationToken);

        return unavailabilities
            .Select(x =>
            {
                var classification = MesReadinessReasonCodes.ClassifyEquipmentReason(x.Reason);
                return NewIssue(
                    classification.Code,
                    classification.Message,
                    classification.SourceSystem,
                    "EquipmentAvailability",
                    x.DowntimeEventNo,
                    classification.FixHint,
                    x.FromUtc,
                    x.ToUtc,
                    x.DeviceAssetId);
            })
            .ToArray();
    }

    private static string StatusFromIssues(IReadOnlyCollection<MesReadinessIssue> issues)
    {
        if (issues.Any(x => string.Equals(x.Severity, "Blocked", StringComparison.Ordinal)))
        {
            return "Blocked";
        }

        return issues.Any(x => string.Equals(x.Severity, "Warning", StringComparison.Ordinal)) ? "Warning" : "Ready";
    }

    private static MesReadinessIssue NewIssue(
        string code,
        string message,
        string sourceSystem,
        string? referenceType,
        string? referenceId,
        string fixHint,
        DateTimeOffset? effectiveFromUtc = null,
        DateTimeOffset? effectiveToUtc = null,
        string? referenceDisplayName = null) =>
        new(
            code,
            "Blocked",
            message,
            sourceSystem,
            referenceType,
            referenceId,
            referenceDisplayName,
            effectiveFromUtc,
            effectiveToUtc,
            null,
            fixHint);
}

public sealed record ListProductionPlansQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<MesProductionPlanListResponse>;

public sealed record MesProductionPlanListResponse(
    IReadOnlyCollection<MesProductionPlanRow> Items,
    int Total);

public sealed record MesProductionPlanRow(
    string ProductionPlanId,
    string SourceSystem,
    string SourceDocumentType,
    string SourceDocumentId,
    string? SourceDemandReference,
    string SkuId,
    decimal PlannedQuantity,
    string UomCode,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedEndUtc,
    string Status,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons);

public sealed class ListProductionPlansQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListProductionPlansQuery, MesProductionPlanListResponse>
{
    public async Task<MesProductionPlanListResponse> Handle(ListProductionPlansQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.WorkOrders
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.SourcePlanReference != null);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.DueUtc)
            .ThenBy(x => x.WorkOrderIdValue)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .Select(x => new
            {
                x.WorkOrderIdValue,
                x.SkuId,
                x.Quantity,
                x.UomCode,
                x.DueUtc,
                x.Status,
                SourceSystem = x.SourcePlanReference!.SourceSystem,
                SourceDocumentType = x.SourcePlanReference.SourceDocumentType,
                SourceDocumentId = x.SourcePlanReference.SourceDocumentId,
                x.SourcePlanReference.SourceDemandReference,
            })
            .ToArrayAsync(cancellationToken);

        var items = rows
            .Select(x => new MesProductionPlanRow(
                x.SourceDocumentId,
                x.SourceSystem,
                x.SourceDocumentType,
                x.SourceDocumentId,
                x.SourceDemandReference,
                x.SkuId,
                x.Quantity,
                x.UomCode ?? string.Empty,
                null,
                x.DueUtc,
                x.Status,
                "Ready",
                []))
            .ToArray();
        return new MesProductionPlanListResponse(items, total);
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

public sealed class GetProductionPlanReadinessQueryHandler(MesFoundationReadinessService readinessService)
    : IQueryHandler<GetProductionPlanReadinessQuery, MesProductionPlanReadinessResponse>
{
    public async Task<MesProductionPlanReadinessResponse> Handle(GetProductionPlanReadinessQuery request, CancellationToken cancellationToken)
    {
        var quality = await readinessService.GetAreaAsync(
            new GetMesFoundationReadinessAreaQuery(
                request.OrganizationId,
                request.EnvironmentId,
                "quality",
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            cancellationToken);
        var areas = new[]
        {
            new MesReadinessArea("master-data", "Ready", []),
            new MesReadinessArea("product-engineering", "Ready", []),
            new MesReadinessArea("supply", "Ready", []),
            quality,
            new MesReadinessArea("equipment", "Ready", []),
            new MesReadinessArea("barcode-numbering", "Ready", []),
        };
        var blockingIssues = areas.SelectMany(x => x.Issues).Where(x => x.Severity == "Blocked").ToArray();
        return new MesProductionPlanReadinessResponse(blockingIssues.Length > 0 ? "Blocked" : "Ready", areas, blockingIssues, []);
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

public sealed record MesSourcePlanReferenceResponse(
    string SourceSystem,
    string SourceDocumentType,
    string SourceDocumentId,
    string? SourceDemandReference);

public sealed record MesWorkOrderDetailResponse(
    string WorkOrderId,
    string SkuId,
    string? ProductionVersionId,
    decimal Quantity,
    string Status,
    string ReadinessStatus,
    IReadOnlyCollection<string> BlockingReasons,
    IReadOnlyCollection<MesOperationTaskRow> OperationTasks,
    MesSourcePlanReferenceResponse? SourcePlanReference = null);

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
                SourcePlanReference = x.SourcePlanReference == null
                    ? null
                    : new MesSourcePlanReferenceResponse(
                        x.SourcePlanReference.SourceSystem,
                        x.SourcePlanReference.SourceDocumentType,
                        x.SourcePlanReference.SourceDocumentId,
                        x.SourcePlanReference.SourceDemandReference),
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"未找到生产工单，WorkOrderId = {request.WorkOrderId}");

        var tasks = await QueryOperationTasks(dbContext, request.OrganizationId, request.EnvironmentId, request.WorkOrderId, null, 0, 500)
            .ToArrayAsync(cancellationToken);

        return new MesWorkOrderDetailResponse(
            workOrder.WorkOrderIdValue,
            workOrder.SkuId,
            workOrder.ProductionVersionId,
            workOrder.Quantity,
            workOrder.Status,
            "Ready",
            [],
            tasks,
            workOrder.SourcePlanReference);
    }

    internal static IQueryable<MesOperationTaskRow> QueryOperationTasks(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string? workOrderId,
        string? status,
        int skip,
        int take)
    {
        var query = QueryOperationTaskEntities(dbContext, organizationId, environmentId, workOrderId, status);

        return query
            .OrderBy(x => x.EarliestStartUtc)
            .ThenBy(x => x.OperationSequence)
            .ThenBy(x => x.OperationTaskIdValue)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 500))
            .Select(x => new MesOperationTaskRow(
                x.OperationTaskIdValue,
                x.WorkOrderId,
                x.Status.ToString(),
                x.OperationSequence,
                x.WorkCenterId,
                x.DeviceAssetId,
                x.ShiftId,
                x.AssignedUserId,
                x.EarliestStartUtc,
                x.ExistingStartUtc,
                "Ready"));
    }

    internal static IQueryable<Domain.AggregatesModel.OperationTaskAggregate.OperationTask> QueryOperationTaskEntities(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string? workOrderId,
        string? status)
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

        return query;
    }
}

public sealed record ListOperationTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<MesOperationTaskListResponse>;

public sealed record MesOperationTaskListResponse(
    IReadOnlyCollection<MesOperationTaskRow> Items,
    int Total);

public sealed class ListOperationTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListOperationTasksQuery, MesOperationTaskListResponse>
{
    public async Task<MesOperationTaskListResponse> Handle(ListOperationTasksQuery request, CancellationToken cancellationToken)
    {
        var total = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTaskEntities(dbContext, request.OrganizationId, request.EnvironmentId, null, request.Status)
            .CountAsync(cancellationToken);
        var items = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTasks(dbContext, request.OrganizationId, request.EnvironmentId, null, request.Status, request.Skip, request.Take)
            .ToArrayAsync(cancellationToken);
        return new MesOperationTaskListResponse(items, total);
    }
}

public sealed record ListMaterialIssueRequestsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Skip = 0,
    int Take = 100) : IQuery<MesMaterialIssueRequestListResponse>;

public sealed record MesMaterialIssueRequestListResponse(
    IReadOnlyCollection<MesMaterialIssueRequestRow> Items,
    int Total);

public sealed record MesMaterialIssueRequestRow(
    string RequestId,
    string WorkOrderId,
    string? OperationTaskId,
    string MaterialId,
    string? MaterialLotId,
    decimal RequestedQuantity,
    decimal ReceivedQuantity,
    string Status,
    DateTimeOffset RequestedAtUtc);

public sealed class ListMaterialIssueRequestsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMaterialIssueRequestsQuery, MesMaterialIssueRequestListResponse>
{
    public async Task<MesMaterialIssueRequestListResponse> Handle(ListMaterialIssueRequestsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.MaterialIssueRequests
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.WorkOrderId))
        {
            query = query.Where(x => x.WorkOrderId == request.WorkOrderId);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.RequestedAtUtc)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .Select(x => new MesMaterialIssueRequestRow(
                x.RequestNo,
                x.WorkOrderId,
                x.OperationTaskId,
                x.MaterialId,
                x.MaterialLotId,
                x.RequestedQuantity,
                x.ReceivedQuantity,
                x.Status,
                x.RequestedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new MesMaterialIssueRequestListResponse(items, total);
    }
}

public sealed record ListDispatchTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<MesDispatchTaskListResponse>;

public sealed record MesDispatchTaskListResponse(
    IReadOnlyCollection<MesDispatchTaskRow> Items,
    int Total);

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
        var total = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTaskEntities(dbContext, request.OrganizationId, request.EnvironmentId, null, request.Status)
            .CountAsync(cancellationToken);
        var tasks = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTasks(dbContext, request.OrganizationId, request.EnvironmentId, null, request.Status, request.Skip, request.Take)
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
        return new MesDispatchTaskListResponse(tasks, total);
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

        var requirements = await dbContext.MaterialRequirements
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId)
            .Select(x => new MaterialReadinessGuards.MaterialRequirementSnapshot(
                x.OperationTaskId,
                x.MaterialId,
                x.MaterialLotId,
                x.RequiredQuantity,
                x.AvailableQuantity,
                x.StagedQuantity,
                x.CapturedAtUtc))
            .ToArrayAsync(cancellationToken);
        requirements = MaterialReadinessGuards.SelectLatestRequirementSnapshots(requirements);

        if (requirements.Length == 0)
        {
            return new MesMaterialReadinessResponse(request.WorkOrderId, "Ready", [], []);
        }

        var issues = await dbContext.MaterialIssueRequests
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId)
            .Select(x => new
            {
                x.MaterialId,
                x.MaterialLotId,
                x.RequestedQuantity,
                x.ReceivedQuantity,
            })
            .ToArrayAsync(cancellationToken);

        var rows = requirements
            .GroupBy(x => new { x.MaterialId, x.MaterialLotId })
            .Select(x =>
            {
                var issueRows = issues.Where(y =>
                    string.Equals(y.MaterialId, x.Key.MaterialId, StringComparison.OrdinalIgnoreCase) &&
                    (x.Key.MaterialLotId is null ||
                        string.Equals(y.MaterialLotId, x.Key.MaterialLotId, StringComparison.OrdinalIgnoreCase)));
                var required = x.Sum(y => y.RequiredQuantity);
                var available = x.Sum(y => y.AvailableQuantity);
                var staged = x.Sum(y => y.StagedQuantity);
                var requested = issueRows.Sum(y => y.RequestedQuantity);
                var received = issueRows.Sum(y => y.ReceivedQuantity);
                var shortage = Math.Max(0m, required - available - staged - received);
                return new MesMaterialReadinessRow(
                    x.Key.MaterialId,
                    x.Key.MaterialLotId,
                    required,
                    available,
                    requested,
                    staged,
                    received,
                    shortage,
                    shortage > 0 ? "Shortage" : "Ready");
            })
            .OrderBy(x => x.MaterialId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.MaterialLotId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var blockingReasons = rows
            .Where(x => x.ShortageQuantity > 0)
            .Select(x => x.MaterialLotId is null
                ? $"{x.MaterialId} shortage {x.ShortageQuantity:0.######}"
                : $"{x.MaterialId} {x.MaterialLotId} shortage {x.ShortageQuantity:0.######}")
            .ToArray();
        var status = blockingReasons.Length > 0 ? "Blocked" : "Ready";
        return new MesMaterialReadinessResponse(request.WorkOrderId, status, blockingReasons, rows);
    }
}

public sealed record GetWipSummaryQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100) : IQuery<MesWipSummaryResponse>;

public sealed record MesWipSummaryResponse(
    IReadOnlyCollection<MesWipSummaryRow> Items,
    int Total);

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
        var total = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTaskEntities(dbContext, request.OrganizationId, request.EnvironmentId, null, request.Status)
            .CountAsync(cancellationToken);
        var tasks = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTasks(dbContext, request.OrganizationId, request.EnvironmentId, null, request.Status, request.Skip, request.Take)
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

        return new MesWipSummaryResponse(items, total);
    }
}

public sealed record ListRelatedQualityItemsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    string? OperationTaskId,
    int Skip = 0,
    int Take = 100) : IQuery<MesRelatedQualityItemListResponse>;

public sealed record MesRelatedQualityItemListResponse(
    IReadOnlyCollection<MesRelatedQualityItemRow> Items,
    int Total);

public sealed record MesRelatedQualityItemRow(
    string QualityItemId,
    string SourceType,
    string SourceDocumentId,
    string Status,
    string? DefectCode,
    string? NcrId);

public sealed class ListRelatedQualityItemsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListRelatedQualityItemsQuery, MesRelatedQualityItemListResponse>
{
    public async Task<MesRelatedQualityItemListResponse> Handle(ListRelatedQualityItemsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.DefectRecords
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.WorkOrderId))
        {
            query = query.Where(x => x.WorkOrderId == request.WorkOrderId);
        }

        if (!string.IsNullOrWhiteSpace(request.OperationTaskId))
        {
            query = query.Where(x => x.OperationTaskId == request.OperationTaskId);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.DefectNo)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .Select(x => new MesRelatedQualityItemRow(
                x.DefectNo,
                "Defect",
                x.OperationTaskId ?? x.WorkOrderId,
                x.Status,
                x.DefectCode,
                null))
            .ToArrayAsync(cancellationToken);
        return new MesRelatedQualityItemListResponse(items, total);
    }
}

public sealed record ListDowntimeEventsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkCenterId,
    string? DeviceAssetId,
    int Skip = 0,
    int Take = 100) : IQuery<MesDowntimeEventListResponse>;

public sealed record MesDowntimeEventListResponse(
    IReadOnlyCollection<MesDowntimeEventRow> Items,
    int Total);

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

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.FromUtc)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .Select(x => new MesDowntimeEventRow(
                x.DowntimeEventNo,
                x.WorkCenterId,
                null,
                x.DeviceAssetId,
                x.ToUtc == null ? "Open" : "Recovered",
                x.FromUtc,
                x.ToUtc,
                x.WorkCenterId,
                x.Reason))
            .ToArrayAsync(cancellationToken);
        return new MesDowntimeEventListResponse(items, total);
    }
}

public sealed record ListShiftHandoversQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ShiftId,
    int Skip = 0,
    int Take = 100) : IQuery<MesShiftHandoverListResponse>;

public sealed record MesShiftHandoverListResponse(
    IReadOnlyCollection<MesShiftHandoverRow> Items,
    int Total);

public sealed record MesShiftHandoverRow(
    string HandoverId,
    string ShiftId,
    string TeamId,
    string HandoverStatus,
    int OpenIssueCount,
    DateTimeOffset CreatedAtUtc);

public sealed class ListShiftHandoversQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListShiftHandoversQuery, MesShiftHandoverListResponse>
{
    public async Task<MesShiftHandoverListResponse> Handle(ListShiftHandoversQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ShiftHandovers
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId);

        if (!string.IsNullOrWhiteSpace(request.ShiftId))
        {
            query = query.Where(x => x.ShiftId == request.ShiftId);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.HandoverNo)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .Select(x => new MesShiftHandoverRow(
                x.HandoverNo,
                x.ShiftId,
                x.TeamId,
                x.HandoverStatus,
                x.OpenIssueCount,
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new MesShiftHandoverListResponse(items, total);
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
        var workOrder = await dbContext.WorkOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderIdValue == request.WorkOrderId,
                cancellationToken);
        if (workOrder is null)
        {
            return new MesTraceabilityResponse(
                [new MesTraceabilityNode(request.WorkOrderId, "WorkOrder", request.WorkOrderId, "Unknown")],
                []);
        }

        var detail = await new GetMesWorkOrderDetailQueryHandler(dbContext).Handle(
            new GetMesWorkOrderDetailQuery(request.OrganizationId, request.EnvironmentId, request.WorkOrderId),
            cancellationToken);
        var reports = await dbContext.ProductionReports
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId)
            .Select(x => new { Id = x.ReportNo, x.OperationTaskId })
            .ToArrayAsync(cancellationToken);

        var nodes = new List<MesTraceabilityNode>
        {
            new(detail.WorkOrderId, "WorkOrder", detail.WorkOrderId, detail.Status),
        };
        var edges = new List<MesTraceabilityEdge>();

        if (detail.SourcePlanReference is not null)
        {
            nodes.Add(new MesTraceabilityNode(
                detail.SourcePlanReference.SourceDocumentId,
                detail.SourcePlanReference.SourceDocumentType,
                detail.SourcePlanReference.SourceDocumentId,
                "Source"));
            edges.Add(new MesTraceabilityEdge(
                detail.SourcePlanReference.SourceDocumentId,
                detail.WorkOrderId,
                "converted-to-work-order"));

            if (!string.IsNullOrWhiteSpace(detail.SourcePlanReference.SourceDemandReference))
            {
                nodes.Add(new MesTraceabilityNode(
                    detail.SourcePlanReference.SourceDemandReference,
                    "DemandSource",
                    detail.SourcePlanReference.SourceDemandReference,
                    "Source"));
                edges.Add(new MesTraceabilityEdge(
                    detail.SourcePlanReference.SourceDemandReference,
                    detail.SourcePlanReference.SourceDocumentId,
                    "pegged-to-plan"));
            }
        }

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

        var consumptions = await dbContext.ProductionReportMaterialConsumptions
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId)
            .Select(x => new { x.ReportNo, x.MaterialId, x.MaterialLotId, x.MaterialIssueRequestNo })
            .ToArrayAsync(cancellationToken);
        foreach (var consumption in consumptions)
        {
            nodes.Add(new MesTraceabilityNode(consumption.MaterialId, "Material", consumption.MaterialId, "Consumed"));
            nodes.Add(new MesTraceabilityNode(consumption.MaterialLotId, "MaterialLot", consumption.MaterialLotId, "Consumed"));
            edges.Add(new MesTraceabilityEdge(consumption.MaterialId, consumption.MaterialLotId, "has-lot"));
            edges.Add(new MesTraceabilityEdge(consumption.MaterialLotId, consumption.ReportNo, "consumed-by-report"));
            if (!string.IsNullOrWhiteSpace(consumption.MaterialIssueRequestNo))
            {
                nodes.Add(new MesTraceabilityNode(consumption.MaterialIssueRequestNo, "MaterialIssueRequest", consumption.MaterialIssueRequestNo, "Received"));
                edges.Add(new MesTraceabilityEdge(consumption.MaterialIssueRequestNo, consumption.MaterialLotId, "received-lot"));
            }
        }

        return new MesTraceabilityResponse(nodes, edges);
    }
}

public sealed record GetBatchTraceabilityQuery(
    string OrganizationId,
    string EnvironmentId,
    string BatchOrSerial) : IQuery<MesTraceabilityResponse>;

public sealed class GetBatchTraceabilityQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetBatchTraceabilityQuery, MesTraceabilityResponse>
{
    public async Task<MesTraceabilityResponse> Handle(GetBatchTraceabilityQuery request, CancellationToken cancellationToken)
    {
        var consumptions = await dbContext.ProductionReportMaterialConsumptions
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.MaterialLotId == request.BatchOrSerial)
            .Select(x => new
            {
                x.ReportNo,
                x.WorkOrderId,
                x.OperationTaskId,
                x.MaterialId,
                x.MaterialLotId,
                x.MaterialIssueRequestNo,
            })
            .ToArrayAsync(cancellationToken);

        if (consumptions.Length == 0)
        {
            return new MesTraceabilityResponse(
                [new MesTraceabilityNode(request.BatchOrSerial, "BatchOrSerial", request.BatchOrSerial, "Unknown")],
                []);
        }

        var nodes = new List<MesTraceabilityNode>
        {
            new(request.BatchOrSerial, "MaterialLot", request.BatchOrSerial, "Consumed"),
        };
        var edges = new List<MesTraceabilityEdge>();

        foreach (var consumption in consumptions)
        {
            nodes.Add(new MesTraceabilityNode(consumption.MaterialId, "Material", consumption.MaterialId, "Consumed"));
            nodes.Add(new MesTraceabilityNode(consumption.WorkOrderId, "WorkOrder", consumption.WorkOrderId, "Reported"));
            nodes.Add(new MesTraceabilityNode(consumption.OperationTaskId, "OperationTask", consumption.OperationTaskId, "Reported"));
            nodes.Add(new MesTraceabilityNode(consumption.ReportNo, "ProductionReport", consumption.ReportNo, "Reported"));
            edges.Add(new MesTraceabilityEdge(consumption.MaterialId, consumption.MaterialLotId, "has-lot"));
            edges.Add(new MesTraceabilityEdge(consumption.MaterialLotId, consumption.ReportNo, "consumed-by-report"));
            edges.Add(new MesTraceabilityEdge(consumption.ReportNo, consumption.OperationTaskId, "reported-operation"));
            edges.Add(new MesTraceabilityEdge(consumption.OperationTaskId, consumption.WorkOrderId, "belongs-to-work-order"));
            if (!string.IsNullOrWhiteSpace(consumption.MaterialIssueRequestNo))
            {
                nodes.Add(new MesTraceabilityNode(consumption.MaterialIssueRequestNo, "MaterialIssueRequest", consumption.MaterialIssueRequestNo, "Received"));
                edges.Add(new MesTraceabilityEdge(consumption.MaterialIssueRequestNo, consumption.MaterialLotId, "received-lot"));
            }
        }

        return new MesTraceabilityResponse(
            nodes.DistinctBy(x => new { x.NodeId, x.NodeType }).ToArray(),
            edges.DistinctBy(x => new { x.FromNodeId, x.ToNodeId, x.RelationType }).ToArray());
    }
}

public sealed record GetMaterialLotTraceabilityQuery(
    string OrganizationId,
    string EnvironmentId,
    string MaterialLotId) : IQuery<MesTraceabilityResponse>;

public sealed class GetMaterialLotTraceabilityQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetMaterialLotTraceabilityQuery, MesTraceabilityResponse>
{
    public async Task<MesTraceabilityResponse> Handle(GetMaterialLotTraceabilityQuery request, CancellationToken cancellationToken)
    {
        var consumptions = await dbContext.ProductionReportMaterialConsumptions
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.MaterialLotId == request.MaterialLotId)
            .Select(x => new
            {
                x.ReportNo,
                x.WorkOrderId,
                x.OperationTaskId,
                x.MaterialId,
                x.MaterialLotId,
                x.MaterialIssueRequestNo,
            })
            .ToArrayAsync(cancellationToken);

        var nodes = new List<MesTraceabilityNode>
        {
            new(request.MaterialLotId, "MaterialLot", request.MaterialLotId, consumptions.Length > 0 ? "Consumed" : "Unknown"),
        };
        var edges = new List<MesTraceabilityEdge>();

        foreach (var consumption in consumptions)
        {
            nodes.Add(new MesTraceabilityNode(consumption.MaterialId, "Material", consumption.MaterialId, "Consumed"));
            nodes.Add(new MesTraceabilityNode(consumption.WorkOrderId, "WorkOrder", consumption.WorkOrderId, "Reported"));
            nodes.Add(new MesTraceabilityNode(consumption.OperationTaskId, "OperationTask", consumption.OperationTaskId, "Reported"));
            nodes.Add(new MesTraceabilityNode(consumption.ReportNo, "ProductionReport", consumption.ReportNo, "Reported"));
            edges.Add(new MesTraceabilityEdge(consumption.MaterialId, consumption.MaterialLotId, "has-lot"));
            edges.Add(new MesTraceabilityEdge(consumption.MaterialLotId, consumption.ReportNo, "consumed-by-report"));
            edges.Add(new MesTraceabilityEdge(consumption.ReportNo, consumption.OperationTaskId, "reported-operation"));
            edges.Add(new MesTraceabilityEdge(consumption.OperationTaskId, consumption.WorkOrderId, "belongs-to-work-order"));
            if (!string.IsNullOrWhiteSpace(consumption.MaterialIssueRequestNo))
            {
                nodes.Add(new MesTraceabilityNode(consumption.MaterialIssueRequestNo, "MaterialIssueRequest", consumption.MaterialIssueRequestNo, "Received"));
                edges.Add(new MesTraceabilityEdge(consumption.MaterialIssueRequestNo, consumption.MaterialLotId, "received-lot"));
            }
        }

        return new MesTraceabilityResponse(
            nodes.DistinctBy(x => new { x.NodeId, x.NodeType }).ToArray(),
            edges.DistinctBy(x => new { x.FromNodeId, x.ToNodeId, x.RelationType }).ToArray());
    }
}
