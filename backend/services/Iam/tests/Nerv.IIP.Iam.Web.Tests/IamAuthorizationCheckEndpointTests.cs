using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamAuthorizationCheckEndpointTests
{
    [Fact]
    public async Task Authorization_check_rejects_anonymous_callers_before_touching_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/internal/iam/v1/authorization/check",
            new AuthorizationCheckRequest("apphub.instances.read", "org-001", "env-dev", null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authorization_check_allows_seeded_admin_for_matching_organization_environment_and_permission()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var auth = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new LoginRequest("admin", "Admin123!"));
        var tokens = await ReadResponseDataAsync<AuthResponse>(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var response = await client.PostAsJsonAsync("/internal/iam/v1/authorization/check",
            new AuthorizationCheckRequest("apphub.instances.read", "org-001", "env-dev", "application-instance", "demo-api-001"));

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<AuthorizationCheckResponse>(response);
        Assert.True(body!.Allowed);
        Assert.Equal("user", body.PrincipalType);
        Assert.Equal("admin", body.LoginName);
        Assert.Null(body.DataScope);
    }

    [Fact]
    public async Task Authorization_check_denies_wrong_environment_even_when_permission_code_exists()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var auth = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new LoginRequest("admin", "Admin123!"));
        var tokens = await ReadResponseDataAsync<AuthResponse>(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var response = await client.PostAsJsonAsync("/internal/iam/v1/authorization/check",
            new AuthorizationCheckRequest("apphub.instances.read", "org-001", "env-prod", null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private static async Task<T> ReadResponseDataAsync<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success, envelope.Message);
        Assert.NotNull(envelope.Data);
        return envelope.Data;
    }
}
