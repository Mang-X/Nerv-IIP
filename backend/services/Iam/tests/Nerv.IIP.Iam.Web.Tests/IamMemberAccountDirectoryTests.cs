using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Iam.Web.Tests;

/// <summary>
/// <c>/internal/iam/v1/member-accounts</c>：业务侧「关联登录账号」的候选与核验读面（#3924）。
/// 只列在指定组织/环境里有成员关系的账号，默认只列启用账号。
/// </summary>
public sealed class IamMemberAccountDirectoryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string InternalToken = "local-internal-service-token";
    private readonly HttpClient _client;

    public IamMemberAccountDirectoryTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Lists_only_member_accounts_of_the_requested_organization_environment()
    {
        var member = await CreateUserAsync("member-acct-a", assignMembership: true);
        var outsider = await CreateUserAsync("member-acct-outsider", assignMembership: false);
        var disabledMember = await CreateUserAsync("member-acct-disabled", assignMembership: true);
        await AsAdminAsync(() => _client.PostAsync($"/api/iam/v1/users/{disabledMember}/disable", null));

        var enabledOnly = await ListAsync("organizationId=org-001&environmentId=env-dev&keyword=member-acct&pageIndex=1&pageSize=50");
        Assert.Equal([member], enabledOnly.Items.Select(x => x.UserId));
        var account = Assert.Single(enabledOnly.Items);
        Assert.Equal("member-acct-a", account.LoginName);
        Assert.True(account.Enabled);
        Assert.Equal(1, enabledOnly.TotalCount);

        var withDisabled = await ListAsync("organizationId=org-001&environmentId=env-dev&keyword=member-acct&includeDisabled=true&pageIndex=1&pageSize=50");
        Assert.Equal(
            new[] { member, disabledMember }.Order(StringComparer.Ordinal),
            withDisabled.Items.Select(x => x.UserId).Order(StringComparer.Ordinal));
        Assert.False(withDisabled.Items.Single(x => x.UserId == disabledMember).Enabled);
        Assert.DoesNotContain(withDisabled.Items, x => x.UserId == outsider);

        var otherEnvironment = await ListAsync("organizationId=org-001&environmentId=env-other&keyword=member-acct&includeDisabled=true&pageIndex=1&pageSize=50");
        Assert.Empty(otherEnvironment.Items);
        Assert.Equal(0, otherEnvironment.TotalCount);
    }

    [Fact]
    public async Task Filters_by_user_id_batch_and_drops_non_members()
    {
        var member = await CreateUserAsync("member-acct-batch", assignMembership: true);
        var outsider = await CreateUserAsync("member-acct-batch-outsider", assignMembership: false);

        var page = await ListAsync(
            $"organizationId=org-001&environmentId=env-dev&userIds={member}&userIds={outsider}&userIds=user-admin&pageIndex=1&pageSize=50");

        Assert.Equal(
            new[] { member, "user-admin" }.Order(StringComparer.Ordinal),
            page.Items.Select(x => x.UserId).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("environmentId=env-dev")]
    [InlineData("organizationId=org-001")]
    [InlineData("organizationId=org-001&environmentId=env-dev&pageSize=101")]
    [InlineData("organizationId=org-001&environmentId=env-dev&pageIndex=0")]
    public async Task Rejects_requests_without_scope_or_with_invalid_page(string query)
    {
        using var request = InternalRequest($"/internal/iam/v1/member-accounts?{query}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_more_than_one_hundred_user_ids()
    {
        var ids = string.Join('&', Enumerable.Range(1, 101).Select(i => $"userIds=user-{i}"));
        using var request = InternalRequest($"/internal/iam/v1/member-accounts?organizationId=org-001&environmentId=env-dev&{ids}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Requires_internal_service_identity()
    {
        var anonymous = await _client.GetAsync("/internal/iam/v1/member-accounts?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var admin = await LoginAsync("admin", "Admin123!");
        using var userRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/internal/iam/v1/member-accounts?organizationId=org-001&environmentId=env-dev");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var asUser = await _client.SendAsync(userRequest);
        Assert.NotEqual(HttpStatusCode.OK, asUser.StatusCode);
    }

    private async Task<string> CreateUserAsync(string loginName, bool assignMembership)
    {
        var userId = string.Empty;
        await AsAdminAsync(async () =>
        {
            var created = await _client.PostAsJsonAsync(
                "/api/iam/v1/users",
                new { loginName, email = $"{loginName}@nerv-iip.local", password = "Operator123!" });
            created.EnsureSuccessStatusCode();
            userId = (await created.Content.ReadFromJsonAsync<ResponseDataEnvelope<CreatedUser>>())!.Data!.UserId;
            if (assignMembership)
            {
                var assign = await _client.PutAsJsonAsync(
                    $"/api/iam/v1/users/{userId}/membership",
                    new { roleIds = new[] { "role-erp-sales" } });
                assign.EnsureSuccessStatusCode();
            }

            return created;
        });
        return userId;
    }

    private async Task AsAdminAsync(Func<Task<HttpResponseMessage>> action)
    {
        var admin = await LoginAsync("admin", "Admin123!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        try
        {
            var response = await action();
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            _client.DefaultRequestHeaders.Authorization = null;
        }
    }

    private async Task<MemberAccountPage> ListAsync(string query)
    {
        using var request = InternalRequest($"/internal/iam/v1/member-accounts?{query}");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<MemberAccountPage>>();
        Assert.NotNull(envelope?.Data);
        return envelope.Data;
    }

    private static HttpRequestMessage InternalRequest(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", InternalToken);
        return request;
    }

    private async Task<string> LoginAsync(string loginName, string password)
    {
        var login = await _client.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName, password });
        login.EnsureSuccessStatusCode();
        var envelope = await login.Content.ReadFromJsonAsync<ResponseDataEnvelope<AuthResponse>>();
        Assert.NotNull(envelope?.Data);
        return envelope.Data.AccessToken;
    }

    private sealed record AuthResponse(string AccessToken);
    private sealed record CreatedUser(string UserId);
    private sealed record MemberAccount(string UserId, string LoginName, string? DisplayName, bool Enabled);
    private sealed record MemberAccountPage(int PageIndex, int PageSize, int TotalCount, IReadOnlyList<MemberAccount> Items);
    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
