using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Queries;

public sealed record PagedMaintenanceListResponse<T>(IReadOnlyCollection<T> Items, int Skip, int Take, int Total);

public sealed record ListMaintenanceWorkOrdersQuery(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100) : IQuery<PagedMaintenanceListResponse<MaintenanceWorkOrderListItem>>;

public sealed record MaintenanceWorkOrderListItem(MaintenanceWorkOrderId WorkOrderId, string DeviceAssetId, string Priority, string Status, string? SourceAlarmId, DateTimeOffset OpenedAtUtc);

public sealed class ListMaintenanceWorkOrdersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMaintenanceWorkOrdersQuery, PagedMaintenanceListResponse<MaintenanceWorkOrderListItem>>
{
    public async Task<PagedMaintenanceListResponse<MaintenanceWorkOrderListItem>> Handle(ListMaintenanceWorkOrdersQuery request, CancellationToken cancellationToken)
    {
        var skip = NormalizeSkip(request.Skip);
        var take = NormalizeTake(request.Take);
        var query = dbContext.MaintenanceWorkOrders
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.OpenedAtUtc)
            .Select(x => new MaintenanceWorkOrderListItem(x.Id, x.DeviceAssetId, x.Priority, x.Status.ToString(), x.SourceAlarmId, x.OpenedAtUtc))
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return new PagedMaintenanceListResponse<MaintenanceWorkOrderListItem>(items, skip, take, total);
    }

    internal static int NormalizeSkip(int skip) => Math.Max(0, skip);

    internal static int NormalizeTake(int take) => Math.Clamp(take, 1, 200);
}

public sealed record ListMaintenancePlansQuery(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100) : IQuery<PagedMaintenanceListResponse<MaintenancePlanListItem>>;

public sealed record MaintenancePlanListItem(MaintenancePlanId PlanId, string DeviceAssetId, string PlanCode, string Interval, DateOnly StartsOn);

public sealed class ListMaintenancePlansQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMaintenancePlansQuery, PagedMaintenanceListResponse<MaintenancePlanListItem>>
{
    public async Task<PagedMaintenanceListResponse<MaintenancePlanListItem>> Handle(ListMaintenancePlansQuery request, CancellationToken cancellationToken)
    {
        var skip = ListMaintenanceWorkOrdersQueryHandler.NormalizeSkip(request.Skip);
        var take = ListMaintenanceWorkOrdersQueryHandler.NormalizeTake(request.Take);
        var query = dbContext.MaintenancePlans
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new MaintenancePlanListItem(x.Id, x.DeviceAssetId, x.PlanCode, x.Interval, x.StartsOn))
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return new PagedMaintenanceListResponse<MaintenancePlanListItem>(items, skip, take, total);
    }
}

public sealed record ListMaintenanceInspectionsQuery(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100) : IQuery<PagedMaintenanceListResponse<MaintenanceInspectionListItem>>;

public sealed record MaintenanceInspectionListItem(
    MaintenanceInspectionId InspectionId,
    MaintenancePlanId? PlanId,
    MaintenanceWorkOrderId? WorkOrderId,
    string Inspector,
    string Result,
    DateTimeOffset InspectedAtUtc);

public sealed class ListMaintenanceInspectionsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMaintenanceInspectionsQuery, PagedMaintenanceListResponse<MaintenanceInspectionListItem>>
{
    public async Task<PagedMaintenanceListResponse<MaintenanceInspectionListItem>> Handle(ListMaintenanceInspectionsQuery request, CancellationToken cancellationToken)
    {
        var skip = ListMaintenanceWorkOrdersQueryHandler.NormalizeSkip(request.Skip);
        var take = ListMaintenanceWorkOrdersQueryHandler.NormalizeTake(request.Take);
        var query = dbContext.MaintenanceInspections
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.InspectedAtUtc)
            .Select(x => new MaintenanceInspectionListItem(x.Id, x.PlanId, x.WorkOrderId, x.Inspector, x.Result, x.InspectedAtUtc))
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return new PagedMaintenanceListResponse<MaintenanceInspectionListItem>(items, skip, take, total);
    }
}

public sealed record ListMaintenanceSparePartsQuery(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100) : IQuery<PagedMaintenanceListResponse<MaintenanceSparePartListItem>>;

