using System.Net;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;
using BusinessOeeAggregateDimension = Nerv.IIP.Contracts.IndustrialTelemetry.OeeAggregateDimension;
using BusinessOeeAggregateRequest = Nerv.IIP.Contracts.IndustrialTelemetry.QueryOeeAggregateBucketsRequest;
using BusinessOeeAggregateResponse = Nerv.IIP.Contracts.IndustrialTelemetry.OeeAggregateBucketsResponse;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessOeeAggregateCapability
{
    Task<BusinessOeeAggregateResponse> QueryAsync(
        BusinessGatewayAuthorizationResult authorization,
        BusinessOeeAggregateRequest request,
        CancellationToken cancellationToken);
}

public sealed class BusinessOeeAggregateCapability(
    IBusinessMasterDataClient masterData,
    IBusinessIndustrialTelemetryClient industrialTelemetry,
    IInternalServiceTokenProvider tokenProvider) : IBusinessOeeAggregateCapability
{
    private const int MasterDataPageSize = 500;
    private static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(31);

    public async Task<BusinessOeeAggregateResponse> QueryAsync(
        BusinessGatewayAuthorizationResult authorization,
        BusinessOeeAggregateRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        if (!authorization.IsAllowed
            || string.IsNullOrWhiteSpace(authorization.PrincipalId)
            || authorization.DataScope?.DenyAll == true
            || !string.Equals(
                authorization.AuthorizedOrganizationId,
                request.OrganizationId,
                StringComparison.Ordinal)
            || !string.Equals(
                authorization.AuthorizedEnvironmentId,
                request.EnvironmentId,
                StringComparison.Ordinal))
        {
            throw Forbidden();
        }

        var grants = PrincipalWorkContextAuthorizationResolver.TrustedGrantsForPermission(
            authorization,
            BusinessGatewayPermissions.IiotTelemetryRead);
        if (grants.Length == 0)
        {
            throw Forbidden();
        }
        var organizationWide = grants.Any(grant =>
            grant.OrganizationWide
            && Kind(grant) == "organization"
            && string.Equals(grant.ScopeId, request.OrganizationId, StringComparison.Ordinal));

        if (!organizationWide && !grants.Any(grant => Kind(grant) is "site" or "workshop" or "production-line" or "work-center"))
        {
            throw Forbidden();
        }

        // MasterData validates explicit identities and their requested hierarchy only.
        // Historical visibility is decided by Telemetry from fact-time ownership.
        var selection = await ResolveSelectionAsync(request, cancellationToken);
        var narrowed = new Nerv.IIP.Contracts.IndustrialTelemetry.QueryInternalOeeAggregateBucketsRequest(
            request.OrganizationId, request.EnvironmentId, request.Dimension,
            request.WindowStartUtc, request.WindowEndUtc,
            selection.DeviceAssetId, request.WorkCenterId, request.ShiftCode,
            request.LineCode, request.WorkshopCode, request.BusinessDate, request.Skip, request.Take,
            organizationWide ? null : ScopeIds("site"),
            organizationWide ? null : ScopeIds("workshop"),
            organizationWide ? null : ScopeIds("production-line"),
            organizationWide ? null : ScopeIds("work-center"));

        var response = await industrialTelemetry.QueryOeeAggregatesAsync(
            tokenProvider.BearerToken, narrowed, cancellationToken);
        if (!organizationWide && selection.HasSpatialFilter && response.TotalCount == 0)
        {
            throw Forbidden();
        }
        return response;

        string ScopeIds(string kind) => string.Join(',', grants
            .Where(grant => Kind(grant) == kind)
            .Select(grant => grant.ScopeId).Distinct(StringComparer.Ordinal));
    }

    private async Task<SpatialSelection> ResolveSelectionAsync(
        BusinessOeeAggregateRequest request,
        CancellationToken cancellationToken)
    {
        var device = request.DeviceAssetId is null
            ? null
            : await FindExactAsync(request, "device-asset", request.DeviceAssetId, cancellationToken);
        var workCenter = request.WorkCenterId is null
            ? null
            : await FindExactAsync(request, "work-center", request.WorkCenterId, cancellationToken);
        var line = request.LineCode is null
            ? null
            : await FindExactAsync(request, "production-line", request.LineCode, cancellationToken);
        var workshop = request.WorkshopCode is null
            ? null
            : await FindExactAsync(request, "workshop", request.WorkshopCode, cancellationToken);
        if (request.ShiftCode is not null)
        {
            _ = await FindExactAsync(request, "shift", request.ShiftCode, cancellationToken);
        }

        var selectedWorkCenter = device?.WorkCenterCode ?? workCenter?.Code;
        var selectedLine = device?.LineCode ?? workCenter?.LineCode ?? line?.Code;
        var selectedWorkshop = device?.WorkshopCode ?? workCenter?.WorkshopCode ?? line?.WorkshopCode ?? workshop?.Code;
        var selectedSite = device?.SiteCode ?? workCenter?.PlantCode ?? line?.SiteCode ?? workshop?.SiteCode;
        EnsureConsistent(request.WorkCenterId, selectedWorkCenter);
        EnsureConsistent(request.LineCode, selectedLine);
        EnsureConsistent(request.WorkshopCode, selectedWorkshop);
        EnsureConsistent(device?.LineCode, workCenter?.LineCode);
        EnsureConsistent(device?.WorkshopCode, workCenter?.WorkshopCode);
        EnsureConsistent(workCenter?.LineCode, line?.Code);
        EnsureConsistent(workCenter?.WorkshopCode, line?.WorkshopCode);
        EnsureConsistent(line?.WorkshopCode, workshop?.Code);

        return new SpatialSelection(
            selectedSite,
            selectedWorkshop,
            selectedLine,
            selectedWorkCenter,
            device?.DeviceAssetId);
    }

    private async Task<BusinessConsoleResourceItem> FindExactAsync(
        BusinessOeeAggregateRequest request,
        string resourceType,
        string reference,
        CancellationToken cancellationToken)
    {
        var resources = await ListResourcesAsync(
            request.OrganizationId,
            request.EnvironmentId,
            resourceType,
            resourceType == "device-asset" ? reference : null,
            resourceType == "shift" ? reference : null,
            cancellationToken);
        var matches = resources
            .Where(resource =>
                string.Equals(resource.Code, reference, StringComparison.Ordinal)
                    || resourceType == "device-asset"
                    && string.Equals(resource.DeviceAssetId, reference, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            throw Forbidden();
        }
        if (matches.Length != 1 || resourceType == "device-asset" && string.IsNullOrWhiteSpace(matches[0].DeviceAssetId))
        {
            throw InvalidDownstream();
        }

        return matches[0];
    }

    private async Task<IReadOnlyCollection<BusinessConsoleResourceItem>> ListResourcesAsync(
        string organizationId,
        string environmentId,
        string resourceType,
        string? deviceAssetId,
        string? shiftCode,
        CancellationToken cancellationToken)
    {
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
                    Skip: skip,
                    Take: MasterDataPageSize,
                    All: true,
                    ShiftCode: shiftCode,
                    DeviceAssetId: deviceAssetId),
                cancellationToken);
            if (response.Resources.Any(resource =>
                    resource is null
                    || !string.Equals(resource.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase)
                    || !resource.Active
                    || string.IsNullOrWhiteSpace(resource.Code)
                    || resource.Code != resource.Code.Trim()
                    || string.IsNullOrWhiteSpace(resource.SnapshotVersion))
                || response.Total < response.Resources.Count
                || response.Resources.Count == 0 && response.Total > skip)
            {
                throw InvalidDownstream();
            }

            resources.AddRange(response.Resources);
            skip += response.Resources.Count;
            if (skip >= response.Total)
            {
                return resources;
            }
        }
    }

    private static string Kind(AuthorizationScopeGrant grant) => grant.ScopeKind.Trim().ToLowerInvariant();

    private static void ValidateRequest(BusinessOeeAggregateRequest request)
    {
        if (!CanonicalRequired(request.OrganizationId, 100)
            || !CanonicalRequired(request.EnvironmentId, 100)
            || request.WindowEndUtc <= request.WindowStartUtc
            || request.WindowEndUtc - request.WindowStartUtc > MaximumWindow
            || request.Skip < 0
            || request.Take is < 1 or > 100
            || !CanonicalOptional(request.DeviceAssetId, 150)
            || !CanonicalOptional(request.WorkCenterId, 100)
            || !CanonicalOptional(request.ShiftCode, 100)
            || !CanonicalOptional(request.LineCode, 100)
            || !CanonicalOptional(request.WorkshopCode, 100))
        {
            throw InvalidRequest();
        }
    }

    private static bool CanonicalRequired(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && value == value.Trim();

    private static bool CanonicalOptional(string? value, int maximumLength) =>
        value is null || !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && value == value.Trim();

    private static void EnsureConsistent(string? left, string? right)
    {
        if (left is not null && right is not null && !string.Equals(left, right, StringComparison.Ordinal))
        {
            throw Forbidden();
        }
    }

    private static BusinessServiceProxyException InvalidRequest() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadRequest, "oee-aggregate-request-invalid");

    private static BusinessServiceProxyException Forbidden() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.Forbidden, "oee-scope-not-authorized");

    private static BusinessServiceProxyException InvalidDownstream() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(HttpStatusCode.BadGateway, "downstream-invalid-response");

    private sealed record SpatialSelection(
        string? SiteCode,
        string? WorkshopCode,
        string? LineCode,
        string? WorkCenterCode,
        string? DeviceAssetId)
    {
        public bool HasSpatialFilter =>
            DeviceAssetId is not null || WorkCenterCode is not null || LineCode is not null || WorkshopCode is not null;
    }
}
