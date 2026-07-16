using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
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
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? Source = null,
    string? ReadinessStatus = null) : IQuery<MesProductionPlanListResponse>;

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
            query = query.Where(x => x.Status.ToLower() == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(x =>
                x.WorkOrderIdValue.ToLower().Contains(keyword) ||
                x.Status.ToLower().Contains(keyword) ||
                x.SkuId.ToLower().Contains(keyword) ||
                (x.ProductionVersionId != null && x.ProductionVersionId.ToLower().Contains(keyword)) ||
                (x.SourcePlanReference != null &&
                    (x.SourcePlanReference.SourceSystem.ToLower().Contains(keyword) ||
                        x.SourcePlanReference.SourceDocumentType.ToLower().Contains(keyword) ||
                        x.SourcePlanReference.SourceDocumentId.ToLower().Contains(keyword) ||
                        (x.SourcePlanReference.SourceDemandReference != null &&
                            x.SourcePlanReference.SourceDemandReference.ToLower().Contains(keyword)))));
        }

        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            var source = request.Source.Trim().ToLower();
            query = query.Where(x =>
                x.SourcePlanReference != null &&
                (x.SourcePlanReference.SourceSystem.ToLower().Contains(source) ||
                    x.SourcePlanReference.SourceDocumentType.ToLower().Contains(source) ||
                    x.SourcePlanReference.SourceDocumentId.ToLower().Contains(source) ||
                    (x.SourcePlanReference.SourceDemandReference != null &&
                        x.SourcePlanReference.SourceDemandReference.ToLower().Contains(source))));
        }

        if (!string.IsNullOrWhiteSpace(request.ReadinessStatus))
        {
            var readinessStatus = request.ReadinessStatus.Trim();
            // Rows in this list currently compute readiness as Ready only; non-Ready filters intentionally return no rows.
            query = string.Equals(readinessStatus, "Ready", StringComparison.OrdinalIgnoreCase)
                ? query
                : query.Where(_ => false);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId) ||
            !string.IsNullOrWhiteSpace(request.ShiftId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            var workCenterId = request.WorkCenterId?.Trim();
            var shiftId = request.ShiftId?.Trim();
            var deviceAssetId = request.DeviceAssetId?.Trim();
            query = query.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                task.WorkOrderId == x.WorkOrderIdValue &&
                (workCenterId == null || task.WorkCenterId == workCenterId) &&
                (shiftId == null || task.ShiftId == shiftId) &&
                (deviceAssetId == null || task.DeviceAssetId == deviceAssetId)));
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
            new MesReadinessArea("barcode-coding", "Ready", []),
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
    MesSourcePlanReferenceResponse? SourcePlanReference = null,
    IReadOnlyCollection<MesWorkOrderQualityHoldSummary>? ActiveQualityHolds = null);

