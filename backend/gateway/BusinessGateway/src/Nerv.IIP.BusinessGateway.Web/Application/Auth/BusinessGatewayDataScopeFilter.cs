using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public sealed class BusinessGatewayDataScopeFilter(
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
{
    private const int MaxRequestedDeviceReferences = 200;
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
        var allowed = resolved.DeviceAssets
            .Select(device => new DeviceSnapshotIdentity(device.DeviceAssetId, device.SnapshotVersion))
            .ToHashSet();
        var requested = DeviceReferenceGroups(request);
        var requestedDevices = await ResolveRequestedDevicesAsync(
            request.OrganizationId,
            request.EnvironmentId,
            requested,
            cancellationToken);
        var narrowed = (!requested.Specified ? allowed : requestedDevices.Where(allowed.Contains))
            .Select(device => device.DeviceAssetId)
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
            return new ResolvedDataScope([], []);
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
        var scopedDeviceAssets = scopedDevices
            .Select(ToDeviceIdentity)
            .Where(x => x is not null)
            .Select(x => x!)
            .GroupBy(x => x.DeviceAssetId, StringComparer.Ordinal)
            .Where(group => group
                .Select(device => (device.Code, device.SnapshotVersion))
                .Distinct()
                .Count() == 1)
            .Select(x => x.First())
            .ToArray();
        var scopedDeviceAssetIds = scopedDeviceAssets
            .Select(x => x.DeviceAssetId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ResolvedDataScope(scopedDeviceAssetIds, scopedDeviceAssets);
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

    private async Task<HashSet<DeviceSnapshotIdentity>> ResolveRequestedDevicesAsync(
        string organizationId,
        string environmentId,
        DeviceReferenceFilter requested,
        CancellationToken cancellationToken)
    {
        if (!requested.Specified)
        {
            return [];
        }

        var references = requested.Groups
            .SelectMany(group => group)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (references.Length == 0 || references.Length > MaxRequestedDeviceReferences)
        {
            return [];
        }

        using var throttle = new SemaphoreSlim(8);
        var tasks = references.Select(async reference =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var device = await ResolveDeviceAsync(
                    organizationId,
                    environmentId,
                    reference,
                    cancellationToken);
                return (Reference: reference, Device: device);
            }
            finally
            {
                throttle.Release();
            }
        });
        var resolved = await Task.WhenAll(tasks);
        if (resolved.Any(x => x.Device is null))
        {
            return [];
        }

        var byReference = resolved.ToDictionary(
            x => x.Reference,
            x => x.Device!,
            StringComparer.Ordinal);
        var intersection = requested.Groups[0]
            .Select(reference => byReference[reference])
            .ToHashSet();
        foreach (var group in requested.Groups.Skip(1))
        {
            intersection.IntersectWith(group.Select(reference => byReference[reference]));
        }
        return intersection;
    }

    private async Task<DeviceSnapshotIdentity?> ResolveDeviceAsync(
        string organizationId,
        string environmentId,
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var detail = await masterData.GetResourceDetailAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleMasterDataResourceRequest(
                    organizationId,
                    environmentId,
                    "device-asset",
                    reference),
                cancellationToken);
            var deviceAssetId = NormalizeDeviceAssetId(detail.DeviceAssetId);
            var requestedDeviceAssetId = NormalizeDeviceAssetId(reference);
            var referenceMatches = string.Equals(detail.Code, reference, StringComparison.Ordinal)
                || (requestedDeviceAssetId is not null
                    && string.Equals(deviceAssetId, requestedDeviceAssetId, StringComparison.Ordinal));
            return string.Equals(detail.ResourceType, "device-asset", StringComparison.Ordinal)
                && string.Equals(detail.OrganizationId, organizationId, StringComparison.Ordinal)
                && string.Equals(detail.EnvironmentId, environmentId, StringComparison.Ordinal)
                && detail.Active
                && !string.IsNullOrWhiteSpace(detail.SnapshotVersion)
                && referenceMatches
                ? new DeviceSnapshotIdentity(deviceAssetId!, detail.SnapshotVersion.Trim())
                : null;
        }
        catch (BusinessServiceProxyException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static DeviceIdentity? ToDeviceIdentity(BusinessConsoleResourceItem resource)
    {
        var deviceAssetId = NormalizeDeviceAssetId(resource.DeviceAssetId);
        return string.Equals(resource.ResourceType, "device-asset", StringComparison.Ordinal)
            && resource.Active
            && !string.IsNullOrWhiteSpace(resource.SnapshotVersion)
            && !string.IsNullOrWhiteSpace(resource.Code)
            && deviceAssetId is not null
                ? new DeviceIdentity(deviceAssetId, resource.Code.Trim(), resource.SnapshotVersion.Trim())
                : null;
    }

    private static string? NormalizeDeviceAssetId(string? value) =>
        Guid.TryParseExact(value?.Trim(), "D", out var parsed) && parsed != Guid.Empty
            ? parsed.ToString("D")
            : null;

    private static DeviceReferenceFilter DeviceReferenceGroups(BusinessConsoleMaintenanceWorkOrderListRequest request)
    {
        var groups = new List<HashSet<string>>();
        var specified = false;
        if (request.DeviceAssetReferences is not null)
        {
            specified = true;
            if (!TryAddExactGroup(groups, request.DeviceAssetReferences))
            {
                return new DeviceReferenceFilter(true, []);
            }
        }
        if (request.DeviceAssetIds is not null)
        {
            specified = true;
            if (!TryAddExactGroup(
                groups,
                request.DeviceAssetIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                return new DeviceReferenceFilter(true, []);
            }
        }
        if (request.DeviceAssetId is not null)
        {
            specified = true;
            if (!TryAddExactGroup(groups, [request.DeviceAssetId]))
            {
                return new DeviceReferenceFilter(true, []);
            }
        }
        return new DeviceReferenceFilter(specified, groups);
    }

    private static bool TryAddExactGroup(List<HashSet<string>> groups, IEnumerable<string> values)
    {
        var supplied = values.ToArray();
        if (supplied.Length == 0 || supplied.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        var normalized = supplied
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.Ordinal);
        groups.Add(normalized);
        return true;
    }

    private static HashSet<string> Normalize(IReadOnlyCollection<string> values) =>
        values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.Ordinal);

    private static bool Matches(string? value, HashSet<string> allowed) =>
        !string.IsNullOrWhiteSpace(value) && allowed.Contains(value.Trim());

    private sealed record ResolvedDataScope(
        IReadOnlyCollection<string> DeviceAssetIds,
        IReadOnlyCollection<DeviceIdentity> DeviceAssets);

    private sealed record DeviceIdentity(string DeviceAssetId, string Code, string SnapshotVersion);

    private sealed record DeviceSnapshotIdentity(string DeviceAssetId, string SnapshotVersion);

    private sealed record DeviceReferenceFilter(bool Specified, IReadOnlyList<HashSet<string>> Groups);
}
