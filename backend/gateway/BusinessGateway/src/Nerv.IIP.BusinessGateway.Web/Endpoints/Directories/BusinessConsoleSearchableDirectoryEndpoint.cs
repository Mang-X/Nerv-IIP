using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Directories;

[Tags("Business Console Directories")]
[HttpGet("/api/business-console/v1/directories/{directoryType}")]
[BusinessGatewayOperationId("listBusinessConsoleSearchableDirectory")]
[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status400BadRequest)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status502BadGateway)]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(ResponseData), StatusCodes.Status503ServiceUnavailable)]
public sealed class BusinessConsoleSearchableDirectoryEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IBusinessInventoryClient inventory,
    IBusinessQualityClient quality,
    IBusinessMaintenanceClient maintenance,
    IInternalServiceTokenProvider tokenProvider)
    : Endpoint<BusinessConsoleSearchableDirectoryRequest, ResponseData<BusinessConsoleSearchableDirectoryResponse>>
{
    public override async Task HandleAsync(BusinessConsoleSearchableDirectoryRequest req, CancellationToken ct)
    {
        var directoryType = (Route<string>("directoryType") ?? req.DirectoryType).Trim().ToLowerInvariant();
        BusinessConsoleSearchableDirectoryDefinition definition;
        try
        {
            definition = BusinessConsoleSearchableDirectoryPolicy.Require(directoryType);
        }
        catch (ArgumentOutOfRangeException)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, StatusCodes.Status400BadRequest, "directory-type-unsupported", ct);
            return;
        }

        var scopeError = BusinessConsoleSearchableDirectoryPolicy.ValidateScope(directoryType, req.ScopeKind, req.ScopeId);
        var rankingError = BusinessConsoleSearchableDirectoryPolicy.ValidateRankingMode(req.RankingMode);
        var tenantScopeInvalid = string.IsNullOrWhiteSpace(req.OrganizationId) || string.IsNullOrWhiteSpace(req.EnvironmentId);
        var pageOffsetValid = TryCalculatePageOffset(req.PageIndex, req.PageSize, out var pageOffset);
        if (tenantScopeInvalid || scopeError is not null || rankingError is not null || !pageOffsetValid)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                StatusCodes.Status400BadRequest,
                tenantScopeInvalid
                    ? "directory-tenant-scope-invalid"
                    : scopeError ?? rankingError ?? "directory-page-invalid",
                ct);
            return;
        }

        var scopeKind = req.ScopeKind?.Trim().ToLowerInvariant();
        var scopeId = req.ScopeId?.Trim();
        var bearerToken = await BusinessGatewayAuthorization.RequirePermissionAsync(
            HttpContext,
            auth,
            new BusinessGatewayPermissionRequirement(
                definition.PermissionCode,
                req.OrganizationId,
                req.EnvironmentId,
                scopeKind ?? "organization",
                scopeId ?? req.OrganizationId),
            BusinessGatewayAuthorizationContinuityMode.ReadCacheAllowed,
            ct);
        if (bearerToken is null)
        {
            return;
        }

        var authorization = HttpContext.Items[BusinessGatewayAuthorization.PrincipalItemKey]
            as BusinessGatewayAuthorizationResult;
        var authorizedScope = BusinessConsoleSearchableDirectoryPolicy.ResolveAuthorizedScope(
            definition,
            authorization,
            req.OrganizationId,
            scopeKind,
            scopeId);
        if (authorizedScope is null)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                HttpContext,
                StatusCodes.Status403Forbidden,
                "directory-scope-not-authorized",
                ct);
            return;
        }
        scopeKind = authorizedScope.Kind;
        scopeId = authorizedScope.Id;

        try
        {
            var response = definition.Owner switch
            {
                "master-data" => await QueryMasterDataAsync(req with { DirectoryType = directoryType }, scopeKind, scopeId, pageOffset, ct),
                "inventory" => await QueryInventoryAsync(req with { DirectoryType = directoryType }, scopeKind, scopeId, pageOffset, ct),
                "quality" => await QueryQualityAsync(req with { DirectoryType = directoryType }, pageOffset, ct),
                "maintenance" => await QueryMaintenanceAsync(req with { DirectoryType = directoryType }, pageOffset, ct),
                _ => throw new InvalidOperationException("Unknown directory owner."),
            };
            await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status200OK, response, ct);
        }
        catch (BusinessServiceProxyException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, (int)ex.StatusCode, ex.Message, ct);
        }
    }

    private async Task<BusinessConsoleSearchableDirectoryResponse> QueryMasterDataAsync(
        BusinessConsoleSearchableDirectoryRequest request,
        string? scopeKind,
        string? scopeId,
        int pageOffset,
        CancellationToken cancellationToken)
    {
        if (request.DirectoryType == "personnel")
        {
            var workers = await masterData.ListWorkersAsync(
                tokenProvider.BearerToken,
                new BusinessConsoleWorkerDirectoryRequest(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.Keyword,
                    TeamCode: scopeKind == "team" ? scopeId : null,
                    WorkshopCode: scopeKind == "workshop" ? scopeId : null,
                    WorkCenterCode: scopeKind == "work-center" ? scopeId : null,
                    PageIndex: request.PageIndex,
                    PageSize: request.PageSize),
                cancellationToken);
            ValidateWorkers(workers, request, pageOffset);
            return BusinessConsoleSearchableDirectoryResponse.FromItems(
                request.DirectoryType,
                [.. workers.Items.Select(worker => new BusinessConsoleSearchableDirectoryItem(
                    worker.UserId,
                    worker.DisplayName,
                    worker.EmployeeNo,
                    "master-data",
                    Context(
                        ("departmentCode", worker.DepartmentCode),
                        ("jobTitle", worker.JobTitle),
                        ("employmentStatus", worker.EmploymentStatus))))],
                workers.TotalCount,
                "master-data",
                rankingMode: request.RankingMode);
        }

        var resourceType = request.DirectoryType switch
        {
            "team" => "team",
            "equipment" => "device-asset",
            "work-center" => "work-center",
            "station" => "station",
            "workshop" => "workshop",
            "material" => "sku",
            "priority" => "reference-data",
            _ => throw new InvalidOperationException("Unsupported MasterData directory type."),
        };
        var query = new BusinessConsoleListResourcesRequest(
            request.OrganizationId,
            request.EnvironmentId,
            resourceType,
            Skip: pageOffset,
            Take: request.PageSize,
            CodeSet: request.DirectoryType == "priority" ? "priority" : null,
            SiteCode: scopeKind == "site" ? scopeId : null,
            WorkCenterCode: scopeKind == "work-center" ? scopeId : null,
            Keyword: request.Keyword,
            WorkshopCode: scopeKind == "workshop" ? scopeId : null);
        var resources = await masterData.ListResourcesAsync(tokenProvider.BearerToken, query, cancellationToken);
        ValidateResources(resources, request, pageOffset);
        var authorityConfigured = true;
        if (request.DirectoryType == "priority" && resources.Total == 0)
        {
            var probe = await masterData.ListResourcesAsync(
                tokenProvider.BearerToken,
                query with { Keyword = null, Skip = 0, Take = 1 },
                cancellationToken);
            authorityConfigured = probe.Total > 0;
        }

        return BusinessConsoleSearchableDirectoryResponse.FromItems(
            request.DirectoryType,
            [.. resources.Resources.Select(resource => new BusinessConsoleSearchableDirectoryItem(
                request.DirectoryType == "equipment" && !string.IsNullOrWhiteSpace(resource.DeviceAssetId)
                    ? resource.DeviceAssetId
                    : resource.Code,
                resource.DisplayName,
                request.DirectoryType == "station" ? resource.StationCode : resource.Code,
                "master-data",
                Context(
                    ("siteCode", resource.SiteCode),
                    ("workshopCode", resource.WorkshopCode),
                    ("workCenterCode", resource.WorkCenterCode),
                    ("stationCode", resource.StationCode))))],
            resources.Total,
            "master-data",
            authorityConfigured,
            rankingMode: request.RankingMode);
    }

    private async Task<BusinessConsoleSearchableDirectoryResponse> QueryInventoryAsync(
        BusinessConsoleSearchableDirectoryRequest request,
        string? scopeKind,
        string? scopeId,
        int pageOffset,
        CancellationToken cancellationToken)
    {
        var response = await inventory.ListDirectoryAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleInventoryDirectoryRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.DirectoryType,
                request.Keyword,
                scopeKind == "site" ? scopeId : null,
                request.SkuCode,
                pageOffset,
                request.PageSize),
            cancellationToken);
        ValidateInventory(response, request, pageOffset);

        return BusinessConsoleSearchableDirectoryResponse.FromItems(
            request.DirectoryType,
            [.. response.Items.Select(item => new BusinessConsoleSearchableDirectoryItem(
                item.Id,
                item.Display,
                item.Code,
                "inventory",
                Context(("siteCode", item.SiteCode), ("skuCode", item.SkuCode))))],
            response.Total,
            "inventory",
            rankingMode: request.RankingMode);
    }

    private async Task<BusinessConsoleSearchableDirectoryResponse> QueryQualityAsync(
        BusinessConsoleSearchableDirectoryRequest request,
        int pageOffset,
        CancellationToken cancellationToken)
    {
        var response = await quality.ListQualityReasonsAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleQualityReasonListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                Enabled: true,
                Search: request.Keyword,
                Skip: pageOffset,
                Take: request.PageSize,
                DefaultDisposition: request.DirectoryType == "scrap-reason" ? "scrap" : null),
            cancellationToken);
        ValidateQuality(response, request, pageOffset);
        return BusinessConsoleSearchableDirectoryResponse.FromItems(
            request.DirectoryType,
            [.. response.Items.Select(item => new BusinessConsoleSearchableDirectoryItem(
                item.ReasonCode,
                item.ReasonName,
                item.ReasonCode,
                "quality",
                Context(
                    ("groupName", item.GroupName),
                    ("severity", item.Severity),
                    ("defaultDisposition", item.DefaultDisposition))))],
            response.Total,
            "quality",
            rankingMode: request.RankingMode);
    }

    private async Task<BusinessConsoleSearchableDirectoryResponse> QueryMaintenanceAsync(
        BusinessConsoleSearchableDirectoryRequest request,
        int pageOffset,
        CancellationToken cancellationToken)
    {
        var response = await maintenance.ListDowntimeReasonsAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleMaintenanceReasonDirectoryRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Keyword,
                pageOffset,
                request.PageSize),
            cancellationToken);
        ValidateMaintenance(response, request, pageOffset);
        return BusinessConsoleSearchableDirectoryResponse.FromItems(
            request.DirectoryType,
            [.. response.Items.Select(item => new BusinessConsoleSearchableDirectoryItem(
                item.Id,
                item.Description,
                item.ReasonCode,
                "maintenance",
                Context(
                    ("reasonCategory", item.ReasonCategory),
                    ("lossCategory", item.LossCategory),
                    ("authorityAlias", request.DirectoryType == "maintenance-reason" ? "downtime-reason" : null))))],
            response.Total,
            "maintenance",
            authorityDirectoryType: "downtime-reason",
            rankingMode: request.RankingMode);
    }

    private static IReadOnlyDictionary<string, string?> Context(params (string Key, string? Value)[] values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

    private static void ValidateWorkers(
        BusinessConsoleWorkerDirectoryResponse response,
        BusinessConsoleSearchableDirectoryRequest request,
        int offset)
    {
        if (response.Items is null
            || response.PageIndex != request.PageIndex
            || response.PageSize != request.PageSize
            || response.TotalCount < 0
            || response.Items.Count > request.PageSize
            || response.Items.Count > 0 && response.TotalCount < (long)offset + response.Items.Count
            || response.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.UserId)
                || string.IsNullOrWhiteSpace(item.EmployeeNo)
                || string.IsNullOrWhiteSpace(item.DisplayName)))
        {
            throw InvalidOwnerResponse();
        }
    }

    private static void ValidateResources(
        BusinessConsoleResourceListResponse response,
        BusinessConsoleSearchableDirectoryRequest request,
        int offset)
    {
        if (response.Resources is null
            || response.Total < 0
            || response.Resources.Count > request.PageSize
            || response.Resources.Count > 0 && response.Total < (long)offset + response.Resources.Count
            || response.Resources.Any(item =>
                string.IsNullOrWhiteSpace(item.Code)
                || string.IsNullOrWhiteSpace(item.DisplayName)))
        {
            throw InvalidOwnerResponse();
        }
    }

    private static void ValidateInventory(
        BusinessConsoleInventoryDirectoryResponse response,
        BusinessConsoleSearchableDirectoryRequest request,
        int offset)
    {
        if (!string.Equals(response.Status, "available", StringComparison.Ordinal)
            || response.ReasonCode is not null
            || response.Items is null
            || response.Total < 0
            || response.Skip != offset
            || response.Take != request.PageSize
            || string.IsNullOrWhiteSpace(response.SourceKind)
            || response.AsOfUtc == default
            || response.Items.Count > request.PageSize
            || response.Items.Count > 0 && response.Total < (long)offset + response.Items.Count
            || response.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.Id)
                || string.IsNullOrWhiteSpace(item.Code)
                || string.IsNullOrWhiteSpace(item.Display)
                || !string.Equals(item.DirectoryType, request.DirectoryType, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(item.SnapshotVersion)))
        {
            throw InvalidOwnerResponse();
        }
    }

    private static void ValidateQuality(
        BusinessConsoleQualityReasonListResponse response,
        BusinessConsoleSearchableDirectoryRequest request,
        int offset)
    {
        if (response.Items is null
            || response.Total < 0
            || response.Items.Count > request.PageSize
            || response.Items.Count > 0 && response.Total < (long)offset + response.Items.Count
            || response.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.ReasonCode)
                || string.IsNullOrWhiteSpace(item.ReasonName)))
        {
            throw InvalidOwnerResponse();
        }
    }

    private static void ValidateMaintenance(
        BusinessConsoleMaintenanceReasonDirectoryResponse response,
        BusinessConsoleSearchableDirectoryRequest request,
        int offset)
    {
        if (response.Items is null
            || response.Skip != offset
            || response.Take != request.PageSize
            || response.Total < 0
            || response.Items.Count > request.PageSize
            || response.Items.Count > 0 && response.Total < (long)offset + response.Items.Count
            || response.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.Id)
                || string.IsNullOrWhiteSpace(item.ReasonCode)
                || string.IsNullOrWhiteSpace(item.Description)))
        {
            throw InvalidOwnerResponse();
        }
    }

    private static BusinessServiceProxyException InvalidOwnerResponse() =>
        BusinessServiceProxyException.FromSafeDownstreamMessage(
            System.Net.HttpStatusCode.BadGateway,
            "downstream-invalid-response");

    private static bool TryCalculatePageOffset(int pageIndex, int pageSize, out int offset)
    {
        offset = 0;
        if (pageIndex < 1 || pageSize is < 1 or > 100)
        {
            return false;
        }

        var candidate = ((long)pageIndex - 1L) * pageSize;
        if (candidate > int.MaxValue)
        {
            return false;
        }

        offset = (int)candidate;
        return true;
    }
}
