using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayAuthorizationTests
{
    [Fact]
    public async Task Business_console_endpoint_requires_user_authentication()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal(0, auth.CallCount);
    }

    [Fact]
    public async Task Business_console_endpoint_returns_forbidden_when_iam_denies_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, auth.CallCount);
        Assert.Equal(BusinessGatewayPermissions.MasterDataProductsRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
    }

    [Fact]
    public async Task Business_console_endpoint_returns_empty_resource_list_when_permission_is_allowed()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetProperty("total").GetInt32());
        Assert.Empty(data.GetProperty("resources").EnumerateArray());
        Assert.Equal(1, auth.CallCount);
    }

    [Fact]
    public async Task Business_console_endpoint_rejects_context_mismatch_before_permission_check()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken(environmentId: "env-prod"));

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, auth.CallCount);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeBusinessGatewayAuthorizationClient auth) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
            });
        });
}

internal sealed class FakeBusinessGatewayAuthorizationClient(bool allowed) : IBusinessGatewayAuthorizationClient
{
    public int CallCount { get; private set; }

    public BusinessGatewayPermissionRequirement? LastRequirement { get; private set; }

    public static FakeBusinessGatewayAuthorizationClient Allowed() => new(true);

    public static FakeBusinessGatewayAuthorizationClient Forbidden() => new(false);

    public Task<BusinessGatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        BusinessGatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequirement = requirement;
        return Task.FromResult(allowed
            ? BusinessGatewayAuthorizationResult.Allowed("user-admin", "user", "admin")
            : BusinessGatewayAuthorizationResult.Forbidden("forbidden"));
    }
}