// 工单当前活跃质量保留（quality hold）投影,供工单详情 hold 区块直接接时间线查询与人工强制释放。
// SourceService + SourceDocumentId 是时间线/强制释放的定位键;Scope 区分工单级与工序级保留。
public sealed record MesWorkOrderQualityHoldSummary(
    string SourceService,
    string SourceDocumentId,
    string Scope,
    string? OperationTaskId,
    string? HoldReason,
    DateTimeOffset? HeldAtUtc,
    string? HeldBy,
    string? HeldInspectionRecordId,
    string? HeldInspectionDocumentId,
    string InspectionRecordId);

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
    string QualityStatus,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null,
    string? OperationCode = null,
    DateTimeOffset? ScheduledAtUtc = null,
    string? ScheduleInvalidationReasonCode = null);

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

        // 单工单活跃保留数量极小(通常 1~2 项),排序放在内存端,规避 SQLite 等 provider 不支持
        // DateTimeOffset ORDER BY 的翻译差异(生产 Postgres 排序等价),口径:最近施加优先。
        var activeHoldRows = await dbContext.QualityHoldContexts
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId &&
                x.Active)
            .Select(x => new MesWorkOrderQualityHoldSummary(
                x.SourceService,
                x.SourceDocumentId,
                x.OperationTaskId == null ? "work-order" : "operation-task",
                x.OperationTaskId,
                x.HoldReason ?? x.DispositionReason,
                x.HeldAtUtc,
                x.HeldBy,
                x.HeldInspectionRecordId,
                x.HeldInspectionDocumentId,
                x.InspectionRecordId))
            .ToArrayAsync(cancellationToken);
        // 最近施加优先;HeldAtUtc 可空/相等时以 SourceDocumentId 兜底,保证顺序确定(review)。
        var activeHolds = activeHoldRows
            .OrderByDescending(x => x.HeldAtUtc)
            .ThenBy(x => x.SourceDocumentId, StringComparer.Ordinal)
            .ToArray();

        return new MesWorkOrderDetailResponse(
            workOrder.WorkOrderIdValue,
            workOrder.SkuId,
            workOrder.ProductionVersionId,
            workOrder.Quantity,
            workOrder.Status,
            "Ready",
            [],
            tasks,
            workOrder.SourcePlanReference,
            activeHolds);
    }

    internal static IQueryable<MesOperationTaskRow> QueryOperationTasks(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string? workOrderId,
        string? status,
        int skip,
        int take,
        string? keyword = null,
        string? workCenterId = null,
        string? shiftId = null,
        string? deviceAssetId = null)
    {
        var query = QueryOperationTaskEntities(
            dbContext,
            organizationId,
            environmentId,
            workOrderId,
            status,
            keyword,
            workCenterId,
            shiftId,
            deviceAssetId);

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
                "Ready",
                x.WorkOrderId,
                x.OperationTaskIdValue,
                x.WorkCenterId,
                null,
                x.DeviceAssetId,
                null,
                x.OperationCode,
                x.ScheduledAtUtc,
                x.ScheduleInvalidationReasonCode));
    }

    internal static IQueryable<Domain.AggregatesModel.OperationTaskAggregate.OperationTask> QueryOperationTaskEntities(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string? workOrderId,
        string? status,
        string? keyword = null,
        string? workCenterId = null,
        string? shiftId = null,
        string? deviceAssetId = null)
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
            query = TryParseOperationTaskStatus(status, out var parsedStatus)
                ? query.Where(x => x.Status == parsedStatus)
                : query.Where(_ => false);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var normalizedKeyword = keyword.Trim().ToLowerInvariant();
            var matchingStatuses = MatchingOperationTaskStatuses(normalizedKeyword);
            query = matchingStatuses.Length > 0
                ? query.Where(x =>
                    x.OperationTaskIdValue.ToLower().Contains(normalizedKeyword) ||
                    x.WorkOrderId.ToLower().Contains(normalizedKeyword) ||
                    x.WorkCenterId.ToLower().Contains(normalizedKeyword) ||
                    (x.DeviceAssetId != null && x.DeviceAssetId.ToLower().Contains(normalizedKeyword)) ||
                    (x.ShiftId != null && x.ShiftId.ToLower().Contains(normalizedKeyword)) ||
                    matchingStatuses.Contains(x.Status))
                : query.Where(x =>
                    x.OperationTaskIdValue.ToLower().Contains(normalizedKeyword) ||
                    x.WorkOrderId.ToLower().Contains(normalizedKeyword) ||
                    x.WorkCenterId.ToLower().Contains(normalizedKeyword) ||
                    (x.DeviceAssetId != null && x.DeviceAssetId.ToLower().Contains(normalizedKeyword)) ||
                    (x.ShiftId != null && x.ShiftId.ToLower().Contains(normalizedKeyword)));
        }

        if (!string.IsNullOrWhiteSpace(workCenterId))
        {
            var normalizedWorkCenterId = workCenterId.Trim();
            query = query.Where(x => x.WorkCenterId == normalizedWorkCenterId);
        }

        if (!string.IsNullOrWhiteSpace(shiftId))
        {
            var normalizedShiftId = shiftId.Trim();
            query = query.Where(x => x.ShiftId == normalizedShiftId);
        }

        if (!string.IsNullOrWhiteSpace(deviceAssetId))
        {
            var normalizedDeviceAssetId = deviceAssetId.Trim();
            query = query.Where(x => x.DeviceAssetId == normalizedDeviceAssetId);
        }

        return query;
    }

    private static bool TryParseOperationTaskStatus(string status, out OperationTaskLifecycleStatus parsedStatus)
    {
        return Enum.TryParse(status.Trim(), ignoreCase: true, out parsedStatus);
    }

    private static OperationTaskLifecycleStatus[] MatchingOperationTaskStatuses(string normalizedKeyword)
    {
        return Enum.GetValues<OperationTaskLifecycleStatus>()
            .Where(status => status.ToString().Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}

public sealed record ListOperationTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null) : IQuery<MesOperationTaskListResponse>;

