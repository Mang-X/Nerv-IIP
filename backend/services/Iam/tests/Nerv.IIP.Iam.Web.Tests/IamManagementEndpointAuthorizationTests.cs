using System.Net;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Iam.Web.Application;
using Nerv.IIP.Iam.Web.Application.Auth;
using Nerv.IIP.Iam.Web.Application.Roles;
using Nerv.IIP.Iam.Web.Application.SecurityAudit;
using Nerv.IIP.Iam.Web.Endpoints.Roles;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamManagementEndpointAuthorizationTests
{
    [Fact]
    public void Role_mutation_endpoints_route_writes_through_mediator()
    {
        AssertRoleMutationEndpointUsesMediator<CreateRoleEndpoint>();
        AssertRoleMutationEndpointUsesMediator<PatchRolePermissionsEndpoint>();
    }

    [Fact]
    public async Task Postgres_management_endpoints_reject_principals_without_permission_in_current_org_env()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Persistence:Provider", "PostgreSQL");
                builder.UseSetting("ConnectionStrings:IamDb", "Host=localhost;Port=1;Database=nerv_iip_iam_unreachable;Username=nerv;Password=nerv");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IIamAuthService>();
                    services.RemoveAll<IIamRoleApplicationService>();
                    services.AddSingleton<IIamAuthService>(new CrossTenantAuthService());
                    services.AddSingleton<IIamRoleApplicationService, EmptyRoleApplicationService>();
                });
            });
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/iam/v1/roles");
        request.Headers.Authorization = new("Bearer", "scoped-test-token");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/iam/v1/users")]
    [InlineData("POST", "/api/iam/v1/users")]
    [InlineData("PATCH", "/api/iam/v1/users/user-admin")]
    [InlineData("POST", "/api/iam/v1/users/user-admin/disable")]
    [InlineData("POST", "/api/iam/v1/users/user-admin/reset-password")]
    [InlineData("GET", "/api/iam/v1/roles")]
    [InlineData("POST", "/api/iam/v1/roles")]
    [InlineData("PATCH", "/api/iam/v1/roles/role-platform-admin/permissions")]
    [InlineData("GET", "/api/iam/v1/permissions")]
    public async Task Postgres_management_endpoints_reject_anonymous_callers_before_touching_persistence(string method, string path)
    {
        var environment = PreserveEnvironment(
            "Persistence__Provider",
            "ConnectionStrings__IamDb");

        try
        {
            Environment.SetEnvironmentVariable("Persistence__Provider", "PostgreSQL");
            Environment.SetEnvironmentVariable("ConnectionStrings__IamDb", "Host=localhost;Port=1;Database=nerv_iip_iam_unreachable;Username=nerv;Password=nerv");

            await using var factory = new WebApplicationFactory<Program>();
            var client = factory.CreateClient();

            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            RestoreEnvironment(environment);
        }
    }

    private static IReadOnlyDictionary<string, string?> PreserveEnvironment(params string[] names)
    {
        return names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
    }

    private static void RestoreEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        foreach (var (name, value) in environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static void AssertRoleMutationEndpointUsesMediator<TEndpoint>()
    {
        var parameterTypes = typeof(TEndpoint)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IMediator), parameterTypes);
        Assert.DoesNotContain(typeof(IIamRoleApplicationService), parameterTypes);
    }

    private sealed class CrossTenantAuthService : IIamAuthService
    {
        public Task<CurrentPrincipalResponse?> GetCurrentPrincipalAsync(HttpContext httpContext, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (string.IsNullOrWhiteSpace(httpContext.Request.Headers.Authorization))
            {
                return Task.FromResult<CurrentPrincipalResponse?>(null);
            }

            return Task.FromResult<CurrentPrincipalResponse?>(new CurrentPrincipalResponse(
                "user-cross-tenant",
                "cross-tenant",
                "cross-tenant@nerv-iip.local",
                "user",
                "org-target",
                "env-prod",
                1,
                []));
        }

        public Task<bool> UserHasPermissionAsync(
            string userId,
            string organizationId,
            string environmentId,
            string permissionCode,
            CancellationToken cancellationToken)
        {
            _ = userId;
            _ = organizationId;
            _ = environmentId;
            _ = permissionCode;
            _ = cancellationToken;
            return Task.FromResult(false);
        }

        public Task<string?> GetAuthenticatedUserIdAsync(HttpContext httpContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthResponse> LoginAsync(string loginName, string password, string? clientInfo, string? ipAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AuthResponse> RefreshAsync(string refreshToken, string? clientInfo, string? ipAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeSessionAsync(string sessionId, string reason, SecurityAuditContext? auditContext, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConnectorPrincipalResponse> ValidateConnectorCredentialAsync(string connectorHostId, string secret, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ClientCredentialsTokenResponse> IssueClientCredentialsTokenAsync(string clientId, string clientSecret, string? scope, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EnterpriseAuthResponse> HandleOidcCallbackAsync(OidcLoginCallbackRequest request, string? clientInfo, string? ipAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EnterpriseAuthResponse> VerifyMfaChallengeAsync(string challengeId, string code, string? clientInfo, string? ipAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> PrincipalHasPermissionAsync(CurrentPrincipalResponse principal, string organizationId, string environmentId, string permissionCode, string? resourceType, string? resourceId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyRoleApplicationService : IIamRoleApplicationService
    {
        public Task<PagedListResponse<RoleResponse>> ListRolesAsync(IamListQueryOptions options, CancellationToken cancellationToken)
        {
            _ = options;
            _ = cancellationToken;
            return Task.FromResult(new PagedListResponse<RoleResponse>(1, 20, 0, []));
        }

        public Task<RoleResponse> CreateRoleAsync(string? roleName, IReadOnlyList<string> permissionCodes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RoleResponse> PatchRolePermissionsAsync(string roleId, IReadOnlyList<string> permissionCodes, SecurityAuditContext? auditContext, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
