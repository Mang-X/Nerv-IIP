using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public sealed class BusinessGatewayDataScopeFilter(
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
{
    private const int MaxRequestedDeviceReferences = 200;
    private const int MaxForwardedDeviceAliases = MaxRequestedDeviceReferences * 2;
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

    public async Task<MaintenanceWorkOrderDataScopeProjection> ApplyToMaintenanceWorkOrdersAsync(
        BusinessConsoleMaintenanceWorkOrderListRequest request,
        AuthorizationDataScope? dataScope,
        CancellationToken cancellationToken)
    {
        if (dataScope?.DenyAll == true)
        {
            return MaintenanceWorkOrderDataScopeProjection.Deny(request);
        }

        var requested = DeviceReferenceGroups(request);
        var hasRestrictedScope = dataScope is { HasRestrictions: true };
        if (!requested.Specified && !hasRestrictedScope)
        {
            return MaintenanceWorkOrderDataScopeProjection.Allow(request);
        }

        if (requested.Specified && requested.Groups.Count == 0)
        {
            return MaintenanceWorkOrderDataScopeProjection.Deny(request);
        }

        VerifiedDeviceSelection? allowedDevices = null;
        IReadOnlyDictionary<string, VerifiedDeviceIdentity>? verifiedByReference;
        if (hasRestrictedScope)
        {
            var resolved = await ResolveAsync(
                request.OrganizationId,
                request.EnvironmentId,
                dataScope!,
                cancellationToken);
            verifiedByReference = await ResolveDeviceReferencesAsync(
                request.OrganizationId,
                request.EnvironmentId,
                resolved.DeviceAssets.Select(device => device.DeviceAssetId),
                cancellationToken);
            allowedDevices = ResolveAllowedDevices(resolved.DeviceAssets, verifiedByReference);
            if (allowedDevices is null)
            {
                return MaintenanceWorkOrderDataScopeProjection.Deny(request);
            }
        }
        else
        {
            verifiedByReference = await ResolveDeviceReferencesAsync(
                request.OrganizationId,
                request.EnvironmentId,
                requested.Groups.SelectMany(group => group),
                cancellationToken);
        }

        var requestedDevices = requested.Specified
            ? ResolveRequestedDevices(
                requested,
                hasRestrictedScope
                    ? AliasIndex(verifiedByReference)
                    : verifiedByReference,
                requireAllReferences: !hasRestrictedScope)
            : null;
        if (requested.Specified && requestedDevices is null)
        {
            return MaintenanceWorkOrderDataScopeProjection.Deny(request);
        }

        var selectedIdentities = requestedDevices?.Identities.ToHashSet()
            ?? allowedDevices!.Identities.ToHashSet();
        if (allowedDevices is not null)
        {
            selectedIdentities.IntersectWith(allowedDevices.Identities);
        }
        if (selectedIdentities.Count == 0)
        {
            return MaintenanceWorkOrderDataScopeProjection.Deny(request);
        }

        var aliasesByIdentity = requestedDevices?.AliasesByIdentity ?? allowedDevices!.AliasesByIdentity;
        var aliases = selectedIdentities
            .SelectMany(identity => aliasesByIdentity[identity])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (aliases.Length == 0 || aliases.Length > MaxForwardedDeviceAliases)
        {
            return MaintenanceWorkOrderDataScopeProjection.Deny(request);
        }

        return MaintenanceWorkOrderDataScopeProjection.Allow(request with
        {
            DeviceAssetIds = null,
            DeviceAssetId = null,
            DeviceAssetReferences = aliases,
        });
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

    private static VerifiedDeviceSelection? ResolveRequestedDevices(
        DeviceReferenceFilter requested,
        IReadOnlyDictionary<string, VerifiedDeviceIdentity>? byReference,
        bool requireAllReferences)
    {
        var references = requested.Groups
            .SelectMany(group => group)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (references.Length == 0 || references.Length > MaxRequestedDeviceReferences)
        {
            return null;
        }

        if (byReference is null
            || (requireAllReferences && references.Any(reference => !byReference.ContainsKey(reference))))
        {
            return null;
        }

        var intersection = requested.Groups[0]
            .Where(byReference.ContainsKey)
            .Select(reference => byReference[reference].Identity)
            .ToHashSet();
        foreach (var group in requested.Groups.Skip(1))
        {
            intersection.IntersectWith(group
                .Where(byReference.ContainsKey)
                .Select(reference => byReference[reference].Identity));
        }
        if (intersection.Count == 0)
        {
            return null;
        }

        return new VerifiedDeviceSelection(
            intersection,
            byReference.Values
                .GroupBy(device => device.Identity)
                .ToDictionary(group => group.Key, group => group.First().Aliases));
    }

    private static VerifiedDeviceSelection? ResolveAllowedDevices(
        IReadOnlyCollection<DeviceIdentity> allowedDevices,
        IReadOnlyDictionary<string, VerifiedDeviceIdentity>? byReference)
    {
        if (allowedDevices.Count == 0 || allowedDevices.Count > MaxRequestedDeviceReferences)
        {
            return null;
        }

        if (byReference is null
            || allowedDevices.Any(device => !byReference.ContainsKey(device.DeviceAssetId)))
        {
            return null;
        }

        var verified = new List<VerifiedDeviceIdentity>(allowedDevices.Count);
        foreach (var allowed in allowedDevices)
        {
            var resolved = byReference[allowed.DeviceAssetId];
            if (!string.Equals(resolved.Identity.DeviceAssetId, allowed.DeviceAssetId, StringComparison.Ordinal)
                || !string.Equals(resolved.Identity.SnapshotVersion, allowed.SnapshotVersion, StringComparison.Ordinal)
                || !string.Equals(resolved.Code, allowed.Code, StringComparison.Ordinal))
            {
                return null;
            }
            verified.Add(resolved);
        }

        return new VerifiedDeviceSelection(
            verified.Select(device => device.Identity).ToHashSet(),
            verified
                .GroupBy(device => device.Identity)
                .ToDictionary(group => group.Key, group => group.First().Aliases));
    }

    private async Task<IReadOnlyDictionary<string, VerifiedDeviceIdentity>?> ResolveDeviceReferencesAsync(
        string organizationId,
        string environmentId,
        IEnumerable<string> references,
        CancellationToken cancellationToken)
    {
        var requested = references
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requested.Length == 0 || requested.Length > MaxRequestedDeviceReferences)
        {
            return null;
        }

        try
        {
            var response = await masterData.ResolveReferencesAsync(
                tokenProvider.BearerToken,
                new BusinessMasterDataResolveReferencesRequest(
                    organizationId,
                    environmentId,
                    requested.Select(reference => new BusinessMasterDataReferenceRequest(
                        "device-asset",
                        reference)).ToArray()),
                cancellationToken);
            if (response.References is null
                || response.References.Count != requested.Length
                || response.References.Count > MaxRequestedDeviceReferences)
            {
                return null;
            }

            var mapped = new Dictionary<string, VerifiedDeviceIdentity>(StringComparer.Ordinal);
            foreach (var item in response.References)
            {
                var reference = item.Code?.Trim();
                var deviceAssetId = NormalizeDeviceAssetId(item.DeviceAssetId);
                var canonicalCode = item.CanonicalCode?.Trim();
                if (string.IsNullOrWhiteSpace(reference)
                    || !requested.Contains(reference, StringComparer.Ordinal)
                    || mapped.ContainsKey(reference)
                    || !string.Equals(item.ResourceType, "device-asset", StringComparison.Ordinal)
                    || !item.Exists
                    || !item.Active
                    || !string.Equals(item.OrganizationId, organizationId, StringComparison.Ordinal)
                    || !string.Equals(item.EnvironmentId, environmentId, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(item.SnapshotVersion)
                    || string.IsNullOrWhiteSpace(canonicalCode)
                    || deviceAssetId is null)
                {
                    return null;
                }
                mapped.Add(
                    reference,
                    new VerifiedDeviceIdentity(
                        new DeviceSnapshotIdentity(deviceAssetId, item.SnapshotVersion.Trim()),
                        canonicalCode,
                        new[] { deviceAssetId, canonicalCode }
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal)
                            .ToArray()));
            }

            if (mapped.Count != requested.Length || !AliasesAreUnique(mapped.Values))
            {
                return null;
            }
            return mapped;
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

    private static IReadOnlyDictionary<string, VerifiedDeviceIdentity>? AliasIndex(
        IReadOnlyDictionary<string, VerifiedDeviceIdentity>? byReference)
    {
        if (byReference is null)
        {
            return null;
        }

        var byAlias = new Dictionary<string, VerifiedDeviceIdentity>(StringComparer.Ordinal);
        foreach (var device in byReference.Values.DistinctBy(item => item.Identity))
        {
            foreach (var alias in device.Aliases)
            {
                if (!byAlias.TryAdd(alias, device))
                {
                    return null;
                }
            }
        }
        return byAlias;
    }

    private static bool AliasesAreUnique(IEnumerable<VerifiedDeviceIdentity> devices)
    {
        var uniqueDevices = devices.DistinctBy(device => device.Identity).ToArray();
        var aliases = new Dictionary<string, DeviceSnapshotIdentity>(StringComparer.Ordinal);
        var ids = uniqueDevices.ToDictionary(device => device.Identity.DeviceAssetId, device => device.Identity, StringComparer.Ordinal);
        foreach (var device in uniqueDevices)
        {
            foreach (var alias in device.Aliases)
            {
                if (aliases.TryGetValue(alias, out var existing) && existing != device.Identity)
                {
                    return false;
                }
                aliases[alias] = device.Identity;
            }
            var codeAsId = NormalizeDeviceAssetId(device.Code);
            if (codeAsId is not null
                && ids.TryGetValue(codeAsId, out var identityById)
                && identityById != device.Identity)
            {
                return false;
            }
        }
        return true;
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

    private sealed record VerifiedDeviceIdentity(
        DeviceSnapshotIdentity Identity,
        string Code,
        IReadOnlyCollection<string> Aliases);

    private sealed record VerifiedDeviceSelection(
        IReadOnlyCollection<DeviceSnapshotIdentity> Identities,
        IReadOnlyDictionary<DeviceSnapshotIdentity, IReadOnlyCollection<string>> AliasesByIdentity);

    private sealed record DeviceReferenceFilter(bool Specified, IReadOnlyList<HashSet<string>> Groups);

}

public sealed record MaintenanceWorkOrderDataScopeProjection(
    BusinessConsoleMaintenanceWorkOrderListRequest Request,
    bool DenyAll)
{
    public static MaintenanceWorkOrderDataScopeProjection Allow(BusinessConsoleMaintenanceWorkOrderListRequest request) =>
        new(request, false);

    public static MaintenanceWorkOrderDataScopeProjection Deny(BusinessConsoleMaintenanceWorkOrderListRequest request) =>
        new(request, true);
}