public sealed record MaintenanceSparePartListItem(
    SparePartLineId SparePartLineId,
    MaintenanceWorkOrderId WorkOrderId,
    string DeviceAssetId,
    string SkuCode,
    decimal Quantity,
    string? UomCode);

public sealed class ListMaintenanceSparePartsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListMaintenanceSparePartsQuery, PagedMaintenanceListResponse<MaintenanceSparePartListItem>>
{
    public async Task<PagedMaintenanceListResponse<MaintenanceSparePartListItem>> Handle(ListMaintenanceSparePartsQuery request, CancellationToken cancellationToken)
    {
        var skip = ListMaintenanceWorkOrdersQueryHandler.NormalizeSkip(request.Skip);
        var take = ListMaintenanceWorkOrdersQueryHandler.NormalizeTake(request.Take);
        var query =
            from sparePart in dbContext.SparePartLines
            join workOrder in dbContext.MaintenanceWorkOrders
                on EF.Property<MaintenanceWorkOrderId>(sparePart, "MaintenanceWorkOrderId") equals workOrder.Id
            where request.OrganizationId == null || workOrder.OrganizationId == request.OrganizationId
            where request.EnvironmentId == null || workOrder.EnvironmentId == request.EnvironmentId
            select new MaintenanceSparePartListItem(
                sparePart.Id,
                workOrder.Id,
                workOrder.DeviceAssetId,
                sparePart.SkuCode,
                sparePart.Quantity,
                sparePart.UomCode);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.SkuCode)
            .ThenBy(x => x.SparePartLineId)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return new PagedMaintenanceListResponse<MaintenanceSparePartListItem>(items, skip, take, total);
    }
}

public sealed record QueryAssetReliabilityQuery(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc) : IQuery<AssetReliabilityResponse>;

public sealed record AssetReliabilityResponse(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int FailureCount,
    int RepairCount,
    decimal MtbfHours,
    decimal MttrMinutes);

public sealed class QueryAssetReliabilityQueryValidator : AbstractValidator<QueryAssetReliabilityQuery>
{
    public QueryAssetReliabilityQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
    }
}

public sealed class QueryAssetReliabilityQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryAssetReliabilityQuery, AssetReliabilityResponse>
{
    public async Task<AssetReliabilityResponse> Handle(QueryAssetReliabilityQuery request, CancellationToken cancellationToken)
    {
        var windowStartUtc = request.WindowStartUtc.ToUniversalTime();
        var windowEndUtc = request.WindowEndUtc.ToUniversalTime();
        var faultOrders = await dbContext.MaintenanceWorkOrders
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.SourceAlarmId != null)
            .Where(x => x.OpenedAtUtc >= windowStartUtc && x.OpenedAtUtc < windowEndUtc)
            .Select(x => new ReliabilityWorkOrderProjection(x.OpenedAtUtc, x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);

        var completedDurations = faultOrders
            .Where(x => x.CompletedAtUtc is not null && x.CompletedAtUtc > x.OpenedAtUtc)
            .Select(x => (decimal)(x.CompletedAtUtc!.Value - x.OpenedAtUtc).TotalMinutes)
            .ToArray();
        var windowHours = (decimal)(windowEndUtc - windowStartUtc).TotalHours;
        var failureCount = faultOrders.Length;
        var repairCount = completedDurations.Length;
        var mtbfHours = failureCount == 0 ? 0m : windowHours / failureCount;
        var mttrMinutes = repairCount == 0 ? 0m : completedDurations.Sum() / repairCount;

        return new AssetReliabilityResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            windowStartUtc,
            windowEndUtc,
            failureCount,
            repairCount,
            Math.Round(mtbfHours, 6),
            Math.Round(mttrMinutes, 6));
    }
}

internal sealed record ReliabilityWorkOrderProjection(DateTimeOffset OpenedAtUtc, DateTimeOffset? CompletedAtUtc);

public sealed record GetMaintenanceAssetAvailabilityWindowsQuery(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int FreshnessMaxAgeMinutes = 60) : IQuery<EquipmentRuntimeAvailabilityResponse>;

public sealed class GetMaintenanceAssetAvailabilityWindowsQueryValidator : AbstractValidator<GetMaintenanceAssetAvailabilityWindowsQuery>
{
    public GetMaintenanceAssetAvailabilityWindowsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.WindowEndUtc).GreaterThan(x => x.WindowStartUtc);
        RuleFor(x => x.FreshnessMaxAgeMinutes).GreaterThan(0);
    }
}