public sealed record MesOperationTaskListResponse(
    IReadOnlyCollection<MesOperationTaskRow> Items,
    int Total);

public sealed class ListOperationTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListOperationTasksQuery, MesOperationTaskListResponse>
{
    public async Task<MesOperationTaskListResponse> Handle(ListOperationTasksQuery request, CancellationToken cancellationToken)
    {
        var total = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTaskEntities(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                request.Status,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId)
            .CountAsync(cancellationToken);
        var items = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTasks(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                request.Status,
                request.Skip,
                request.Take,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId)
            .ToArrayAsync(cancellationToken);
        return new MesOperationTaskListResponse(items, total);
    }
}

public sealed record ListMaterialIssueRequestsQuery(
    string OrganizationId,
    string EnvironmentId,
    string? WorkOrderId,
    int Skip = 0,
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? Status = null) : IQuery<MesMaterialIssueRequestListResponse>;

public sealed record MesMaterialIssueRequestListResponse(
    IReadOnlyCollection<MesMaterialIssueRequestRow> Items,
    int Total);

public sealed record MesMaterialIssueRequestRow(
    string RequestId,
    string WorkOrderId,
    string? OperationTaskId,
    string MaterialId,
    string UomCode,
    string? MaterialLotId,
    decimal RequestedQuantity,
    decimal ReceivedQuantity,
    decimal ConsumedQuantity,
    string Status,
    DateTimeOffset RequestedAtUtc,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? MaterialCode = null,
    string? InventoryPostingFailureCode = null,
    string? InventoryPostingFailureMessage = null,
    DateTimeOffset? InventoryPostingFailedAtUtc = null);

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

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(x =>
                x.RequestNo.ToLower().Contains(keyword) ||
                x.WorkOrderId.ToLower().Contains(keyword) ||
                (x.OperationTaskId != null && x.OperationTaskId.ToLower().Contains(keyword)) ||
                x.MaterialId.ToLower().Contains(keyword) ||
                (x.MaterialLotId != null && x.MaterialLotId.ToLower().Contains(keyword)) ||
                x.Status.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLower();
            query = query.Where(x => x.Status.ToLower() == status);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId) ||
            !string.IsNullOrWhiteSpace(request.ShiftId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            var workCenterId = request.WorkCenterId?.Trim();
            var shiftId = request.ShiftId?.Trim();
            var deviceAssetId = request.DeviceAssetId?.Trim();
            query = query.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                ((x.OperationTaskId != null && task.OperationTaskIdValue == x.OperationTaskId) ||
                    (x.OperationTaskId == null && task.WorkOrderId == x.WorkOrderId)) &&
                (workCenterId == null || task.WorkCenterId == workCenterId) &&
                (shiftId == null || task.ShiftId == shiftId) &&
                (deviceAssetId == null || task.DeviceAssetId == deviceAssetId)));
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
                x.UomCode,
                x.MaterialLotId,
                x.RequestedQuantity,
                x.ReceivedQuantity,
                // Authoritative consumed-so-far, matching WorkOrderCancellation's derivation
                // (sum of production-report consumptions for this request/material/lot). Lets the cancel
                // preview compute returnable = max(0, received - consumed) instead of assuming received.
                dbContext.ProductionReportMaterialConsumptions
                    .Where(c => c.OrganizationId == x.OrganizationId
                        && c.EnvironmentId == x.EnvironmentId
                        && c.MaterialIssueRequestNo == x.RequestNo
                        && c.MaterialId == x.MaterialId
                        && c.MaterialLotId == x.MaterialLotId)
                    .Sum(c => (decimal?)c.ConsumedQuantity) ?? 0m,
                x.Status,
                x.RequestedAtUtc,
                x.WorkOrderId,
                x.OperationTaskId,
                x.MaterialId,
                x.InventoryPostingFailureCode,
                x.InventoryPostingFailureMessage,
                x.InventoryPostingFailedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new MesMaterialIssueRequestListResponse(items, total);
    }
}

