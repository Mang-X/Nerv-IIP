using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalDelegationAggregate;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Approval.Web.Application.Auth;

public sealed class ResolveApprovalStepAuthority(
    IHttpClientFactory clients,
    IInternalServiceTokenProvider internalToken,
    IConfiguration configuration,
    ApplicationDbContext dbContext,
    ILogger<ResolveApprovalStepAuthority> logger)
{
    public const string IamClient = "ApprovalIam";
    public const string MasterDataClient = "ApprovalMasterData";

    internal Task<(int Status, Actor? Actor)> ResolveChainActionAsync(
        string authorization, string? organizationId, string? environmentId, ApprovalChainId chainId, CancellationToken ct,
        bool loadApproverContext = false) =>
        ResolveAsync(authorization, organizationId, environmentId, chainId, null, loadApproverContext, ct);

    internal Task<(int Status, Actor? Actor)> ResolveDelegationAsync(
        string authorization, string organizationId, string environmentId, ApprovalDelegationId? delegationId, CancellationToken ct) =>
        ResolveAsync(authorization, organizationId, environmentId, null, delegationId, false, ct);

    private async Task<(int Status, Actor? Actor)> ResolveAsync(
        string authorization, string? organizationId, string? environmentId,
        ApprovalChainId? chainId, ApprovalDelegationId? delegationId, bool loadApproverContext, CancellationToken ct)
    {
        if (!AuthenticationHeaderValue.TryParse(authorization, out var bearer)
            || !string.Equals(bearer.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(bearer.Parameter))
        {
            return (StatusCodes.Status401Unauthorized, null);
        }

        try
        {
            using var iam = clients.CreateClient(IamClient);
            iam.DefaultRequestHeaders.Authorization = bearer;
            using var me = await iam.GetAsync("/api/iam/v1/me", ct);
            if (me.StatusCode == HttpStatusCode.Unauthorized)
            {
                return (StatusCodes.Status401Unauthorized, null);
            }

            var principal = await ReadDataAsync<Principal>(me, ct);
            if (string.IsNullOrWhiteSpace(principal.UserId)
                || principal.PrincipalType is not ("user" or "external-client"))
            {
                return (StatusCodes.Status403Forbidden, null);
            }

            if ((organizationId is null) != (environmentId is null)
                || (organizationId is not null && (string.IsNullOrWhiteSpace(organizationId)
                    || string.IsNullOrWhiteSpace(environmentId))))
            {
                return (StatusCodes.Status400BadRequest, null);
            }

            var source = await LoadScopeAsync(organizationId, environmentId, chainId, delegationId, ct);
            if (source is null || (organizationId is not null
                && (organizationId != source.OrganizationId || environmentId != source.EnvironmentId)))
            {
                return (StatusCodes.Status403Forbidden, null);
            }

            organizationId = source.OrganizationId;
            environmentId = source.EnvironmentId;
            if (principal.PrincipalType == "external-client"
                && (principal.OrganizationId != organizationId || principal.EnvironmentId != environmentId))
            {
                return (StatusCodes.Status403Forbidden, null);
            }

            var resourceType = chainId is not null ? "approval-chain"
                : delegationId is not null ? "approval-delegation" : null;
            var resourceId = chainId?.Id.ToString("D") ?? delegationId?.Id.ToString("D");
            using var check = await iam.PostAsJsonAsync("/internal/iam/v1/authorization/check",
                new AuthorizationCheckRequest(ApprovalPermissionCodes.Manage, organizationId, environmentId,
                    resourceType, resourceId)
                {
                    IncludeSelectedMembership = principal.PrincipalType == "user",
                    IncludePrincipalContext = principal.PrincipalType == "user",
                }, ct);
            if (check.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ((int)check.StatusCode, null);
            }

            var permission = await ReadDataAsync<AuthorizationCheckResponse>(check, ct);
            if (!permission.Allowed || permission.PrincipalId != principal.UserId
                || permission.PrincipalType != principal.PrincipalType || permission.DataScope?.DenyAll == true
                || !AllowsSource(permission, source))
            {
                return (StatusCodes.Status403Forbidden, null);
            }

            string? roles = null;
            string? department = null;
            if (principal.PrincipalType == "user")
            {
                var membership = permission.SelectedMembership;
                if (membership?.RoleIds is not { Count: > 0 }
                    || membership.OrganizationId != organizationId || membership.EnvironmentId != environmentId)
                {
                    return (StatusCodes.Status403Forbidden, null);
                }

                if (loadApproverContext)
                {
                    roles = string.Join(",", membership.RoleIds);
                    department = await LoadDepartmentAsync(bearer, principal.UserId, organizationId, environmentId, ct);
                }
            }

            return (StatusCodes.Status200OK, new Actor(organizationId, environmentId,
                principal.PrincipalType, principal.UserId, roles, department));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                   || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Approval action authority unavailable ({ErrorType}).", ex.GetType().Name);
            return (StatusCodes.Status503ServiceUnavailable, null);
        }
    }

    private async Task<ApprovalScope?> LoadScopeAsync(
        string? organizationId, string? environmentId,
        ApprovalChainId? chainId, ApprovalDelegationId? delegationId, CancellationToken ct)
    {
        if (chainId is not null)
        {
            return await dbContext.ApprovalChains.AsNoTracking()
                .Where(chain => chain.Id == chainId)
                .Select(chain => new ApprovalScope(
                    chain.OrganizationId, chain.EnvironmentId, chain.DocumentReference.OrganizationId))
                .SingleOrDefaultAsync(ct);
        }

        if (delegationId is not null)
        {
            return await dbContext.ApprovalDelegations.AsNoTracking()
                .Where(delegation => delegation.Id == delegationId)
                .Select(delegation => new ApprovalScope(
                    delegation.OrganizationId, delegation.EnvironmentId, delegation.OrganizationId))
                .SingleOrDefaultAsync(ct);
        }

        // A new delegation has only the organization and environment of the requested record.
        return organizationId is not null && environmentId is not null
            ? new ApprovalScope(organizationId, environmentId, organizationId)
            : null;
    }

    private static bool AllowsSource(AuthorizationCheckResponse permission, ApprovalScope source)
    {
        var resourceOrganizationId = source.ResourceOrganizationId ?? source.OrganizationId;
        if (!string.Equals(resourceOrganizationId, source.OrganizationId, StringComparison.Ordinal))
        {
            return false;
        }

        var scope = permission.DataScope;
        if (scope is null)
        {
            // The in-memory IAM provider can return grants without a DataScope projection.
            return permission.ScopeGrants is not { Count: > 0 } grants
                || grants.Any(grant => grant.OrganizationWide
                    && grant.ScopeKind == "organization"
                    && grant.ScopeId == source.OrganizationId
                    && grant.ApplicablePermissionCodes?.Contains(ApprovalPermissionCodes.Manage, StringComparer.Ordinal) == true);
        }

        // Approval has no authoritative location, team or document owner
        // to compare with these IAM grants.
        return scope.SiteCodes is { Count: 0 }
            && scope.WorkshopCodes is { Count: 0 }
            && scope.ProductionLineCodes is { Count: 0 }
            && scope.WorkCenterCodes is not { Count: > 0 }
            && scope.TeamCodes is not { Count: > 0 }
            && scope.SelfIds is not { Count: > 0 }
            && (scope.OrganizationIds is not { Count: > 0 } organizations
                || organizations.All(id => id == resourceOrganizationId));
    }

    private sealed record ApprovalScope(string OrganizationId, string EnvironmentId, string? ResourceOrganizationId);

    private async Task<string?> LoadDepartmentAsync(
        AuthenticationHeaderValue userBearer, string userId, string organizationId, string environmentId, CancellationToken ct)
    {
        using var masterData = clients.CreateClient(MasterDataClient);
        var settings = configuration.GetSection("Approval:MasterDataDepartment");
        var credentials = settings.GetSection("OutboundCredentials");
        if (settings.GetValue("UseScopedCaller", false) || credentials.Exists())
        {
            return await LoadScopedDepartmentAsync(masterData, credentials, userBearer, organizationId, environmentId, ct);
        }

        masterData.DefaultRequestHeaders.Authorization = new("Bearer", internalToken.BearerToken);
        using var response = await masterData.GetAsync(
            $"/api/business/v1/master-data/principals/{Uri.EscapeDataString(userId)}/work-context"
            + $"?organizationId={Uri.EscapeDataString(organizationId)}"
            + $"&environmentId={Uri.EscapeDataString(environmentId)}", ct);
        var workContext = await ReadDataAsync<WorkContext>(response, ct);
        return workContext.Worker is { EmploymentStatus: "active" } worker && worker.UserId == userId
            ? worker.DepartmentId
            : null;
    }

    private async Task<string?> LoadScopedDepartmentAsync(
        HttpClient masterData, IConfigurationSection credentials, AuthenticationHeaderValue userBearer,
        string organizationId, string environmentId, CancellationToken ct)
    {
        if (!credentials.Exists())
        {
            throw new HttpRequestException("Approval department caller credentials are unavailable.");
        }

        var sharedBearer = internalToken.BearerToken;
        string credential;
        try
        {
            credential = ScopedCallerOutboundToken.Resolve(credentials, organizationId, environmentId, sharedBearer);
        }
        catch (InvalidOperationException)
        {
            throw new HttpRequestException("Approval department caller credentials are invalid for the selected scope.");
        }

        if (credential == sharedBearer || credentials.GetChildren().Count(profile => profile["BearerToken"] == credential) != 1)
        {
            throw new HttpRequestException("Approval department caller credentials must be distinct per scope.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/master-data/v1/approval-principal-department")
        {
            Content = JsonContent.Create(new { organizationId, environmentId }),
        };
        request.Headers.Authorization = new("Bearer", credential);
        request.Headers.Add(ApprovalDepartmentUserAuthorization.HeaderName, userBearer.ToString());
        using var response = await masterData.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException("Approval department lookup failed.", null, response.StatusCode);
        }

        var result = await ReadDataAsync<Department>(response, ct);
        if (result.DepartmentId is { } id && (id.Length == 0 || id != id.Trim()))
        {
            throw new JsonException("Invalid approval department identifier.");
        }

        return result.DepartmentId;
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response, CancellationToken ct)
        where T : class
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Envelope<T>>(ct);
        return body is { Success: true, Data: { } data }
            ? data
            : throw new JsonException("Expected a successful downstream response with data.");
    }

    internal sealed record Actor(
        string OrganizationId, string EnvironmentId, string Type, string Id, string? RoleRefs, string? DepartmentRef);
    private sealed record Envelope<T>(bool Success, T? Data);
    private sealed record Principal(
        string UserId, string PrincipalType, string OrganizationId, string EnvironmentId);
    private sealed record Department([property: JsonRequired] string? DepartmentId);
    private sealed record WorkContext(Worker? Worker);
    private sealed record Worker(string UserId, string EmploymentStatus, string? DepartmentId);
}
