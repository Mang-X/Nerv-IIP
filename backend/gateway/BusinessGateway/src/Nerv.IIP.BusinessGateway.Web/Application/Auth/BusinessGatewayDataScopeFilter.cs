using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public sealed class BusinessGatewayDataScopeFilter(
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
{
    private const string NoScopeMatch = "__iam_scope_no_match__";

    public async Task<BusinessConsoleMesListRequest> ApplyToMesWorkOrdersAsync(
        BusinessConsoleMesListRequest request,
        AuthorizationDataScope? dataScope,
        CancellationToken cancellationToken)
    {
        if (dataScope is not { HasRestrictions: true })
        {
            return request;
        }

        var resolved = await ResolveAsync(request.OrganizationId, request.EnvironmentId, dataScope, cancellationToken);
        return request with
        {
            WorkCenterIds = NarrowSingle(request.WorkCenterId, resolved.WorkCenterCodes),
            DeviceAssetIds = NarrowSingle(request.DeviceAssetId, resolved.DeviceAssetIds),
        };
    }

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

    public async Task<BusinessConsoleMaintenanceListRequest> ApplyToMaintenanceWorkOrdersAsync(
        BusinessConsoleMaintenanceListRequest request,
        AuthorizationDataScope? dataScope,
        CancellationToken cancellationToken)
    {
        if (dataScope is not { HasRestrictions: true })
        {
            return request;
        }

        var resolved = await ResolveAsync(request.OrganizationId, request.EnvironmentId, dataScope, cancellationToken);
        return request with { DeviceAssetIds = JoinOrNoMatch(resolved.DeviceAssetIds) };
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
            .Where(x => Matches(x.SiteCode, siteCodes)
                || Matches(x.WorkshopCode, workshopCodes)
                || Matches(x.LineCode, lineSet))
            .Select(x => x.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var workCenterSet = scopedWorkCenterCodes.ToHashSet(StringComparer.Ordinal);

        var devices = await ListResourcesAsync(organizationId, environmentId, "device-asset", cancellationToken);
        var scopedDeviceAssetIds = devices
            .Where(x => Matches(x.SiteCode, siteCodes)
                || Matches(x.WorkshopCode, workshopCodes)
                || Matches(x.LineCode, lineSet)
                || Matches(x.WorkCenterCode, workCenterSet))
            .Select(x => string.IsNullOrWhiteSpace(x.DeviceAssetId) ? x.Code : x.DeviceAssetId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ResolvedDataScope(scopedWorkCenterCodes, scopedDeviceAssetIds);
    }

    private async Task<IReadOnlyCollection<BusinessConsoleResourceItem>> ListResourcesAsync(
        string organizationId,
        string environmentId,
        string resourceType,
        CancellationToken cancellationToken)
    {
        var response = await masterData.ListResourcesAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleListResourcesRequest(
                organizationId,
                environmentId,
                resourceType,
                IncludeDisabled: false,
                Take: 500,
                All: true),
            cancellationToken);
        return response.Resources;
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

    private static HashSet<string> Normalize(IReadOnlyCollection<string> values) =>
        values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.Ordinal);

    private static bool Matches(string? value, HashSet<string> allowed) =>
        !string.IsNullOrWhiteSpace(value) && allowed.Contains(value.Trim());

    private sealed record ResolvedDataScope(
        IReadOnlyCollection<string> WorkCenterCodes,
        IReadOnlyCollection<string> DeviceAssetIds);
}
