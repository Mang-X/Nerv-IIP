using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceInspectionAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.ServiceAuth;

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

public sealed record ListDowntimeReasonsQuery(string? OrganizationId, string? EnvironmentId, int Skip = 0, int Take = 100) : IQuery<PagedMaintenanceListResponse<DowntimeReasonListItem>>;

public sealed record DowntimeReasonListItem(DowntimeReasonId DowntimeReasonId, string OrganizationId, string EnvironmentId, string ReasonCode, string Description, string ReasonCategory, string LossCategory);

public sealed class ListDowntimeReasonsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListDowntimeReasonsQuery, PagedMaintenanceListResponse<DowntimeReasonListItem>>
{
    public async Task<PagedMaintenanceListResponse<DowntimeReasonListItem>> Handle(ListDowntimeReasonsQuery request, CancellationToken cancellationToken)
    {
        var skip = ListMaintenanceWorkOrdersQueryHandler.NormalizeSkip(request.Skip);
        var take = ListMaintenanceWorkOrdersQueryHandler.NormalizeTake(request.Take);
        var query = dbContext.DowntimeReasons
            .Where(x => request.OrganizationId == null || x.OrganizationId == request.OrganizationId)
            .Where(x => request.EnvironmentId == null || x.EnvironmentId == request.EnvironmentId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.ReasonCode)
            .Select(x => new DowntimeReasonListItem(x.Id, x.OrganizationId, x.EnvironmentId, x.ReasonCode, x.Description, x.ReasonCategory, x.LossCategory))
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        return new PagedMaintenanceListResponse<DowntimeReasonListItem>(items, skip, take, total);
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
    decimal? MtbfHours,
    decimal? MttrMinutes,
    string MtbfRuntimeSource,
    bool MtbfRuntimeHasSamples);

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

public sealed class QueryAssetReliabilityQueryHandler : IQueryHandler<QueryAssetReliabilityQuery, AssetReliabilityResponse>
{
    private readonly ApplicationDbContext dbContext;
    private readonly IAssetRuntimeHoursProvider runtimeHoursProvider;

    public QueryAssetReliabilityQueryHandler(ApplicationDbContext dbContext, IAssetRuntimeHoursProvider runtimeHoursProvider)
    {
        this.dbContext = dbContext;
        this.runtimeHoursProvider = runtimeHoursProvider;
    }

    public async Task<AssetReliabilityResponse> Handle(QueryAssetReliabilityQuery request, CancellationToken cancellationToken)
    {
        var windowStartUtc = request.WindowStartUtc.ToUniversalTime();
        var windowEndUtc = request.WindowEndUtc.ToUniversalTime();
        var faultOrders = await dbContext.MaintenanceWorkOrders
            .Where(x => x.OrganizationId == request.OrganizationId)
            .Where(x => x.EnvironmentId == request.EnvironmentId)
            .Where(x => x.DeviceAssetId == request.DeviceAssetId)
            .Where(x => x.SourceAlarmId != null || x.SourceType == MaintenanceWorkOrderSourceTypes.Inspection || x.FailureModeCode != null || x.DowntimeReasonCode != null)
            .Where(x => x.OpenedAtUtc >= windowStartUtc && x.OpenedAtUtc < windowEndUtc)
            .Select(x => new ReliabilityWorkOrderProjection(x.OpenedAtUtc, x.RepairStartedAtUtc, x.CompletedAtUtc))
            .ToArrayAsync(cancellationToken);
        var runtimeHours = await runtimeHoursProvider.CalculateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            windowStartUtc,
            windowEndUtc,
            cancellationToken);

        var completedDurations = faultOrders
            .Where(x => x.CompletedAtUtc is not null && x.CompletedAtUtc > x.OpenedAtUtc)
            .Select(x => (decimal)(x.CompletedAtUtc!.Value - (x.RepairStartedAtUtc ?? x.OpenedAtUtc)).TotalMinutes)
            .Where(x => x > 0)
            .ToArray();
        var failureCount = faultOrders.Length;
        var repairCount = completedDurations.Length;
        var mtbfHours = failureCount == 0 ? null : (decimal?)(runtimeHours.RuntimeHours / failureCount);
        var mttrMinutes = repairCount == 0 ? null : (decimal?)(completedDurations.Sum() / repairCount);