public sealed class GetMaintenanceAssetAvailabilityWindowsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetMaintenanceAssetAvailabilityWindowsQuery, EquipmentRuntimeAvailabilityResponse>
{
    public async Task<EquipmentRuntimeAvailabilityResponse> Handle(GetMaintenanceAssetAvailabilityWindowsQuery request, CancellationToken cancellationToken)
    {
        var query = new QueryMaintenanceAvailabilityWindowsQuery(new EquipmentRuntimeAvailabilityRequest(
            request.OrganizationId,
            request.EnvironmentId,
            request.WindowStartUtc,
            request.WindowEndUtc,
            [request.DeviceAssetId],
            null,
            request.FreshnessMaxAgeMinutes));
        return await new QueryMaintenanceAvailabilityWindowsQueryHandler(dbContext).Handle(query, cancellationToken);
    }
}

public sealed record QueryMaintenanceAvailabilityWindowsQuery(EquipmentRuntimeAvailabilityRequest Request) : IQuery<EquipmentRuntimeAvailabilityResponse>;

public sealed class QueryMaintenanceAvailabilityWindowsQueryValidator : AbstractValidator<QueryMaintenanceAvailabilityWindowsQuery>
{
    public QueryMaintenanceAvailabilityWindowsQueryValidator()
    {
        RuleFor(x => x.Request.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.WindowEndUtc).GreaterThan(x => x.Request.WindowStartUtc);
        RuleFor(x => x.Request.FreshnessMaxAgeMinutes).GreaterThan(0);
    }
}

public sealed class QueryMaintenanceAvailabilityWindowsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<QueryMaintenanceAvailabilityWindowsQuery, EquipmentRuntimeAvailabilityResponse>
{
    private static readonly string[] InspectionRequiredResults = ["failed", "fail", "blocked", "not-ok", "not ok", "nok"];

