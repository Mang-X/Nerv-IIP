using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Approval.Web.Application.Auth;

public sealed class ResolveApprovalStepAuthority(
    IHttpClientFactory clients,
    IInternalServiceTokenProvider internalToken,
    ILogger<ResolveApprovalStepAuthority> logger)
{
    public const string IamClient = "ApprovalIam";
    public const string MasterDataClient = "ApprovalMasterData";

    internal async Task<(int Status, Actor? Actor)> ResolveAsync(
        string authorization, string organizationId, string environmentId, ApprovalChainId chainId, CancellationToken ct)
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
            // IAM's current principal carries the roles of the token's active membership.
            if (string.IsNullOrWhiteSpace(principal.UserId)
                || principal.OrganizationId != organizationId || principal.EnvironmentId != environmentId)
            {
                return (StatusCodes.Status403Forbidden, null);
            }

            using var check = await iam.PostAsJsonAsync("/internal/iam/v1/authorization/check",
                new AuthorizationCheckRequest(ApprovalPermissionCodes.Manage, organizationId, environmentId,
                    "approval-chain", chainId.Id.ToString("D")), ct);
            if (check.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ((int)check.StatusCode, null);
            }

            var permission = await ReadDataAsync<AuthorizationCheckResponse>(check, ct);
            if (!permission.Allowed || permission.PrincipalId != principal.UserId
                || permission.PrincipalType != principal.PrincipalType || permission.DataScope?.DenyAll == true
                || principal.PrincipalType is not ("user" or "external-client"))
            {
                return (StatusCodes.Status403Forbidden, null);
            }

            var department = principal.PrincipalType == "user"
                ? await LoadDepartmentAsync(principal, ct)
                : null;
            var roles = principal.PrincipalType == "user" ? string.Join(",", principal.RoleIds ?? []) : null;
            return (StatusCodes.Status200OK, new Actor(principal.OrganizationId, principal.EnvironmentId,
                principal.PrincipalType, principal.UserId, roles, department));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Approval resolution authority unavailable ({ErrorType}).", ex.GetType().Name);
            return (StatusCodes.Status503ServiceUnavailable, null);
        }
    }

    private async Task<string?> LoadDepartmentAsync(Principal principal, CancellationToken ct)
    {
        using var masterData = clients.CreateClient(MasterDataClient);
        masterData.DefaultRequestHeaders.Authorization = new("Bearer", internalToken.BearerToken);
        using var response = await masterData.GetAsync(
            $"/api/business/v1/master-data/principals/{Uri.EscapeDataString(principal.UserId)}/work-context"
            + $"?organizationId={Uri.EscapeDataString(principal.OrganizationId)}"
            + $"&environmentId={Uri.EscapeDataString(principal.EnvironmentId)}", ct);
        var workContext = await ReadDataAsync<WorkContext>(response, ct);
        return workContext.Worker is { EmploymentStatus: "active" } worker && worker.UserId == principal.UserId
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
        string UserId, string PrincipalType, string OrganizationId, string EnvironmentId, IReadOnlyList<string>? RoleIds);
    private sealed record WorkContext(Worker? Worker);
    private sealed record Worker(string UserId, string EmploymentStatus, string? DepartmentId);
}
