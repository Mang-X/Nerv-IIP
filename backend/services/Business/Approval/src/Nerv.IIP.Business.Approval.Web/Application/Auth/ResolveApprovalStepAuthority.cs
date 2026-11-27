using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Approval.Web.Application.Auth;

public sealed class ResolveApprovalStepAuthority(
    IHttpClientFactory clients,
    IInternalServiceTokenProvider internalToken,
    ApplicationDbContext dbContext,
    ILogger<ResolveApprovalStepAuthority> logger)
{
    public const string IamClient = "ApprovalIam";
    public const string MasterDataClient = "ApprovalMasterData";

    internal Task<(int Status, Actor? Actor)> ResolveAsync(
        string authorization, string organizationId, string environmentId, ApprovalChainId chainId, CancellationToken ct,
        bool loadApproverContext = true) =>
        ResolveAsync(authorization, organizationId, environmentId, chainId, ct, loadApproverContext, verifySource: false);

    internal Task<(int Status, Actor? Actor)> ResolveChainActionAsync(
        string authorization, string? organizationId, string? environmentId, ApprovalChainId chainId, CancellationToken ct) =>
        ResolveAsync(authorization, organizationId, environmentId, chainId, ct, loadApproverContext: false, verifySource: true);

    private async Task<(int Status, Actor? Actor)> ResolveAsync(
        string authorization, string? organizationId, string? environmentId, ApprovalChainId chainId, CancellationToken ct,
        bool loadApproverContext, bool verifySource)
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

            ChainSource? source = null;
            if (verifySource)
            {
                if ((organizationId is null) != (environmentId is null)
                    || (organizationId is not null && (string.IsNullOrWhiteSpace(organizationId)
                        || string.IsNullOrWhiteSpace(environmentId))))
                {
                    return (StatusCodes.Status400BadRequest, null);
                }

                source = await dbContext.ApprovalChains.AsNoTracking()
                    .Where(chain => chain.Id == chainId)
                    .Select(chain => new ChainSource(
                        chain.OrganizationId, chain.EnvironmentId, chain.DocumentReference.OrganizationId))
                    .SingleOrDefaultAsync(ct);
                if (source is null || (organizationId is not null
                    && (organizationId != source.OrganizationId || environmentId != source.EnvironmentId)))
                {
                    return (StatusCodes.Status403Forbidden, null);
                }

                organizationId = source.OrganizationId;
                environmentId = source.EnvironmentId;
            }

            if (organizationId is null || environmentId is null
                || (principal.PrincipalType == "external-client"
                    && (principal.OrganizationId != organizationId || principal.EnvironmentId != environmentId)))
            {
                return (StatusCodes.Status403Forbidden, null);
            }

            using var check = await iam.PostAsJsonAsync("/internal/iam/v1/authorization/check",
                new AuthorizationCheckRequest(ApprovalPermissionCodes.Manage, organizationId, environmentId,
                    "approval-chain", chainId.Id.ToString("D"))
                {
                    IncludeSelectedMembership = principal.PrincipalType == "user",
                    IncludePrincipalContext = verifySource && principal.PrincipalType == "user",
                }, ct);
            if (check.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ((int)check.StatusCode, null);
            }

            var permission = await ReadDataAsync<AuthorizationCheckResponse>(check, ct);
            if (!permission.Allowed || permission.PrincipalId != principal.UserId
                || permission.PrincipalType != principal.PrincipalType || permission.DataScope?.DenyAll == true
                || (source is not null && !AllowsSource(permission, source)))
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
                    department = await LoadDepartmentAsync(principal.UserId, organizationId, environmentId, ct);
                }
            }

            return (StatusCodes.Status200OK, new Actor(organizationId, environmentId,
                principal.PrincipalType, principal.UserId, roles, department));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Approval action authority unavailable ({ErrorType}).", ex.GetType().Name);
            return (StatusCodes.Status503ServiceUnavailable, null);
        }
    }

    private static bool AllowsSource(AuthorizationCheckResponse permission, ChainSource source)
    {
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

        // Approval stores routing metadata. It has no authoritative source location,
        // team or owner to compare with these IAM grants.
        return scope.SiteCodes is { Count: 0 }
            && scope.WorkshopCodes is { Count: 0 }
            && scope.ProductionLineCodes is { Count: 0 }
            && scope.WorkCenterCodes is not { Count: > 0 }
            && scope.TeamCodes is not { Count: > 0 }
            && scope.SelfIds is not { Count: > 0 }
            && (scope.OrganizationIds is not { Count: > 0 } organizations
                || (source.DocumentOrganizationId == source.OrganizationId
                    && organizations.All(id => id == source.DocumentOrganizationId)));
    }

    private sealed record ChainSource(string OrganizationId, string EnvironmentId, string? DocumentOrganizationId);

    private async Task<string?> LoadDepartmentAsync(
        string userId, string organizationId, string environmentId, CancellationToken ct)
    {
        using var masterData = clients.CreateClient(MasterDataClient);
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
    private sealed record WorkContext(Worker? Worker);
    private sealed record Worker(string UserId, string EmploymentStatus, string? DepartmentId);
}
