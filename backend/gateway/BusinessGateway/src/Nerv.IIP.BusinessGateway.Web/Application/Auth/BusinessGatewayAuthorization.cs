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
    public const string MesWorkOrdersRead = "business.mes.work-orders.read";
    public const string MesWorkOrdersManage = "business.mes.work-orders.manage";
    public const string MesReportingWrite = "business.mes.reporting.write";
    public const string MesSchedulesManage = "business.mes.schedules.manage";
}

public sealed class BusinessGatewayAuthorizationOptions
{
    public int AuthorizationCacheTtlSeconds { get; set; } = 45;
}

public sealed class HttpBusinessGatewayAuthorizationClient(
    HttpClient httpClient,
    IAppCache cache,
    IOptions<BusinessGatewayAuthorizationOptions> options) : IBusinessGatewayAuthorizationClient
{
    private TimeSpan AuthorizationCacheTtl => TimeSpan.FromSeconds(
        options.Value.AuthorizationCacheTtlSeconds > 0
            ? options.Value.AuthorizationCacheTtlSeconds
            : 45);

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
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/iam/v1/authorization/check");
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