public sealed record ListDispatchTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    int Skip = 0,
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null) : IQuery<MesDispatchTaskListResponse>;

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
    IReadOnlyCollection<string> BlockingReasons,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null,
    DateTimeOffset? ScheduledAtUtc = null,
    string? ScheduleInvalidationReasonCode = null);

public sealed class ListDispatchTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListDispatchTasksQuery, MesDispatchTaskListResponse>
{
    public async Task<MesDispatchTaskListResponse> Handle(ListDispatchTasksQuery request, CancellationToken cancellationToken)
    {
        var total = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTaskEntities(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                request.Status,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId)
            .CountAsync(cancellationToken);
        var tasks = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTasks(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                request.Status,
                request.Skip,
                request.Take,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId)
            .Select(x => new MesDispatchTaskRow(
                x.OperationTaskId,
                x.WorkOrderId,
                x.Status,
                x.WorkCenterId,
                x.DeviceAssetId,
                x.ShiftId,
                x.AssignedUserId,
                x.PlannedStartUtc,
                Array.Empty<string>(),
                x.WorkOrderId,
                x.OperationTaskId,
                x.WorkCenterId,
                null,
                x.DeviceAssetId,
                null,
                x.ScheduledAtUtc,
                x.ScheduleInvalidationReasonCode))
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
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null) : IQuery<MesWipSummaryResponse>;

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
    IReadOnlyCollection<string> BlockingReasons,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? WorkCenterCode = null,
    string? WorkCenterName = null);

public sealed class GetWipSummaryQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetWipSummaryQuery, MesWipSummaryResponse>
{
    public async Task<MesWipSummaryResponse> Handle(GetWipSummaryQuery request, CancellationToken cancellationToken)
    {
        var total = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTaskEntities(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                request.Status,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId)
            .CountAsync(cancellationToken);
        var tasks = await GetMesWorkOrderDetailQueryHandler
            .QueryOperationTasks(
                dbContext,
                request.OrganizationId,
                request.EnvironmentId,
                null,
                request.Status,
                request.Skip,
                request.Take,
                request.Keyword,
                request.WorkCenterId,
                request.ShiftId,
                request.DeviceAssetId)
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
                [],
                task.WorkOrderId,
                task.OperationTaskId,
                task.WorkCenterId,
                null);
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
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? ShiftId = null,
    string? DeviceAssetId = null,
    string? Status = null) : IQuery<MesRelatedQualityItemListResponse>;

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

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(x =>
                x.DefectNo.ToLower().Contains(keyword) ||
                x.WorkOrderId.ToLower().Contains(keyword) ||
                (x.OperationTaskId != null && x.OperationTaskId.ToLower().Contains(keyword)) ||
                x.Status.ToLower().Contains(keyword) ||
                x.DefectCode.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLower();
            query = query.Where(x => x.Status.ToLower() == status);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId) ||
            !string.IsNullOrWhiteSpace(request.ShiftId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            var workCenterId = request.WorkCenterId?.Trim();
            var shiftId = request.ShiftId?.Trim();
            var deviceAssetId = request.DeviceAssetId?.Trim();
            query = query.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                ((x.OperationTaskId != null && task.OperationTaskIdValue == x.OperationTaskId) ||
                    (x.OperationTaskId == null && task.WorkOrderId == x.WorkOrderId)) &&
                (workCenterId == null || task.WorkCenterId == workCenterId) &&
                (shiftId == null || task.ShiftId == shiftId) &&
                (deviceAssetId == null || task.DeviceAssetId == deviceAssetId)));
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
    int Take = 100,
    string? Keyword = null,
    string? ShiftId = null,
    string? Status = null) : IQuery<MesDowntimeEventListResponse>;

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
    string ReasonCode,
    string? WorkOrderNo = null,
    string? OperationTaskNo = null,
    string? DeviceAssetCode = null,
    string? DeviceAssetName = null);

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

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(x =>
                x.DowntimeEventNo.ToLower().Contains(keyword) ||
                x.WorkCenterId.ToLower().Contains(keyword) ||
                (x.DeviceAssetId != null && x.DeviceAssetId.ToLower().Contains(keyword)) ||
                x.Reason.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = status switch
            {
                "open" => query.Where(x => x.ToUtc == null),
                "recovered" => query.Where(x => x.ToUtc != null),
                _ => query.Where(_ => false),
            };
        }

        if (!string.IsNullOrWhiteSpace(request.ShiftId))
        {
            var shiftId = request.ShiftId.Trim();
            query = query.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                task.WorkCenterId == x.WorkCenterId &&
                task.ShiftId == shiftId &&
                (x.DeviceAssetId == null || task.DeviceAssetId == x.DeviceAssetId)));
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
                x.Reason,
                null,
                null,
                x.DeviceAssetId,
                null))
            .ToArrayAsync(cancellationToken);
        return new MesDowntimeEventListResponse(items, total);
    }
}

