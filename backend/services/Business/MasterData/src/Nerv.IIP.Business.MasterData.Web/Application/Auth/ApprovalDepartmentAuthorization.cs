using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.MasterData.Web.Application.Auth;

internal sealed class ApprovalDepartmentAuthorization(IHttpClientFactory clients)
{
    internal const string Policy = "MasterData.ApprovalDepartment";
    internal const string Scheme = "MasterDataApprovalDepartmentCaller";
    private const string IamClient = "MasterDataApprovalDepartmentIam";

    internal static void Add(WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        services.AddScoped<ApprovalDepartmentAuthorization>();
        var callers = configuration.GetSection("MasterData:ApprovalDepartment:ScopedCallers");
        if (!callers.Exists())
        {
            services.AddAuthorization(options =>
                options.AddPolicy(Policy, policy => policy.RequireAssertion(_ => false)));
            return;
        }

        services.AddNervIipScopedCallerAuthentication(callers, Scheme);
        services.AddOptions<ScopedCallerAuthenticationOptions>(Scheme)
            .Validate<IInternalServiceTokenProvider>((options, shared) =>
                options.Profiles.All(profile => profile.BearerToken != shared.BearerToken),
                "Approval department credentials must differ from the shared internal bearer.");
        services.AddNervIipScopedCallerPolicy(Policy, Scheme, NervIipPermissionCodes.InternalMasterDataApprovalDepartmentRead);

        var iamAddress = InternalServiceBaseAddress.ResolveAllowingTestHost(
            configuration, builder.Environment, "Iam:BaseUrl", "http://localhost:5102");
        services.AddHttpClient(IamClient, client => client.BaseAddress = iamAddress)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    }

    internal async Task<(int Status, string? UserId)> VerifyUserAsync(
        AuthenticationHeaderValue bearer, string organizationId, string environmentId, CancellationToken ct)
    {
        try
        {
            using var iam = clients.CreateClient(IamClient);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/iam/v1/authorization/check")
            {
                Content = JsonContent.Create(new AuthorizationCheckRequest(
                    NervIipPermissionCodes.ApprovalsManage, organizationId, environmentId, null, null)
                {
                    IncludeSelectedMembership = true,
                }),
            };
            request.Headers.Authorization = bearer;
            using var response = await iam.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ((int)response.StatusCode, null);
            }

            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<AuthorizationEnvelope>(ct);
            if (envelope is not { Success: true, Data: { } authorization })
            {
                return (StatusCodes.Status503ServiceUnavailable, null);
            }

            var membership = authorization.SelectedMembership;
            if (!authorization.Allowed || authorization.PrincipalType != "user"
                || string.IsNullOrWhiteSpace(authorization.PrincipalId) || authorization.DataScope?.DenyAll == true
                || membership?.RoleIds is not { Count: > 0 }
                || membership.OrganizationId != organizationId || membership.EnvironmentId != environmentId)
            {
                return (StatusCodes.Status403Forbidden, null);
            }

            return (StatusCodes.Status200OK, authorization.PrincipalId);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                   || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return (StatusCodes.Status503ServiceUnavailable, null);
        }
    }

    private sealed record AuthorizationEnvelope(bool Success, AuthorizationCheckResponse? Data);
}