        return new AssetReliabilityResponse(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            windowStartUtc,
            windowEndUtc,
            failureCount,
            repairCount,
            mtbfHours.HasValue ? Math.Round(mtbfHours.Value, 6) : null,
            mttrMinutes.HasValue ? Math.Round(mttrMinutes.Value, 6) : null,
            runtimeHours.RuntimeSource,
            runtimeHours.HasRuntimeSamples);
    }
}

internal sealed record ReliabilityWorkOrderProjection(DateTimeOffset OpenedAtUtc, DateTimeOffset? RepairStartedAtUtc, DateTimeOffset? CompletedAtUtc);

public static class AssetRuntimeSources
{
    public const string Oee = "oee";
    public const string Fallback = "fallback";
}

public sealed record AssetRuntimeHoursResult(decimal RuntimeHours, string RuntimeSource, bool HasRuntimeSamples);

public interface IAssetRuntimeHoursProvider
{
    Task<AssetRuntimeHoursResult> CalculateAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken);
}

public interface IAssetRuntimeHoursFallbackProvider
{
    Task<AssetRuntimeHoursResult> CalculateFallbackAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken);
}

public sealed class MaintenanceUnavailableWindowRuntimeHoursProvider(ISender sender) : IAssetRuntimeHoursProvider, IAssetRuntimeHoursFallbackProvider
{
    public async Task<AssetRuntimeHoursResult> CalculateAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        return await CalculateFallbackAsync(organizationId, environmentId, deviceAssetId, windowStartUtc, windowEndUtc, cancellationToken);
    }

    public async Task<AssetRuntimeHoursResult> CalculateFallbackAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        var windowHours = (decimal)(windowEndUtc - windowStartUtc).TotalHours;
        if (windowHours <= 0)
        {
            return new AssetRuntimeHoursResult(0m, AssetRuntimeSources.Fallback, HasRuntimeSamples: false);
        }

        var availability = await sender.Send(
            new QueryMaintenanceAvailabilityWindowsQuery(new EquipmentRuntimeAvailabilityRequest(
                organizationId,
                environmentId,
                windowStartUtc,
                windowEndUtc,
                [deviceAssetId],
                null)),
            cancellationToken);

        var unavailableHours = CalculateUnavailableHours(availability.Items, deviceAssetId, windowStartUtc, windowEndUtc);
        return new AssetRuntimeHoursResult(Math.Max(0m, windowHours - unavailableHours), AssetRuntimeSources.Fallback, HasRuntimeSamples: false);
    }

    private static decimal CalculateUnavailableHours(
        IReadOnlyCollection<EquipmentRuntimeAvailabilityWindowContract> windows,
        string deviceAssetId,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        var intervals = windows
            .Where(x => string.Equals(x.DeviceAssetId, deviceAssetId, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.AvailabilityStatus == EquipmentRuntimeAvailabilityStatus.Unavailable)
            .Select(x => new RuntimeInterval(Max(x.StartUtc, windowStartUtc), Min(x.EndUtc, windowEndUtc)))
            .Where(x => x.EndUtc > x.StartUtc)
            .OrderBy(x => x.StartUtc)
            .ToArray();
        if (intervals.Length == 0)
        {
            return 0m;
        }

        var totalTicks = 0L;
        var currentStart = intervals[0].StartUtc;
        var currentEnd = intervals[0].EndUtc;
        foreach (var interval in intervals.Skip(1))
        {
            if (interval.StartUtc <= currentEnd)
            {
                currentEnd = Max(currentEnd, interval.EndUtc);
                continue;
            }

            totalTicks += currentEnd.UtcTicks - currentStart.UtcTicks;
            currentStart = interval.StartUtc;
            currentEnd = interval.EndUtc;
        }

        totalTicks += currentEnd.UtcTicks - currentStart.UtcTicks;
        return (decimal)TimeSpan.FromTicks(totalTicks).TotalHours;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    private sealed record RuntimeInterval(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
}

public sealed class HttpIndustrialTelemetryAssetRuntimeHoursProvider(
    IHttpClientFactory httpClientFactory,
    IInternalServiceTokenProvider? tokenProvider,
    IAssetRuntimeHoursFallbackProvider fallbackProvider,
    ILogger<HttpIndustrialTelemetryAssetRuntimeHoursProvider> logger) : IAssetRuntimeHoursProvider
{
    public const string ClientName = "MaintenanceIndustrialTelemetryRuntime";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AssetRuntimeHoursResult> CalculateAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildRuntimeHoursPath(organizationId, environmentId, deviceAssetId, windowStartUtc, windowEndUtc));
            var token = tokenProvider?.BearerToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var client = httpClientFactory.CreateClient(ClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<IndustrialTelemetryRuntimeHoursResponse>>(JsonOptions, cancellationToken);
            var data = envelope?.Data;
            if (data is null || data.StateSampleCount == 0 || data.HasRuntimeSamples == false)
            {
                return await CalculateFallbackAsync();
            }

            var runtimeHours = data.TotalRuntimeHours ?? CalculateLegacyRuntimeHours(data, windowStartUtc, windowEndUtc);
            return new AssetRuntimeHoursResult(Math.Round(runtimeHours, 6), AssetRuntimeSources.Oee, HasRuntimeSamples: true);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "IndustrialTelemetry runtime-hours source was unavailable for {OrganizationId}/{EnvironmentId}/{DeviceAssetId}; falling back to Maintenance availability windows.", organizationId, environmentId, deviceAssetId);
            return await CalculateFallbackAsync();
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "IndustrialTelemetry runtime-hours source timed out for {OrganizationId}/{EnvironmentId}/{DeviceAssetId}; falling back to Maintenance availability windows.", organizationId, environmentId, deviceAssetId);
            return await CalculateFallbackAsync();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "IndustrialTelemetry runtime-hours source returned an invalid response for {RequestUri}; falling back to Maintenance availability windows.", BuildRuntimeHoursPath(organizationId, environmentId, deviceAssetId, windowStartUtc, windowEndUtc));
            return await CalculateFallbackAsync();
        }

        Task<AssetRuntimeHoursResult> CalculateFallbackAsync() =>
            fallbackProvider.CalculateFallbackAsync(organizationId, environmentId, deviceAssetId, windowStartUtc, windowEndUtc, cancellationToken);
    }

    private static decimal CalculateLegacyRuntimeHours(
        IndustrialTelemetryRuntimeHoursResponse data,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        var windowHours = (decimal)(windowEndUtc - windowStartUtc).TotalHours;
        var loadingRate = data.LoadingRate ?? 1m;
        return windowHours * loadingRate * data.AvailabilityRate;
    }

    private static string BuildRuntimeHoursPath(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc)
    {
        return "/api/business/v1/iiot/runtime-hours?" + string.Join('&',
            Query("organizationId", organizationId),
            Query("environmentId", environmentId),
            Query("deviceAssetId", deviceAssetId),
            Query("windowStartUtc", windowStartUtc.ToString("O", CultureInfo.InvariantCulture)),
            Query("windowEndUtc", windowEndUtc.ToString("O", CultureInfo.InvariantCulture)));
    }

    private static string Query(string name, string value) => $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private sealed record IndustrialTelemetryRuntimeHoursResponse(
        string OrganizationId,
        string EnvironmentId,
        string DeviceAssetId,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        int StateSampleCount,
        decimal? TotalRuntimeHours,
        decimal? TotalLoadingHours,
        bool? HasRuntimeSamples,
        decimal AvailabilityRate,
        decimal? LoadingRate,
        decimal PerformanceRate,
        decimal QualityRate,
        decimal OeeRate,
        bool PerformanceRateEstimated,
        bool QualityRateEstimated);
}

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
        return await MaintenanceAvailabilityWindowCalculator.CalculateAsync(
            dbContext,
            new EquipmentRuntimeAvailabilityRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.WindowStartUtc,
                request.WindowEndUtc,
                [request.DeviceAssetId],
                null,
                request.FreshnessMaxAgeMinutes),
            cancellationToken);
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
    public Task<EquipmentRuntimeAvailabilityResponse> Handle(QueryMaintenanceAvailabilityWindowsQuery request, CancellationToken cancellationToken)
    {
        return MaintenanceAvailabilityWindowCalculator.CalculateAsync(dbContext, request.Request, cancellationToken);
    }
}

internal static class MaintenanceAvailabilityWindowCalculator
{
    public static async Task<EquipmentRuntimeAvailabilityResponse> CalculateAsync(
        ApplicationDbContext dbContext,
        EquipmentRuntimeAvailabilityRequest originalContract,
        CancellationToken cancellationToken)
    {
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
                x.AlarmClearedAtUtc,
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
                ? workOrder.AlarmClearedAtUtc ?? contract.WindowEndUtc
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
        return MaintenanceInspectionResults.IsFailed(result);
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
    DateTimeOffset? AlarmClearedAtUtc,
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