public sealed record ListShiftHandoversQuery(
    string OrganizationId,
    string EnvironmentId,
    string? ShiftId,
    int Skip = 0,
    int Take = 100,
    string? Keyword = null,
    string? WorkCenterId = null,
    string? DeviceAssetId = null,
    string? Status = null) : IQuery<MesShiftHandoverListResponse>;

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

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(x =>
                x.HandoverNo.ToLower().Contains(keyword) ||
                x.ShiftId.ToLower().Contains(keyword) ||
                x.TeamId.ToLower().Contains(keyword) ||
                x.HandoverStatus.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLower();
            query = query.Where(x => x.HandoverStatus.ToLower() == status);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkCenterId) ||
            !string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            var workCenterId = request.WorkCenterId?.Trim();
            var deviceAssetId = request.DeviceAssetId?.Trim();
            query = query.Where(x => dbContext.OperationTasks.Any(task =>
                task.OrganizationId == request.OrganizationId &&
                task.EnvironmentId == request.EnvironmentId &&
                task.ShiftId == x.ShiftId &&
                (workCenterId == null || task.WorkCenterId == workCenterId) &&
                (deviceAssetId == null || task.DeviceAssetId == deviceAssetId)));
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

public static class MesTraceabilityProductionReportQueries
{
    public static IQueryable<ProductionReport> ActiveProductionReports(this ApplicationDbContext dbContext)
    {
        return dbContext.ProductionReports
            .AsNoTracking()
            .Where(report =>
                report.ReversedReportNo == null &&
                !dbContext.ProductionReports.Any(reversal =>
                    reversal.OrganizationId == report.OrganizationId &&
                    reversal.EnvironmentId == report.EnvironmentId &&
                    reversal.ReversedReportNo == report.ReportNo));
    }
}

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
        var activeProductionReports = dbContext.ActiveProductionReports();
        var reports = await activeProductionReports
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId)
            .Select(x => new { Id = x.ReportNo, x.OperationTaskId, x.ProducedLotNo, x.SerialNo })
            .ToArrayAsync(cancellationToken);
        var activeReportNos = reports.Select(x => x.Id).ToArray();

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
            if (!string.IsNullOrWhiteSpace(report.ProducedLotNo))
            {
                nodes.Add(new MesTraceabilityNode(report.ProducedLotNo, "ProducedLot", report.ProducedLotNo, "Produced"));
                edges.Add(new MesTraceabilityEdge(report.Id, report.ProducedLotNo, "produced-lot"));
            }

            if (!string.IsNullOrWhiteSpace(report.SerialNo))
            {
                nodes.Add(new MesTraceabilityNode(report.SerialNo, "Serial", report.SerialNo, "Produced"));
                edges.Add(new MesTraceabilityEdge(report.Id, report.SerialNo, "produced-serial"));
            }
        }

        var consumptions = await dbContext.ProductionReportMaterialConsumptions
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId &&
                activeReportNos.Contains(x.ReportNo))
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
        var activeProductionReports = dbContext.ActiveProductionReports();
        var consumptions = await dbContext.ProductionReportMaterialConsumptions
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.MaterialLotId == request.BatchOrSerial &&
                activeProductionReports.Any(report =>
                    report.OrganizationId == x.OrganizationId &&
                    report.EnvironmentId == x.EnvironmentId &&
                    report.ReportNo == x.ReportNo))
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

        var producedReports = await activeProductionReports
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                (x.ProducedLotNo == request.BatchOrSerial || x.SerialNo == request.BatchOrSerial))
            .Select(x => new { x.ReportNo, x.WorkOrderId, x.OperationTaskId, x.ProducedLotNo, x.SerialNo })
            .ToArrayAsync(cancellationToken);

        if (consumptions.Length == 0 && producedReports.Length == 0)
        {
            return new MesTraceabilityResponse(
                [new MesTraceabilityNode(request.BatchOrSerial, "BatchOrSerial", request.BatchOrSerial, "Unknown")],
                []);
        }

        var nodes = new List<MesTraceabilityNode>
        {
            new(request.BatchOrSerial, producedReports.Length > 0 ? "ProducedLotOrSerial" : "MaterialLot", request.BatchOrSerial, producedReports.Length > 0 ? "Produced" : "Consumed"),
        };
        var edges = new List<MesTraceabilityEdge>();

        foreach (var report in producedReports)
        {
            nodes.Add(new MesTraceabilityNode(report.WorkOrderId, "WorkOrder", report.WorkOrderId, "Reported"));
            nodes.Add(new MesTraceabilityNode(report.OperationTaskId, "OperationTask", report.OperationTaskId, "Reported"));
            nodes.Add(new MesTraceabilityNode(report.ReportNo, "ProductionReport", report.ReportNo, "Reported"));
            edges.Add(new MesTraceabilityEdge(report.ReportNo, request.BatchOrSerial, report.SerialNo == request.BatchOrSerial ? "produced-serial" : "produced-lot"));
            edges.Add(new MesTraceabilityEdge(report.ReportNo, report.OperationTaskId, "reported-operation"));
            edges.Add(new MesTraceabilityEdge(report.OperationTaskId, report.WorkOrderId, "belongs-to-work-order"));
        }

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
        var activeProductionReports = dbContext.ActiveProductionReports();
        var consumptions = await dbContext.ProductionReportMaterialConsumptions
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.MaterialLotId == request.MaterialLotId &&
                activeProductionReports.Any(report =>
                    report.OrganizationId == x.OrganizationId &&
                    report.EnvironmentId == x.EnvironmentId &&
                    report.ReportNo == x.ReportNo))
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
        var reportNos = consumptions.Select(x => x.ReportNo).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var producedReports = await dbContext.ProductionReports
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                reportNos.Contains(x.ReportNo))
            .Select(x => new { x.ReportNo, x.ProducedLotNo, x.SerialNo })
            .ToArrayAsync(cancellationToken);

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

        foreach (var report in producedReports)
        {
            if (!string.IsNullOrWhiteSpace(report.ProducedLotNo))
            {
                nodes.Add(new MesTraceabilityNode(report.ProducedLotNo, "ProducedLot", report.ProducedLotNo, "Produced"));
                edges.Add(new MesTraceabilityEdge(report.ReportNo, report.ProducedLotNo, "produced-lot"));
            }

            if (!string.IsNullOrWhiteSpace(report.SerialNo))
            {
                nodes.Add(new MesTraceabilityNode(report.SerialNo, "Serial", report.SerialNo, "Produced"));
                edges.Add(new MesTraceabilityEdge(report.ReportNo, report.SerialNo, "produced-serial"));
            }
        }

        return new MesTraceabilityResponse(
            nodes.DistinctBy(x => new { x.NodeId, x.NodeType }).ToArray(),
            edges.DistinctBy(x => new { x.FromNodeId, x.ToNodeId, x.RelationType }).ToArray());
    }
}
