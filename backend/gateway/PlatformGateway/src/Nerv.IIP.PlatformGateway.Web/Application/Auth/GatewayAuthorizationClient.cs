using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Nerv.IIP.Caching;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public sealed class GatewayAuthorizationOptions
{
    public int AuthorizationCacheTtlSeconds { get; set; } = 45;
}

public sealed class HttpGatewayAuthorizationClient(
    HttpClient httpClient,
    IAppCache cache,
    IOptions<GatewayAuthorizationOptions> options) : IGatewayAuthorizationClient
{
    private TimeSpan AuthorizationCacheTtl => TimeSpan.FromSeconds(
        options.Value.AuthorizationCacheTtlSeconds > 0
            ? options.Value.AuthorizationCacheTtlSeconds
            : 45);

    public async Task<GatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        GatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(bearerToken, requirement);
        return await cache.GetOrCreateAsync(
            cacheKey,
            () => CheckRemoteAsync(bearerToken, requirement, cancellationToken),
            AuthorizationCacheTtl);
    }

    private async Task<GatewayAuthorizationResult> CheckRemoteAsync(
        string bearerToken,
        GatewayPermissionRequirement requirement,
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
            return GatewayAuthorizationResult.Forbidden("unauthorized");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return GatewayAuthorizationResult.Forbidden("forbidden");
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<AuthorizationCheckResponse>>(cancellationToken);
        var body = envelope?.Data;
        return body is not null && body.Allowed
            ? GatewayAuthorizationResult.Allowed(body.PrincipalId!, body.PrincipalType!, body.LoginName!)
            : GatewayAuthorizationResult.Forbidden(body?.DenialReason ?? "forbidden");
    }

    private static string BuildCacheKey(string bearerToken, GatewayPermissionRequirement requirement)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bearerToken))).ToLowerInvariant();
        var permissionVersion = TryReadPermissionVersion(bearerToken) ?? "unknown";
        var resourceType = requirement.ResourceType ?? "-";
        var resourceId = requirement.ResourceId ?? "-";
        return string.Join(
            ':',
            "gateway",
            "authorization",
            tokenHash,
            "permission-version",
            permissionVersion,
            requirement.PermissionCode,
            requirement.OrganizationId,
            requirement.EnvironmentId,
            resourceType,
            resourceId,
            "v1");
    }

    private static string? TryReadPermissionVersion(string bearerToken)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(bearerToken);
            var value = jwt.Claims.FirstOrDefault(claim => claim.Type == "permissionVersion")?.Value;
            return int.TryParse(value, out var permissionVersion)
                ? permissionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
