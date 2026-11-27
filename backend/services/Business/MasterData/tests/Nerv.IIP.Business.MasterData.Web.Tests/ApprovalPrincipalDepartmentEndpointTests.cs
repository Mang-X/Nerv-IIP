using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class ApprovalPrincipalDepartmentEndpointTests
{
    [Theory]
    [InlineData("approval", "org-001", "env-dev", "user", HttpStatusCode.OK)]
    [InlineData("approval", "org-other", "env-dev", "user", HttpStatusCode.Forbidden)]
    [InlineData("approval", "org-001", "env-other", "user", HttpStatusCode.Forbidden)]
    [InlineData("gateway", "org-001", "env-dev", "user", HttpStatusCode.Forbidden)]
    [InlineData("ungranted", "org-001", "env-dev", "user", HttpStatusCode.Forbidden)]
    [InlineData("shared", "org-001", "env-dev", "user", HttpStatusCode.Unauthorized)]
    [InlineData("approval", "org-001", "env-dev", null, HttpStatusCode.Unauthorized)]
    [InlineData("approval", "org-001", "env-dev", "unverified", HttpStatusCode.Unauthorized)]
    [InlineData("approval", "org-001", "env-dev", "external-client", HttpStatusCode.Forbidden)]
    [InlineData("approval", "org-001", "env-dev", "other-membership", HttpStatusCode.Forbidden)]
    [InlineData("approval", "org-001", "env-dev", "no-membership", HttpStatusCode.Forbidden)]
    [InlineData("approval", "org-001", "env-dev", "not-permitted", HttpStatusCode.Forbidden)]
    [InlineData("approval", "org-001", "env-dev", "iam-unavailable", HttpStatusCode.ServiceUnavailable)]
    [InlineData("disabled", "org-001", "env-dev", "user", HttpStatusCode.Unauthorized)]
    public async Task Department_requires_scoped_approval_and_verified_user_membership(
        string caller, string organization, string environment, string? credential, HttpStatusCode expected)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Iam:Jwt:JwksJson", IamJwtFixture.PublicJwksJson);
            builder.UseSetting("InternalService:BearerToken", "synthetic-shared-token");
            builder.UseSetting("MasterData:Seed:Enabled", "false");
            builder.UseSetting("ConnectionStrings:PostgreSQL", "Host=unused;Database=synthetic");
            builder.UseSetting("Iam:BaseUrl", "http://iam.test");
            if (caller != "disabled")
            {
                ConfigureCaller(builder, 0, "approval", "business-approval", "internal.masterdata.approval-department.read");
                ConfigureCaller(builder, 1, "gateway", "business-gateway", "internal.masterdata.approval-department.read");
                ConfigureCaller(builder, 2, "ungranted", "business-approval", "business.masterdata.resources.read");
            }
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient("MasterDataApprovalDepartmentIam")
                    .ConfigurePrimaryHttpMessageHandler(() => new SyntheticIam());
                services.AddScoped<IRequestHandler<GetApprovalPrincipalDepartmentQuery, ApprovalPrincipalDepartmentResponse>,
                    SyntheticDepartment>();
            });
        });

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "/internal/master-data/v1/approval-principal-department")
        {
            Content = JsonContent.Create(new { organizationId = organization, environmentId = environment, userId = "ignored" }),
        };
        var callerToken = caller == "disabled" ? "shared" : caller;
        request.Headers.Authorization = new("Bearer", $"synthetic-{callerToken}-token");
        if (credential is not null)
        {
            request.Headers.Add("X-Nerv-IIP-User-Authorization", $"Bearer {credential}");
        }

        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var department = Assert.Single(result.GetProperty("data").EnumerateObject());
            Assert.Equal("departmentId", department.Name);
            Assert.Equal("DEPT-VERIFIED", department.Value.GetString());
        }
    }

    private static void ConfigureCaller(IWebHostBuilder builder, int index, string name, string subject, string permission)
    {
        var prefix = $"MasterData:ApprovalDepartment:ScopedCallers:Profiles:{index}";
        builder.UseSetting($"{prefix}:Name", name);
        builder.UseSetting($"{prefix}:BearerToken", $"synthetic-{name}-token");
        builder.UseSetting($"{prefix}:Subject", subject);
        builder.UseSetting($"{prefix}:OrganizationId", "org-001");
        builder.UseSetting($"{prefix}:EnvironmentId", "env-dev");
        builder.UseSetting($"{prefix}:Permissions:0", permission);
    }

    private sealed class SyntheticIam : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/internal/iam/v1/authorization/check", request.RequestUri!.AbsolutePath);
            var check = await request.Content!.ReadFromJsonAsync<AuthorizationCheckRequest>(cancellationToken);
            Assert.NotNull(check);
            Assert.True(check.IncludeSelectedMembership);
            Assert.Equal("business.approvals.manage", check.PermissionCode);
            Assert.Equal("org-001", check.OrganizationId);
            Assert.Equal("env-dev", check.EnvironmentId);
            var bearer = request.Headers.Authorization;
            Assert.Equal("Bearer", bearer?.Scheme);
            HttpStatusCode? denied = bearer!.Parameter switch
            {
                "unverified" => HttpStatusCode.Unauthorized,
                "not-permitted" => HttpStatusCode.Forbidden,
                "iam-unavailable" => HttpStatusCode.ServiceUnavailable,
                _ => null,
            };
            if (denied is { } deniedStatus)
            {
                return new(deniedStatus);
            }

            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    success = true,
                    data = new
                    {
                        allowed = true,
                        principalId = "iam-user",
                        principalType = bearer.Parameter == "external-client" ? "external-client" : "user",
                        selectedMembership = bearer.Parameter == "no-membership" ? null : new
                        {
                            organizationId = "org-001",
                            environmentId = bearer.Parameter == "other-membership" ? "env-other" : "env-dev",
                            roleIds = new[] { "approver" },
                        },
                    },
                }),
            };
        }
    }

    private sealed class SyntheticDepartment
        : IRequestHandler<GetApprovalPrincipalDepartmentQuery, ApprovalPrincipalDepartmentResponse>
    {
        public Task<ApprovalPrincipalDepartmentResponse> Handle(GetApprovalPrincipalDepartmentQuery request, CancellationToken cancellationToken)
        {
            Assert.Equal(new GetApprovalPrincipalDepartmentQuery("org-001", "env-dev", "iam-user"), request);
            return Task.FromResult(new ApprovalPrincipalDepartmentResponse("DEPT-VERIFIED"));
        }
    }
}
