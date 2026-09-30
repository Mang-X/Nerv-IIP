using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamProductionPlannerMembersTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public IamProductionPlannerMembersTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Lists_only_active_planners_in_requested_organization_environment()
    {
        var adminToken = await LoginAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var planner = await CreateUserAsync("planner-member", ["role-production-planner"]);
        var secondPlanner = await CreateUserAsync("planner-member-second", ["role-production-planner"]);
        await CreateUserAsync("planner-nonmember", ["role-erp-sales"]);
        var disabled = await CreateUserAsync("planner-disabled", ["role-production-planner"]);
        var expired = await CreateUserAsync("planner-expired", ["role-production-planner"], DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/iam/v1/users/{disabled}/disable", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        var result = await ListAsync("org-001", "env-dev");
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { planner, secondPlanner }.Order(StringComparer.Ordinal), result.Items);
        Assert.Equal([result.Items[0]], (await ListAsync("org-001", "env-dev", pageIndex: 1, pageSize: 1)).Items);
        Assert.Equal([result.Items[1]], (await ListAsync("org-001", "env-dev", pageIndex: 2, pageSize: 1)).Items);
        Assert.Empty((await ListAsync("org-other", "env-dev")).Items);
        Assert.Empty((await ListAsync("org-001", "env-other")).Items);
        Assert.DoesNotContain(expired, result.Items);
    }

    [Fact]
    public async Task Requires_internal_service_identity()
    {
        var response = await client.GetAsync("/internal/iam/v1/production-planner-members?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> CreateUserAsync(string loginName, string[] roleIds, DateTimeOffset? expiresAt = null)
    {
        var created = await client.PostAsJsonAsync("/api/iam/v1/users", new
        {
            loginName,
            email = $"{loginName}@nerv-iip.local",
            password = "Operator123!",
            accountExpiresAtUtc = expiresAt,
        });
        created.EnsureSuccessStatusCode();
        var userId = (await ReadAsync<CreatedUser>(created)).UserId;
        var assign = await client.PutAsJsonAsync($"/api/iam/v1/users/{userId}/membership", new { roleIds });
        assign.EnsureSuccessStatusCode();
        return userId;
    }

    private async Task<MemberIdsPage> ListAsync(string organizationId, string environmentId, int pageIndex = 1, int pageSize = 20)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/internal/iam/v1/production-planner-members?organizationId={organizationId}&environmentId={environmentId}&pageIndex={pageIndex}&pageSize={pageSize}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "local-internal-service-token");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<MemberIdsPage>(response);
    }

    private async Task<string> LoginAsync()
    {
        var response = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName = "admin", password = "Admin123!" });
        response.EnsureSuccessStatusCode();
        return (await ReadAsync<AuthResponse>(response)).AccessToken;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) where T : class
    {
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>();
        return Assert.IsType<T>(envelope?.Data);
    }

    private sealed record AuthResponse(string AccessToken);
    private sealed record CreatedUser(string UserId);
    private sealed record MemberIdsPage(int PageIndex, int PageSize, int TotalCount, IReadOnlyList<string> Items);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
