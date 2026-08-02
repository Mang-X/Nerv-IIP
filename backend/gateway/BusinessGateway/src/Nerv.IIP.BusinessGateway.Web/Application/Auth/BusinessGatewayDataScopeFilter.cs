using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public sealed class BusinessGatewayDataScopeFilter(
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
{
    private const string NoScopeMatch = "__iam_scope_no_match__";

    public async Task<BusinessConsoleTelemetryAlarmListRequest> ApplyToTelemetryAlarmsAsync(
        BusinessConsoleTelemetryAlarmListRequest request,
        AuthorizationDataScope? dataScope,
        CancellationToken cancellationToken)
    {
        if (dataScope is not { HasRestrictions: true })
        {
            return request;
        }

        var resolved = await ResolveAsync(request.OrganizationId, request.EnvironmentId, dataScope, cancellationToken);
        return request with { DeviceAssetIds = NarrowSingle(request.DeviceAssetId, resolved.DeviceAssetIds) };
    }

    public async Task<BusinessConsoleEquipmentAlarmListRequest> ApplyToEquipmentAlarmsAsync(
        BusinessConsoleEquipmentAlarmListRequest request,
        AuthorizationDataScope? dataScope,
        CancellationToken cancellationToken)
    {
        if (dataScope is not { HasRestrictions: true })
        {
            return request;
        }

        var resolved = await ResolveAsync(request.OrganizationId, request.EnvironmentId, dataScope, cancellationToken);
        return request with { DeviceAssetIds = NarrowSingle(request.DeviceAssetId, resolved.DeviceAssetIds) };
    }

    public async Task<BusinessConsoleMaintenanceWorkOrderListRequest> ApplyToMaintenanceWorkOrdersAsync(
        BusinessConsoleMaintenanceWorkOrderListRequest request,
        AuthorizationDataScope? dataScope,
        CancellationToken cancellationToken)
    {
        if (dataScope is not { HasRestrictions: true })
        {
            return request;
        }

        var resolved = await ResolveAsync(request.OrganizationId, request.EnvironmentId, dataScope, cancellationToken);
        var allowed = resolved.DeviceAssetReferences.ToHashSet(StringComparer.Ordinal);
        var requested = ExactDeviceReferences(request);
        var narrowed = (!requested.Specified ? allowed : requested.Values.Where(allowed.Contains))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return request with
        {
            DeviceAssetIds = null,
            DeviceAssetId = null,
            DeviceAssetReferences = narrowed.Length == 0 ? [NoScopeMatch] : narrowed,
        };
    }

    private async Task<ResolvedDataScope> ResolveAsync(
        string organizationId,
        string environmentId,
        AuthorizationDataScope dataScope,
        CancellationToken cancellationToken)
    {
        var siteCodes = Normalize(dataScope.SiteCodes);
        var workshopCodes = Normalize(dataScope.WorkshopCodes);
        var explicitLineCodes = Normalize(dataScope.ProductionLineCodes);
        var explicitWorkCenterCodes = Normalize(dataScope.WorkCenterCodes ?? []);
        if (dataScope.DenyAll
            || dataScope.SelfIds?.Count > 0
            || dataScope.TeamCodes?.Count > 0
            || dataScope.OrganizationIds?.Any(x => !string.Equals(x.Trim(), organizationId, StringComparison.Ordinal)) == true)
        {
            return new ResolvedDataScope([], [], []);
        }

        var lines = await ListResourcesAsync(organizationId, environmentId, "production-line", cancellationToken);
        var scopedLineCodes = lines
            .Where(x => explicitLineCodes.Contains(x.Code)
                || Matches(x.SiteCode, siteCodes)
                || Matches(x.WorkshopCode, workshopCodes))
            .Select(x => x.Code)
            .Concat(explicitLineCodes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var lineSet = scopedLineCodes.ToHashSet(StringComparer.Ordinal);

        var workCenters = await ListResourcesAsync(organizationId, environmentId, "work-center", cancellationToken);
        var scopedWorkCenterCodes = workCenters
            .Where(x => explicitWorkCenterCodes.Contains(x.Code)
                || Matches(x.SiteCode, siteCodes)
                || Matches(x.WorkshopCode, workshopCodes)
                || Matches(x.LineCode, lineSet))
            .Select(x => x.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var workCenterSet = scopedWorkCenterCodes.ToHashSet(StringComparer.Ordinal);

        var devices = await ListResourcesAsync(organizationId, environmentId, "device-asset", cancellationToken);
        var scopedDevices = devices
            .Where(x => Matches(x.SiteCode, siteCodes)
                || Matches(x.WorkshopCode, workshopCodes)
                || Matches(x.LineCode, lineSet)
                || Matches(x.WorkCenterCode, workCenterSet))
            .ToArray();
        var scopedDeviceAssetIds = scopedDevices
            .Select(x => string.IsNullOrWhiteSpace(x.DeviceAssetId) ? x.Code : x.DeviceAssetId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var scopedDeviceAssetReferences = scopedDevices
            .SelectMany(x => new[] { x.DeviceAssetId, x.Code })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ResolvedDataScope(scopedWorkCenterCodes, scopedDeviceAssetIds, scopedDeviceAssetReferences);
    }

    private async Task<IReadOnlyCollection<BusinessConsoleResourceItem>> ListResourcesAsync(
        string organizationId,
        string environmentId,
        string resourceType,
        CancellationToken cancellationToken)
    {
        const int PageSize = 500;
        var resources = new List<BusinessConsoleResourceItem>();
        var skip = 0;
        while (true)
        {
            var response = await masterData.ListResourcesAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleListResourcesRequest(
                    organizationId,
                    environmentId,
                    resourceType,
                    IncludeDisabled: false,
                    Skip: skip,
                    Take: PageSize,
                    All: true),
                cancellationToken);
            resources.AddRange(response.Resources);
            skip += response.Resources.Count;
            if (response.Resources.Count < PageSize || skip >= response.Total)
            {
                return resources;
            }
        }
    }

    private static string NarrowSingle(string? requested, IReadOnlyCollection<string> allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return allowedSet.Contains(requested.Trim()) ? requested.Trim() : NoScopeMatch;
        }

        return JoinOrNoMatch(allowed);
    }

    private static string JoinOrNoMatch(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? NoScopeMatch : string.Join(',', values.Order(StringComparer.Ordinal));

    private static DeviceReferenceFilter ExactDeviceReferences(BusinessConsoleMaintenanceWorkOrderListRequest request)
    {
        var groups = new List<HashSet<string>>();
        AddGroup(groups, request.DeviceAssetReferences ?? []);
        if (!string.IsNullOrWhiteSpace(request.DeviceAssetIds))
        {
            AddGroup(
                groups,
                request.DeviceAssetIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        if (!string.IsNullOrWhiteSpace(request.DeviceAssetId))
        {
            AddGroup(groups, [request.DeviceAssetId!]);
        }
        if (groups.Count == 0)
        {
            return new DeviceReferenceFilter(false, []);
        }

        var intersection = groups[0];
        foreach (var group in groups.Skip(1))
        {
            intersection.IntersectWith(group);
        }
        return new DeviceReferenceFilter(true, intersection);
    }

    private static void AddGroup(List<HashSet<string>> groups, IEnumerable<string> values)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.Ordinal);
        if (normalized.Count > 0)
        {
            groups.Add(normalized);
        }
    }

    private static HashSet<string> Normalize(IReadOnlyCollection<string> values) =>
        values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.Ordinal);

    private static bool Matches(string? value, HashSet<string> allowed) =>
        !string.IsNullOrWhiteSpace(value) && allowed.Contains(value.Trim());

    private sealed record ResolvedDataScope(
        IReadOnlyCollection<string> WorkCenterCodes,
        IReadOnlyCollection<string> DeviceAssetIds,
        IReadOnlyCollection<string> DeviceAssetReferences);

    private sealed record DeviceReferenceFilter(bool Specified, HashSet<string> Values);
}
