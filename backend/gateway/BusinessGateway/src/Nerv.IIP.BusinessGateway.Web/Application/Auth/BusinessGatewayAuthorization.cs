using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public sealed record BusinessGatewayPermissionRequirement(
    string PermissionCode,
    string OrganizationId,
    string EnvironmentId,
    string? ResourceType,
    string? ResourceId);

public sealed record BusinessGatewayAuthorizationResult(
    bool IsAllowed,
    string? PrincipalId,
    string? PrincipalType,
    string? LoginName,
    string? DenialReason)
{
    public static BusinessGatewayAuthorizationResult Allowed(string principalId, string principalType, string loginName) =>
        new(true, principalId, principalType, loginName, null);

    public static BusinessGatewayAuthorizationResult Forbidden(string reason) =>
        new(false, null, null, null, reason);
}

public interface IBusinessGatewayAuthorizationClient
{
    Task<BusinessGatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        BusinessGatewayPermissionRequirement requirement,
        CancellationToken cancellationToken);
}

public static class BusinessGatewayPermissions
{
    public const string MasterDataProductsRead = "business.masterdata.products.read";
    public const string MasterDataProductsManage = "business.masterdata.products.manage";
    public const string MasterDataResourcesRead = "business.masterdata.resources.read";
    public const string InventoryLedgerRead = "business.inventory.ledger.read";
    public const string InventoryMovementsCreate = "business.inventory.movements.create";
    public const string InventoryCountsManage = "business.inventory.counts.manage";
    public const string QualityInspectionRecordsRead = "business.quality.inspection-records.read";
    public const string QualityInspectionRecordsCreate = "business.quality.inspection-records.create";
    public const string QualityNcrRead = "business.quality.ncr.read";
    public const string QualityNcrManage = "business.quality.ncr.manage";
    public const string MesFoundationRead = "business.mes.foundation.read";
    public const string MesOverviewRead = "business.mes.overview.read";
    public const string MesPlansRead = "business.mes.plans.read";
    public const string MesWorkOrdersRead = "business.mes.work-orders.read";
    public const string MesWorkOrdersManage = "business.mes.work-orders.manage";
    public const string MesMaterialsRead = "business.mes.materials.read";
    public const string MesMaterialsManage = "business.mes.materials.manage";
    public const string MesDispatchRead = "business.mes.dispatch.read";
    public const string MesDispatchManage = "business.mes.dispatch.manage";
    public const string MesOperationsRead = "business.mes.operations.read";
    public const string MesOperationsManage = "business.mes.operations.manage";
    public const string MesReportingRead = "business.mes.reporting.read";
    public const string MesReportingWrite = "business.mes.reporting.write";
    public const string MesQualityRead = "business.mes.quality.read";
    public const string MesQualityWrite = "business.mes.quality.write";
    public const string MesReceiptsRead = "business.mes.receipts.read";
    public const string MesReceiptsManage = "business.mes.receipts.manage";
    public const string MesDowntimeRead = "business.mes.downtime.read";
    public const string MesDowntimeManage = "business.mes.downtime.manage";
    public const string MesHandoversRead = "business.mes.handovers.read";
    public const string MesHandoversManage = "business.mes.handovers.manage";
    public const string MesTraceabilityRead = "business.mes.traceability.read";
    public const string MesSchedulesRead = "business.mes.schedules.read";
    public const string MesSchedulesManage = "business.mes.schedules.manage";
    public const string MesCapacityRead = "business.mes.capacity.read";
}

public sealed class BusinessGatewayAuthorizationOptions
{
    public int AuthorizationCacheTtlSeconds { get; set; } = 15;

    public string AuthorizationCheckPath { get; set; } = "/internal/iam/v1/authorization/check";
}

public sealed class HttpBusinessGatewayAuthorizationClient(
    HttpClient httpClient,
    IAppCache cache,
    IOptions<BusinessGatewayAuthorizationOptions> options) : IBusinessGatewayAuthorizationClient
{
    private TimeSpan AuthorizationCacheTtl => TimeSpan.FromSeconds(
        options.Value.AuthorizationCacheTtlSeconds > 0
            ? options.Value.AuthorizationCacheTtlSeconds
            : 15);

    public async Task<BusinessGatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        BusinessGatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(bearerToken, requirement);
        return await cache.GetOrCreateAsync(
            cacheKey,
            () => CheckRemoteAsync(bearerToken, requirement, cancellationToken),
            AuthorizationCacheTtl);
    }

    private async Task<BusinessGatewayAuthorizationResult> CheckRemoteAsync(
        string bearerToken,
        BusinessGatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthorizationCheckPath());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = JsonContent.Create(new AuthorizationCheckRequest(
            requirement.PermissionCode,
            requirement.OrganizationId,
            requirement.EnvironmentId,
            requirement.ResourceType,
            requirement.ResourceId));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return BusinessGatewayAuthorizationResult.Forbidden("unauthorized");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return BusinessGatewayAuthorizationResult.Forbidden("forbidden");
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<AuthorizationCheckResponse>>(cancellationToken);
        var body = envelope?.Data;
        return body is not null && body.Allowed
            ? BusinessGatewayAuthorizationResult.Allowed(body.PrincipalId!, body.PrincipalType!, body.LoginName!)
            : BusinessGatewayAuthorizationResult.Forbidden(body?.DenialReason ?? "forbidden");
    }

    private static string BuildCacheKey(string bearerToken, BusinessGatewayPermissionRequirement requirement)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bearerToken))).ToLowerInvariant();
        var resourceType = requirement.ResourceType ?? "-";
        var resourceId = requirement.ResourceId ?? "-";
        return string.Join(
            ':',
            "business-gateway",
            "authorization",
            tokenHash,
            requirement.PermissionCode,
            requirement.OrganizationId,
            requirement.EnvironmentId,
            resourceType,
            resourceId,
            "v1");
    }

    private string AuthorizationCheckPath()
    {
        var configuredPath = options.Value.AuthorizationCheckPath;
        return string.IsNullOrWhiteSpace(configuredPath)
            ? "/internal/iam/v1/authorization/check"
            : configuredPath;
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}

public static class BusinessGatewayAuthorization
{
    public const string PrincipalItemKey = "Nerv.IIP.BusinessGateway.Principal";

    public static async Task<string?> RequirePermissionAsync(
        HttpContext context,
        IBusinessGatewayAuthorizationClient auth,
        BusinessGatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        var bearerToken = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Unauthorized.",
                cancellationToken);
            return null;
        }

        var principalOrganizationId = FirstClaimValue(context.User, "organizationId");
        var principalEnvironmentId = FirstClaimValue(context.User, "environmentId");
        if (!string.Equals(principalOrganizationId, requirement.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(principalEnvironmentId, requirement.EnvironmentId, StringComparison.Ordinal))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Forbidden.",
                cancellationToken);
            return null;
        }

        var result = await auth.CheckAsync(bearerToken, requirement, cancellationToken);
        if (!result.IsAllowed)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Forbidden.",
                cancellationToken);
            return null;
        }

        context.Items[PrincipalItemKey] = result;
        return bearerToken;
    }

    private static string? FirstClaimValue(ClaimsPrincipal user, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = user.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
