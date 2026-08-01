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
        if (tenantScopeInvalid || scopeError is not null || rankingError is not null || req.PageIndex < 1 || req.PageSize is < 1 or > 100)
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
                scopeKind ?? $"{definition.Owner}-directory",
                scopeId),
            BusinessGatewayAuthorizationContinuityMode.ReadCacheAllowed,
            ct);
        if (bearerToken is null)
        {
            return;
        }

        try
        {
            var response = definition.Owner switch
            {
                "master-data" => await QueryMasterDataAsync(req with { DirectoryType = directoryType }, scopeKind, scopeId, ct),
                "inventory" => await QueryInventoryAsync(req with { DirectoryType = directoryType }, scopeKind, scopeId, ct),
                "quality" => await QueryQualityAsync(req with { DirectoryType = directoryType }, ct),
                "maintenance" => await QueryMaintenanceAsync(req with { DirectoryType = directoryType }, ct),
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
            return BusinessConsoleSearchableDirectoryResponse.FromItems(
                request.DirectoryType,
                workers.Items.Select(worker => new BusinessConsoleSearchableDirectoryItem(
                    worker.UserId,
                    worker.DisplayName,
                    worker.EmployeeNo,
                    "master-data",
                    Context(
                        ("departmentCode", worker.DepartmentCode),
                        ("jobTitle", worker.JobTitle),
                        ("employmentStatus", worker.EmploymentStatus)))).ToArray(),
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
            Skip: (request.PageIndex - 1) * request.PageSize,
            Take: request.PageSize,
            CodeSet: request.DirectoryType == "priority" ? "priority" : null,
            SiteCode: scopeKind == "site" ? scopeId : null,
            WorkCenterCode: scopeKind == "work-center" ? scopeId : null,
            Keyword: request.Keyword,
            WorkshopCode: scopeKind == "workshop" ? scopeId : null);
        var resources = await masterData.ListResourcesAsync(tokenProvider.BearerToken, query, cancellationToken);
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
            resources.Resources.Select(resource => new BusinessConsoleSearchableDirectoryItem(
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
                    ("stationCode", resource.StationCode)))).ToArray(),
            resources.Total,
            "master-data",
            authorityConfigured,
            rankingMode: request.RankingMode);
    }

    private async Task<BusinessConsoleSearchableDirectoryResponse> QueryInventoryAsync(
        BusinessConsoleSearchableDirectoryRequest request,
        string? scopeKind,
        string? scopeId,
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
                (request.PageIndex - 1) * request.PageSize,
                request.PageSize),
            cancellationToken);
        if (!string.Equals(response.Status, "available", StringComparison.Ordinal) || response.ReasonCode is not null)
        {
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(System.Net.HttpStatusCode.BadGateway, "downstream-invalid-response");
        }

        return BusinessConsoleSearchableDirectoryResponse.FromItems(
            request.DirectoryType,
            response.Items.Select(item => new BusinessConsoleSearchableDirectoryItem(
                item.Id,
                item.DisplayName,
                item.Code,
                "inventory",
                Context(("siteCode", item.SiteCode), ("skuCode", item.SkuCode)))).ToArray(),
            response.Total,
            "inventory",
            rankingMode: request.RankingMode);
    }

    private async Task<BusinessConsoleSearchableDirectoryResponse> QueryQualityAsync(
        BusinessConsoleSearchableDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var response = await quality.ListQualityReasonsAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleQualityReasonListRequest(
                request.OrganizationId,
                request.EnvironmentId,
                Enabled: true,
                Search: request.Keyword,
                Skip: (request.PageIndex - 1) * request.PageSize,
                Take: request.PageSize,
                DefaultDisposition: request.DirectoryType == "scrap-reason" ? "scrap" : null),
            cancellationToken);
        return BusinessConsoleSearchableDirectoryResponse.FromItems(
            request.DirectoryType,
            response.Items.Select(item => new BusinessConsoleSearchableDirectoryItem(
                item.ReasonCode,
                item.ReasonName,
                item.ReasonCode,
                "quality",
                Context(
                    ("groupName", item.GroupName),
                    ("severity", item.Severity),
                    ("defaultDisposition", item.DefaultDisposition)))).ToArray(),
            response.Total,
            "quality",
            rankingMode: request.RankingMode);
    }

    private async Task<BusinessConsoleSearchableDirectoryResponse> QueryMaintenanceAsync(
        BusinessConsoleSearchableDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var response = await maintenance.ListDowntimeReasonsAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleMaintenanceReasonDirectoryRequest(
                request.OrganizationId,
                request.EnvironmentId,
                request.Keyword,
                (request.PageIndex - 1) * request.PageSize,
                request.PageSize),
            cancellationToken);
        return BusinessConsoleSearchableDirectoryResponse.FromItems(
            request.DirectoryType,
            response.Items.Select(item => new BusinessConsoleSearchableDirectoryItem(
                item.Id,
                item.Description,
                item.ReasonCode,
                "maintenance",
                Context(
                    ("reasonCategory", item.ReasonCategory),
                    ("lossCategory", item.LossCategory),
                    ("authorityAlias", request.DirectoryType == "maintenance-reason" ? "downtime-reason" : null)))).ToArray(),
            response.Total,
            "maintenance",
            authorityDirectoryType: "downtime-reason",
            rankingMode: request.RankingMode);
    }

    private static IReadOnlyDictionary<string, string?> Context(params (string Key, string? Value)[] values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);
}