    public async Task<EquipmentRuntimeAvailabilityResponse> Handle(QueryMaintenanceAvailabilityWindowsQuery request, CancellationToken cancellationToken)
    {
        var originalContract = request.Request;
        if (originalContract.WindowEndUtc <= originalContract.WindowStartUtc)
        {
            throw new KnownException("Maintenance availability window end must be after start.");
        }

        var contract = originalContract with
        {
            WindowStartUtc = originalContract.WindowStartUtc.ToUniversalTime(),
            WindowEndUtc = originalContract.WindowEndUtc.ToUniversalTime(),
        };

        var workCenterIds = Normalize(contract.WorkCenterIds);
        if (workCenterIds.Length > 0)
        {
            throw new KnownException("Maintenance availability windows require explicit device asset ids; work center resolution is not available in P0.");
        }

        var deviceAssetIds = Normalize(contract.DeviceAssetIds);
        if (deviceAssetIds.Length == 0)
        {
            throw new KnownException("deviceAssetIds is required in P0 maintenance availability.");
        }

        var workOrders = await dbContext.MaintenanceWorkOrders
            .Where(x => x.OrganizationId == contract.OrganizationId)
            .Where(x => x.EnvironmentId == contract.EnvironmentId)
            .Where(x => deviceAssetIds.Contains(x.DeviceAssetId))
            .Where(x => x.AssetUnavailable)
            .Where(x => x.AssetUnavailableFromUtc != null)
            .Where(x => x.Status == MaintenanceWorkOrderStatus.Open || x.CompletedAtUtc != null)
            .Where(x => x.AssetUnavailableFromUtc < contract.WindowEndUtc)
            .Where(x => x.Status == MaintenanceWorkOrderStatus.Open || x.CompletedAtUtc > contract.WindowStartUtc)
            .OrderBy(x => x.AssetUnavailableFromUtc)
            .Select(x => new MaintenanceWorkOrderAvailabilityProjection(
                x.Id,
                x.DeviceAssetId,
                x.SourceAlarmId,
                x.Status,
                x.AssetUnavailableFromUtc!.Value,
                x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);

        var plans = await dbContext.MaintenancePlans
            .Where(x => x.OrganizationId == contract.OrganizationId)
            .Where(x => x.EnvironmentId == contract.EnvironmentId)
            .Where(x => deviceAssetIds.Contains(x.DeviceAssetId))
            .Where(x => x.WindowStartUtc != null && x.WindowEndUtc != null)
            .Where(x => x.WindowStartUtc < contract.WindowEndUtc && x.WindowEndUtc > contract.WindowStartUtc)
            .OrderBy(x => x.WindowStartUtc)
            .Select(x => new MaintenancePlanAvailabilityProjection(x.Id, x.DeviceAssetId, x.PlanCode, x.WindowStartUtc!.Value, x.WindowEndUtc!.Value))
            .ToArrayAsync(cancellationToken);

        var inspectionPlans = await dbContext.MaintenancePlans
            .Where(x => x.OrganizationId == contract.OrganizationId)
            .Where(x => x.EnvironmentId == contract.EnvironmentId)
            .Where(x => deviceAssetIds.Contains(x.DeviceAssetId))
            .Select(x => new MaintenanceInspectionPlanProjection(x.Id, x.DeviceAssetId))
            .ToArrayAsync(cancellationToken);
        var inspectionWorkOrders = await dbContext.MaintenanceWorkOrders
            .Where(x => x.OrganizationId == contract.OrganizationId)
            .Where(x => x.EnvironmentId == contract.EnvironmentId)
            .Where(x => deviceAssetIds.Contains(x.DeviceAssetId))
            .Select(x => new MaintenanceInspectionWorkOrderProjection(x.Id, x.DeviceAssetId))
            .ToArrayAsync(cancellationToken);
        var inspectionPlanIds = inspectionPlans.Select(x => x.PlanId).ToArray();
        var inspectionWorkOrderIds = inspectionWorkOrders.Select(x => x.WorkOrderId).ToArray();

        var inspections = await dbContext.MaintenanceInspections
            .Where(x => x.OrganizationId == contract.OrganizationId)
            .Where(x => x.EnvironmentId == contract.EnvironmentId)
            .Where(x => x.InspectedAtUtc < contract.WindowEndUtc)
            .Where(x => x.PlanId != null && inspectionPlanIds.Contains(x.PlanId) || x.WorkOrderId != null && inspectionWorkOrderIds.Contains(x.WorkOrderId))
            .OrderBy(x => x.InspectedAtUtc)
            .Select(x => new MaintenanceInspectionAvailabilityProjection(x.Id, x.PlanId, x.WorkOrderId, x.Result, x.InspectedAtUtc))
            .ToArrayAsync(cancellationToken);

        var windows = new List<EquipmentRuntimeAvailabilityWindowContract>();
        foreach (var workOrder in workOrders)
        {
            var endUtc = workOrder.Status == MaintenanceWorkOrderStatus.Open
                ? contract.WindowEndUtc
                : workOrder.CompletedAtUtc!.Value;
            AddWindow(
                windows,
                workOrder.DeviceAssetId,
                workOrder.SourceAlarmId is null ? EquipmentRuntimeReasonCodes.Downtime : EquipmentRuntimeReasonCodes.ActiveAlarm,
                workOrder.SourceAlarmId is null ? EquipmentRuntimeSeverity.Blocked : EquipmentRuntimeSeverity.Critical,
                workOrder.AssetUnavailableFromUtc,
                endUtc,
                workOrder.SourceAlarmId is null ? EquipmentRuntimeSourceType.Downtime : EquipmentRuntimeSourceType.Alarm,
                workOrder.SourceAlarmId ?? workOrder.WorkOrderId.ToString(),
                contract);
        }

        foreach (var plan in plans)
        {
            AddWindow(
                windows,
                plan.DeviceAssetId,
                EquipmentRuntimeReasonCodes.MaintenanceWindow,
                EquipmentRuntimeSeverity.Warning,
                plan.WindowStartUtc,
                plan.WindowEndUtc,
                EquipmentRuntimeSourceType.MaintenanceWindow,
                plan.PlanCode,
                contract);
        }

        var latestInspections = inspections
            .GroupBy(GetInspectionReferenceKey, StringComparer.Ordinal)
            .Select(x => x.OrderByDescending(inspection => inspection.InspectedAtUtc).First())
            .ToArray();
        foreach (var inspection in latestInspections.Where(x => IsInspectionRequired(x.Result)))
        {
            var deviceAssetId = ResolveInspectionDeviceAssetId(inspection, inspectionPlans, inspectionWorkOrders);
            if (deviceAssetId is null)
            {
                continue;
            }

            AddWindow(
                windows,
                deviceAssetId,
                EquipmentRuntimeReasonCodes.InspectionRequired,
                EquipmentRuntimeSeverity.Blocked,
                inspection.InspectedAtUtc,
                contract.WindowEndUtc,
                EquipmentRuntimeSourceType.Inspection,
                inspection.InspectionId.ToString(),
                contract);
        }

        return new EquipmentRuntimeAvailabilityResponse(
            ContractVersion: 1,
            OrganizationId: contract.OrganizationId,
            EnvironmentId: contract.EnvironmentId,
            QueryWindowStartUtc: contract.WindowStartUtc,
            QueryWindowEndUtc: contract.WindowEndUtc,
            Items: windows
                .OrderBy(x => x.DeviceAssetId)
                .ThenBy(x => x.StartUtc)
                .ThenBy(x => x.ReasonCode)
                .ToArray());
    }

    private static string[] Normalize(IReadOnlyCollection<string>? values)
    {
        return values?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static void AddWindow(
        List<EquipmentRuntimeAvailabilityWindowContract> windows,
        string deviceAssetId,
        string reasonCode,
        EquipmentRuntimeSeverity severity,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        EquipmentRuntimeSourceType sourceType,
        string sourceReferenceId,
        EquipmentRuntimeAvailabilityRequest request)
    {
        var clippedStartUtc = Max(startUtc, request.WindowStartUtc);
        var clippedEndUtc = Min(endUtc, request.WindowEndUtc);
        if (clippedEndUtc <= clippedStartUtc)
        {
            return;
        }

        windows.Add(new EquipmentRuntimeAvailabilityWindowContract(
            DeviceAssetId: deviceAssetId,
            WorkCenterId: null,
            AvailabilityStatus: EquipmentRuntimeAvailabilityStatus.Unavailable,
            ReasonCode: reasonCode,
            Severity: severity,
            StartUtc: clippedStartUtc,
            EndUtc: clippedEndUtc,
            SourceType: sourceType,
            SourceReferenceId: sourceReferenceId,
            MessageKey: reasonCode,
            SubstituteDeviceAssetIds: []));
    }

    private static string? ResolveInspectionDeviceAssetId(
        MaintenanceInspectionAvailabilityProjection inspection,
        IReadOnlyCollection<MaintenanceInspectionPlanProjection> plans,
        IReadOnlyCollection<MaintenanceInspectionWorkOrderProjection> workOrders)
    {
        if (inspection.PlanId is not null)
        {
            return plans.FirstOrDefault(x => x.PlanId == inspection.PlanId)?.DeviceAssetId;
        }

        return inspection.WorkOrderId is null
            ? null
            : workOrders.FirstOrDefault(x => x.WorkOrderId == inspection.WorkOrderId)?.DeviceAssetId;
    }

    private static string GetInspectionReferenceKey(MaintenanceInspectionAvailabilityProjection inspection)
    {
        return inspection.PlanId is not null
            ? $"plan:{inspection.PlanId}"
            : $"workOrder:{inspection.WorkOrderId}";
    }

    private static bool IsInspectionRequired(string result)
    {
        return InspectionRequiredResults.Contains(result.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right)
    {
        return left > right ? left : right;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left < right ? left : right;
    }
}

internal sealed record MaintenanceWorkOrderAvailabilityProjection(
    MaintenanceWorkOrderId WorkOrderId,
    string DeviceAssetId,
    string? SourceAlarmId,
    MaintenanceWorkOrderStatus Status,
    DateTimeOffset AssetUnavailableFromUtc,
    DateTimeOffset? CompletedAtUtc);

internal sealed record MaintenancePlanAvailabilityProjection(MaintenancePlanId PlanId, string DeviceAssetId, string PlanCode, DateTimeOffset WindowStartUtc, DateTimeOffset WindowEndUtc);

internal sealed record MaintenanceInspectionPlanProjection(MaintenancePlanId PlanId, string DeviceAssetId);

internal sealed record MaintenanceInspectionWorkOrderProjection(MaintenanceWorkOrderId WorkOrderId, string DeviceAssetId);

internal sealed record MaintenanceInspectionAvailabilityProjection(
    MaintenanceInspectionId InspectionId,
    MaintenancePlanId? PlanId,
    MaintenanceWorkOrderId? WorkOrderId,
    string Result,
    DateTimeOffset InspectedAtUtc);
