using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public sealed record AssembleSchedulingProblemRequest(
    string ProblemId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset HorizonStartUtc,
    DateTimeOffset HorizonEndUtc,
    IReadOnlyCollection<SchedulingProblemSourceOrder> Orders,
    IReadOnlyCollection<SchedulingQualityBlockContract>? QualityBlocks = null,
    IReadOnlyCollection<SchedulingLockedAssignmentContract>? LockedAssignments = null);

public sealed record SchedulingProblemSourceOrder(
    string OrderId,
    string SkuCode,
    decimal Quantity,
    DateTimeOffset DueUtc,
    int Priority,
    bool IsRush,
    DateTimeOffset EarliestStartUtc,
    string RoutingVersionId,
    IReadOnlyCollection<SchedulingProblemOperationConstraint>? OperationConstraints = null);

public sealed record SchedulingProblemOperationConstraint(
    string OperationCode,
    IReadOnlyCollection<string>? RequiredSkillCodes = null,
    IReadOnlyCollection<string>? RequiredToolingIds = null);

public interface ISchedulingProblemProducer
{
    Task<SchedulingProblemContract> AssembleAsync(
        AssembleSchedulingProblemRequest request,
        CancellationToken cancellationToken);
}

public sealed class SchedulingProblemProducer(
    ISchedulingProblemProductEngineeringClient productEngineering,
    ISchedulingProblemMasterDataClient masterData) : ISchedulingProblemProducer
{
    public async Task<SchedulingProblemContract> AssembleAsync(
        AssembleSchedulingProblemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var routingTasks = request.Orders
            .Select(x => x.RoutingVersionId)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                x => x,
                x => productEngineering.GetRoutingAsync(request.OrganizationId, request.EnvironmentId, x, cancellationToken),
                StringComparer.Ordinal);
        await Task.WhenAll(routingTasks.Values);

        var routingsByVersion = routingTasks.ToDictionary(x => x.Key, x => x.Value.Result, StringComparer.Ordinal);
        var workCenterCodes = routingsByVersion.Values
            .SelectMany(x => x.Operations)
            .Select(x => x.WorkCenterCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var workCenters = await LoadWorkCentersAsync(request, workCenterCodes, cancellationToken);
        var calendars = await LoadCalendarsAsync(request, workCenters.Values, cancellationToken);
        var devicesByWorkCenter = await LoadDeviceAssetsAsync(request, workCenterCodes, cancellationToken);
        var operationCapabilitiesByWorkCenter = routingsByVersion.Values
            .SelectMany(x => x.Operations)
            .GroupBy(x => x.WorkCenterCode, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyCollection<string>)x.Select(y => y.OperationCode).ToArray(),
                StringComparer.Ordinal);
        var resources = BuildResources(workCenters.Values, devicesByWorkCenter, operationCapabilitiesByWorkCenter);
        var orderedOrders = request.Orders.OrderBy(x => x.DueUtc).ThenBy(x => x.Priority).ThenBy(x => x.OrderId, StringComparer.Ordinal).ToArray();
        var transitions = BuildTransitions(orderedOrders, routingsByVersion);
        var toolingFacts = await masterData.ResolveToolingFactsAsync(request.OrganizationId, request.EnvironmentId, transitions, cancellationToken);
        var toolingFactsByOperation = toolingFacts.ToDictionary(x => x.OperationId, StringComparer.Ordinal);

        return new SchedulingProblemContract(
            ContractVersion: 1,
            ProblemId: request.ProblemId,
            OrganizationId: request.OrganizationId,
            EnvironmentId: request.EnvironmentId,
            HorizonStartUtc: request.HorizonStartUtc,
            HorizonEndUtc: request.HorizonEndUtc,
            Orders: orderedOrders
                .Select(order => ToOrder(order, routingsByVersion[order.RoutingVersionId], resources, toolingFactsByOperation))
                .ToArray(),
            Resources: resources.Values
                .OrderBy(x => x.SortKey, StringComparer.Ordinal)
                .ThenBy(x => x.ResourceId, StringComparer.Ordinal)
                .ToArray(),
            Calendars: calendars.Values
                .OrderBy(x => x.CalendarId, StringComparer.Ordinal)
                .ToArray(),
            UnavailabilityWindows: [],
            MaterialReadiness: [],
            QualityBlocks: request.QualityBlocks ?? [],
            LockedAssignments: request.LockedAssignments ?? []);
    }

    private static SchedulingOrderContract ToOrder(
        SchedulingProblemSourceOrder order,
        SchedulingProblemRoutingSnapshot routing,
        IReadOnlyDictionary<string, SchedulingResourceContract> resources,
        IReadOnlyDictionary<string, SchedulingProblemToolingFactSnapshot> toolingFacts)
    {
        var constraints = (order.OperationConstraints ?? [])
            .GroupBy(x => x.OperationCode, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var previousOperationIds = new List<string>();
        var operations = new List<SchedulingOperationContract>();
        foreach (var operation in routing.Operations.OrderBy(x => x.Sequence))
        {
            var operationId = $"{order.OrderId}-{operation.Sequence}-{operation.OperationCode}";
            var eligibleResources = resources.Values
                .Where(x => string.Equals(x.WorkCenterId, operation.WorkCenterCode, StringComparison.Ordinal))
                .Select(x => x.ResourceId)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var constraint = constraints.GetValueOrDefault(operation.OperationCode);
            var toolingFact = toolingFacts.GetValueOrDefault(operationId);
            operations.Add(new SchedulingOperationContract(
                OperationId: operationId,
                OperationSequence: operation.Sequence,
                PredecessorOperationIds: previousOperationIds.ToArray(),
                DurationMinutes: CalculateDurationMinutes(operation, order.Quantity),
                RequiredCapabilityCode: operation.OperationCode,
                EligibleResourceIds: eligibleResources,
                PrimaryResourceId: eligibleResources.FirstOrDefault(),
                EarliestStartUtc: order.EarliestStartUtc,
                DueUtc: order.DueUtc,
                Priority: order.Priority,
                IsRush: order.IsRush,
                SplitPolicy: ScheduleSplitPolicyContract.NonSplittable,
                MaterialReadyUtc: null,
                QualityBlockReason: operation.RequiresQualityInspection ? "quality.inspectionRequired" : null,
                SourceReference: $"product-engineering:routing:{routing.RoutingCode}:{routing.Revision}:{operation.OperationCode}",
                SetupMinutes: toolingFact is null || toolingFact.SetupMinutes == 0 ? operation.SetupMinutes : toolingFact.SetupMinutes,
                RequiredSkillCodes: NormalizeCodes(constraint?.RequiredSkillCodes),
                RequiredToolingIds: NormalizeCodes(toolingFact?.RequiredToolingCodes),
                ToolingAvailable: toolingFact?.ToolingAvailable ?? true));
            previousOperationIds.Clear();
            previousOperationIds.Add(operationId);
        }

        return new SchedulingOrderContract(
            order.OrderId,
            order.SkuCode,
            order.Quantity,
            order.DueUtc,
            order.Priority,
            order.IsRush,
            operations);
    }

    private static IReadOnlyCollection<SchedulingProblemToolingTransitionSnapshot> BuildTransitions(
        IReadOnlyCollection<SchedulingProblemSourceOrder> orders,
        IReadOnlyDictionary<string, SchedulingProblemRoutingSnapshot> routings)
    {
        var previousSkuByWorkCenter = new Dictionary<string, string>(StringComparer.Ordinal);
        var transitions = new List<SchedulingProblemToolingTransitionSnapshot>();
        foreach (var order in orders)
        {
            foreach (var operation in routings[order.RoutingVersionId].Operations.OrderBy(x => x.Sequence))
            {
                var operationId = $"{order.OrderId}-{operation.Sequence}-{operation.OperationCode}";
                var fromSku = previousSkuByWorkCenter.GetValueOrDefault(operation.WorkCenterCode) ?? order.SkuCode;
                transitions.Add(new SchedulingProblemToolingTransitionSnapshot(operationId, operation.WorkCenterCode, fromSku, null, order.SkuCode));
                previousSkuByWorkCenter[operation.WorkCenterCode] = order.SkuCode;
            }
        }
        return transitions;
    }

    private async Task<Dictionary<string, SchedulingProblemWorkCenterSnapshot>> LoadWorkCentersAsync(
        AssembleSchedulingProblemRequest request,
        IReadOnlyCollection<string> workCenterCodes,
        CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(workCenterCodes.Select(code =>
            masterData.GetWorkCenterAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken)));
        return snapshots.ToDictionary(x => x.Code, StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, SchedulingCalendarContract>> LoadCalendarsAsync(
        AssembleSchedulingProblemRequest request,
        IEnumerable<SchedulingProblemWorkCenterSnapshot> workCenters,
        CancellationToken cancellationToken)
    {
        var calendarCodes = workCenters
            .Select(x => x.DefaultCalendarCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var snapshots = await Task.WhenAll(calendarCodes.Select(code =>
            masterData.GetCalendarAsync(request.OrganizationId, request.EnvironmentId, code, request.HorizonStartUtc, request.HorizonEndUtc, cancellationToken)));
        return snapshots.ToDictionary(
            x => x.Code,
            x => new SchedulingCalendarContract(x.Code, x.Shifts
                .OrderBy(y => y.StartUtc)
                .ThenBy(y => y.EndUtc)
                .ThenBy(y => y.ReasonCode, StringComparer.Ordinal)
                .Select(y => new SchedulingTimeWindowContract(y.StartUtc, y.EndUtc, y.ReasonCode))
                .ToArray()),
            StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>>> LoadDeviceAssetsAsync(
        AssembleSchedulingProblemRequest request,
        IReadOnlyCollection<string> workCenterCodes,
        CancellationToken cancellationToken)
    {
        var devices = await Task.WhenAll(workCenterCodes.Select(async code => new
        {
            WorkCenterCode = code,
            Devices = await masterData.ListDeviceAssetsAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken)
        }));
        return devices.ToDictionary(x => x.WorkCenterCode, x => x.Devices, StringComparer.Ordinal);
    }

    private static Dictionary<string, SchedulingResourceContract> BuildResources(
        IEnumerable<SchedulingProblemWorkCenterSnapshot> workCenters,
        IReadOnlyDictionary<string, IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>> devicesByWorkCenter,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> operationCapabilitiesByWorkCenter)
    {
        var resources = new Dictionary<string, SchedulingResourceContract>(StringComparer.Ordinal);
        foreach (var workCenter in workCenters.OrderBy(x => x.Code, StringComparer.Ordinal))
        {
            var devices = devicesByWorkCenter.GetValueOrDefault(workCenter.Code) ?? [];
            var resourceIds = devices.Count == 0
                ? [workCenter.Code]
                : devices.Select(x => x.ResourceId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var capacityUnits = devices.Count == 0
                ? Math.Max(1, workCenter.NumberOfCapacities)
                : 1;
            foreach (var resourceId in resourceIds)
            {
                resources[resourceId] = new SchedulingResourceContract(
                    ResourceId: resourceId,
                    WorkCenterId: workCenter.Code,
                    CapabilityCodes: NormalizeCodes(workCenter.CapabilityCodes
                        .Concat(operationCapabilitiesByWorkCenter.GetValueOrDefault(workCenter.Code) ?? [])
                        .Concat([workCenter.Code])),
                    CapacityUnits: capacityUnits,
                    CalendarId: workCenter.DefaultCalendarCode,
                    SortKey: $"{workCenter.Code}:{resourceId}");
            }
        }

        return resources;
    }

    private static int CalculateDurationMinutes(SchedulingProblemRoutingOperationSnapshot operation, decimal quantity)
    {
        var runMinutes = Math.Max(0, operation.RunMinutes);
        var effectiveQuantity = Math.Max(0m, quantity);
        var totalRunMinutes = (int)Math.Ceiling(runMinutes * effectiveQuantity);
        return Math.Max(1, totalRunMinutes + Math.Max(0, operation.TeardownMinutes));
    }

    private static IReadOnlyCollection<string> NormalizeCodes(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}

public interface ISchedulingProblemProductEngineeringClient
{
    Task<SchedulingProblemRoutingSnapshot> GetRoutingAsync(
        string organizationId,
        string environmentId,
        string routingVersionId,
        CancellationToken cancellationToken);
}

public interface ISchedulingProblemMasterDataClient
{
    Task<SchedulingProblemWorkCenterSnapshot> GetWorkCenterAsync(
        string organizationId,
        string environmentId,
        string workCenterCode,
        CancellationToken cancellationToken);

    Task<SchedulingProblemCalendarSnapshot> GetCalendarAsync(
        string organizationId,
        string environmentId,
        string calendarCode,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>> ListDeviceAssetsAsync(
        string organizationId,
        string environmentId,
        string workCenterCode,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>> ResolveToolingFactsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<SchedulingProblemToolingTransitionSnapshot> transitions,
        CancellationToken cancellationToken);
}

public sealed record SchedulingProblemRoutingSnapshot(
    string RoutingCode,
    string Revision,
    string SkuCode,
    IReadOnlyCollection<SchedulingProblemRoutingOperationSnapshot> Operations);

public sealed record SchedulingProblemRoutingOperationSnapshot(
    int Sequence,
    string WorkCenterCode,
    string OperationCode,
    string OperationName,
    int SetupMinutes,
    int RunMinutes,
    int TeardownMinutes,
    bool RequiresQualityInspection = false);

public sealed record SchedulingProblemWorkCenterSnapshot(
    string Code,
    string DefaultCalendarCode,
    int NumberOfCapacities,
    IReadOnlyCollection<string> CapabilityCodes);

public sealed record SchedulingProblemCalendarSnapshot(
    string Code,
    IReadOnlyCollection<SchedulingProblemShiftWindowSnapshot> Shifts);

public sealed record SchedulingProblemShiftWindowSnapshot(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string ReasonCode);

public sealed record SchedulingProblemDeviceAssetSnapshot(string ResourceId, string WorkCenterCode);
public sealed record SchedulingProblemToolingTransitionSnapshot(string OperationId, string WorkCenterCode, string FromSkuCode, string? FromProductCategoryCode, string ToSkuCode);
public sealed record SchedulingProblemToolingFactSnapshot(string OperationId, int SetupMinutes, IReadOnlyCollection<string> RequiredToolingCodes, bool ToolingAvailable = true);

public sealed class HttpSchedulingProblemProductEngineeringClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider? internalTokenProvider = null) : ISchedulingProblemProductEngineeringClient
{
    public async Task<SchedulingProblemRoutingSnapshot> GetRoutingAsync(
        string organizationId,
        string environmentId,
        string routingVersionId,
        CancellationToken cancellationToken)
    {
        var (routingCode, revision) = ParseVersionId(routingVersionId);
        var response = await SendAsync<ProductEngineeringRoutingResponse>(
            "/api/business/v1/engineering/routings/" +
            $"{Uri.EscapeDataString(routingCode)}/{Uri.EscapeDataString(revision)}?" +
            SchedulingProblemHttp.Query(("organizationId", organizationId), ("environmentId", environmentId)),
            cancellationToken);
        return new SchedulingProblemRoutingSnapshot(
            response.RoutingCode,
            response.Revision,
            response.SkuCode,
            response.Operations.Select(x => new SchedulingProblemRoutingOperationSnapshot(
                x.Sequence,
                x.WorkCenterCode,
                x.OperationCode,
                x.OperationName,
                x.SetupMinutes,
                Math.Max(1, x.RunMinutes == 0 ? x.StandardMinutes - x.SetupMinutes - x.TeardownMinutes : x.RunMinutes),
                Math.Max(0, x.TeardownMinutes),
                x.RequiresQualityInspection)).ToArray());
    }

    private async Task<T> SendAsync<T>(string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var bearerToken = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(SchedulingJson.Options, cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException("ProductEngineering returned an empty response envelope.");
    }

    private static (string Code, string Revision) ParseVersionId(string versionId)
    {
        var parts = versionId.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new KnownException("RoutingVersionId must use 'routingCode:revision' format.");
        }

        return (parts[0], parts[1]);
    }

    private sealed record ProductEngineeringRoutingResponse(
        string RoutingCode,
        string Revision,
        string SkuCode,
        string Status,
        DateOnly? EffectiveDate,
        IReadOnlyCollection<ProductEngineeringRoutingOperationResponse> Operations);

    private sealed record ProductEngineeringRoutingOperationResponse(
        int Sequence,
        string WorkCenterCode,
        string OperationCode,
        string OperationName,
        int StandardMinutes,
        int SetupMinutes,
        int RunMinutes,
        int TeardownMinutes,
        string ControlKey,
        bool RequiresReporting,
        bool RequiresQualityInspection,
        bool IsOutsourced);
}

public sealed class HttpSchedulingProblemMasterDataClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider? internalTokenProvider = null) : ISchedulingProblemMasterDataClient
{
    private readonly object shiftDetailsLock = new();
    private readonly Dictionary<(string OrganizationId, string EnvironmentId), Task<IReadOnlyCollection<MasterDataResourceDetailResponse>>> shiftDetailsTasks = new();

    public async Task<SchedulingProblemWorkCenterSnapshot> GetWorkCenterAsync(
        string organizationId,
        string environmentId,
        string workCenterCode,
        CancellationToken cancellationToken)
    {
        var detail = await GetResourceDetailAsync(
            organizationId,
            environmentId,
            "work-center",
            workCenterCode,
            cancellationToken);
        return new SchedulingProblemWorkCenterSnapshot(
            detail.Code,
            detail.DefaultCalendarCode ?? throw new KnownException($"Work center '{workCenterCode}' does not have a default calendar."),
            Math.Max(1, detail.NumberOfCapacities ?? 1),
            [detail.Code]);
    }

    public async Task<SchedulingProblemCalendarSnapshot> GetCalendarAsync(
        string organizationId,
        string environmentId,
        string calendarCode,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc,
        CancellationToken cancellationToken)
    {
        var calendar = await GetResourceDetailAsync(organizationId, environmentId, "work-calendar", calendarCode, cancellationToken);
        var shifts = await GetShiftDetailsAsync(organizationId, environmentId, cancellationToken);
        var windows = BuildShiftWindows(calendar, shifts, horizonStartUtc, horizonEndUtc);
        return new SchedulingProblemCalendarSnapshot(calendar.Code, windows);
    }

    public async Task<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>> ListDeviceAssetsAsync(
        string organizationId,
        string environmentId,
        string workCenterCode,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<MasterDataResourceListResponse>(
            "/api/business/v1/master-data/resources?" + SchedulingProblemHttp.Query(
                ("organizationId", organizationId),
                ("environmentId", environmentId),
                ("resourceType", "device-asset"),
                ("workCenterCode", workCenterCode),
                ("all", true)),
            cancellationToken);
        if (response.Truncated)
        {
            throw new KnownException($"MasterData device asset list for work center '{workCenterCode}' was truncated.");
        }

        return response.Resources
            .Where(x => x.Active)
            .Where(x => string.Equals(x.WorkCenterCode, workCenterCode, StringComparison.Ordinal))
            .Select(x => new SchedulingProblemDeviceAssetSnapshot(
                string.IsNullOrWhiteSpace(x.DeviceAssetId) ? x.Code : x.DeviceAssetId!,
                workCenterCode))
            .OrderBy(x => x.ResourceId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>> ResolveToolingFactsAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<SchedulingProblemToolingTransitionSnapshot> transitions,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<ResolveSchedulingToolingFactsResponse>(
            "/api/business/v1/master-data/scheduling-tooling-facts/resolve",
            cancellationToken,
            new ResolveSchedulingToolingFactsRequest(organizationId, environmentId, transitions));
        return response.Facts;
    }

    private Task<IReadOnlyCollection<MasterDataResourceDetailResponse>> GetShiftDetailsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var key = (organizationId, environmentId);
        lock (shiftDetailsLock)
        {
            if (!shiftDetailsTasks.TryGetValue(key, out var task))
            {
                task = ListShiftDetailsAsync(organizationId, environmentId, cancellationToken);
                shiftDetailsTasks.Add(key, task);
            }

            return task;
        }
    }

    private async Task<IReadOnlyCollection<MasterDataResourceDetailResponse>> ListShiftDetailsAsync(
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<MasterDataResourceListResponse>(
            "/api/business/v1/master-data/resources?" + SchedulingProblemHttp.Query(
                ("organizationId", organizationId),
                ("environmentId", environmentId),
                ("resourceType", "shift"),
                ("all", true)),
            cancellationToken);
        if (response.Truncated)
        {
            throw new KnownException("MasterData shift list was truncated.");
        }

        var shifts = await Task.WhenAll(response.Resources
            .Where(x => x.Active)
            .Select(x => GetResourceDetailAsync(organizationId, environmentId, "shift", x.Code, cancellationToken)));
        return shifts;
    }

    private async Task<MasterDataResourceDetailResponse> GetResourceDetailAsync(
        string organizationId,
        string environmentId,
        string resourceType,
        string code,
        CancellationToken cancellationToken)
    {
        return await SendAsync<MasterDataResourceDetailResponse>(
            "/api/business/v1/master-data/resources/" +
            $"{Uri.EscapeDataString(resourceType)}/{Uri.EscapeDataString(code)}?" +
            SchedulingProblemHttp.Query(("organizationId", organizationId), ("environmentId", environmentId)),
            cancellationToken);
    }

    private async Task<T> SendAsync<T>(string requestUri, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var bearerToken = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(SchedulingJson.Options, cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException("MasterData returned an empty response envelope.");
    }

    private async Task<T> SendAsync<T>(string requestUri, CancellationToken cancellationToken, object body)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(body, options: SchedulingJson.Options)
        };
        var bearerToken = internalTokenProvider?.BearerToken;
        if (!string.IsNullOrWhiteSpace(bearerToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(SchedulingJson.Options, cancellationToken);
        return envelope?.Data ?? throw new InvalidOperationException("MasterData returned an empty response envelope.");
    }

    private static IReadOnlyCollection<SchedulingProblemShiftWindowSnapshot> BuildShiftWindows(
        MasterDataResourceDetailResponse calendar,
        IReadOnlyCollection<MasterDataResourceDetailResponse> shifts,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc)
    {
        var workingDays = (calendar.WorkingTimes ?? [])
            .Select(x => x.DayOfWeek)
            .ToHashSet();
        var holidays = (calendar.Holidays ?? [])
            .Select(x => x.Date)
            .ToHashSet();
        var exceptions = (calendar.Exceptions ?? [])
            .ToDictionary(x => x.Date);
        var windows = new List<SchedulingProblemShiftWindowSnapshot>();
        for (var day = DateOnly.FromDateTime(horizonStartUtc.UtcDateTime.Date); day <= DateOnly.FromDateTime(horizonEndUtc.UtcDateTime.Date); day = day.AddDays(1))
        {
            var hasException = exceptions.TryGetValue(day, out var exception);
            var isWorkingDay = hasException
                ? exception!.IsWorkingDay
                : workingDays.Contains(day.DayOfWeek) && !holidays.Contains(day);
            if (!isWorkingDay)
            {
                continue;
            }

            if (hasException && exception!.StartsAt.HasValue && exception.EndsAt.HasValue)
            {
                AddWindow(windows, day, exception.StartsAt.Value, exception.EndsAt.Value, "calendar-exception", horizonStartUtc, horizonEndUtc);
                continue;
            }

            // Current MasterData calendars own working-day markers; shift definitions are global resources.
            foreach (var shift in shifts)
            {
                if (shift.StartsAt.HasValue && shift.EndsAt.HasValue)
                {
                    AddWindow(windows, day, shift.StartsAt.Value, shift.EndsAt.Value, shift.Code, horizonStartUtc, horizonEndUtc);
                }
            }
        }

        return windows
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.EndUtc)
            .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddWindow(
        ICollection<SchedulingProblemShiftWindowSnapshot> windows,
        DateOnly day,
        TimeOnly startsAt,
        TimeOnly endsAt,
        string reasonCode,
        DateTimeOffset horizonStartUtc,
        DateTimeOffset horizonEndUtc)
    {
        var start = new DateTimeOffset(day.ToDateTime(startsAt), TimeSpan.Zero);
        var rawEndDay = endsAt <= startsAt ? day.AddDays(1) : day;
        var end = new DateTimeOffset(rawEndDay.ToDateTime(endsAt), TimeSpan.Zero);

        var clippedStart = start < horizonStartUtc ? horizonStartUtc : start;
        var clippedEnd = end > horizonEndUtc ? horizonEndUtc : end;
        if (clippedEnd > clippedStart)
        {
            windows.Add(new SchedulingProblemShiftWindowSnapshot(clippedStart, clippedEnd, reasonCode));
        }
    }

    private sealed record MasterDataResourceListResponse(
        IReadOnlyCollection<MasterDataResourceListItem> Resources,
        int Total,
        bool Truncated = false,
        int? Limit = null);

    private sealed record MasterDataResourceListItem(
        string ResourceType,
        string Code,
        string DisplayName,
        bool Active,
        string SnapshotVersion,
        string? WorkCenterCode = null,
        string? DeviceAssetId = null);

    private sealed record MasterDataResourceDetailResponse(
        string ResourceType,
        string Code,
        string DisplayName,
        bool Active,
        string SnapshotVersion,
        string OrganizationId,
        string EnvironmentId,
        TimeOnly? StartsAt = null,
        TimeOnly? EndsAt = null,
        int? PaidMinutes = null,
        string? DefaultCalendarCode = null,
        IReadOnlyCollection<WorkCalendarWorkingTimeResponse>? WorkingTimes = null,
        IReadOnlyCollection<WorkCalendarHolidayResponse>? Holidays = null,
        IReadOnlyCollection<WorkCalendarExceptionResponse>? Exceptions = null,
        int? NumberOfCapacities = null);

    private sealed record WorkCalendarWorkingTimeResponse(DayOfWeek DayOfWeek);
    private sealed record WorkCalendarHolidayResponse(DateOnly Date, string Name);
    private sealed record WorkCalendarExceptionResponse(DateOnly Date, bool IsWorkingDay, TimeOnly? StartsAt, TimeOnly? EndsAt, string? Reason);
    private sealed record ResolveSchedulingToolingFactsRequest(string OrganizationId, string EnvironmentId, IReadOnlyCollection<SchedulingProblemToolingTransitionSnapshot> Transitions);
    private sealed record ResolveSchedulingToolingFactsResponse(IReadOnlyCollection<SchedulingProblemToolingFactSnapshot> Facts);
}

internal sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

internal static class SchedulingProblemHttp
{
    public static string Query(params (string Name, object? Value)[] values)
    {
        var pairs = values
            .Where(x => x.Value is not null && !string.IsNullOrWhiteSpace(Convert.ToString(x.Value, CultureInfo.InvariantCulture)))
            .Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(FormatValue(x.Value!))}");
        return string.Join('&', pairs);
    }

    private static string FormatValue(object value) => value switch
    {
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
