using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// 员工「关联登录账号」（#3924）的网关一侧：账号候选目录、新建员工前核验、员工名册补登录名。
/// </summary>
public sealed class BusinessConsoleWorkerLoginAccountTests
{
    private const string AccountNotLinkableMessage = "所选登录账号不存在、已停用或不属于当前组织，请重新选择。";

    [Fact]
    public async Task Login_account_directory_lists_enabled_member_accounts_for_organization_wide_managers()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-001", BusinessGatewayPermissions.MasterDataResourcesManage, organizationWide: true),
        ]);
        var iamHandler = new RecordingJsonHandler(MemberAccountsPayload(
            pageIndex: 1,
            pageSize: 20,
            total: 1,
            ("user-019a", "zhangsan", "张三", true)));
        await using var lease = LeaseHost(auth, iamHandler);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/login-account?organizationId=org-001&environmentId=env-dev&keyword=zhang");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MasterDataResourcesManage, auth.LastRequirement!.PermissionCode);
        var request = Assert.Single(iamHandler.Requests);
        Assert.Equal("/internal/iam/v1/member-accounts", request.Path);
        Assert.Equal("internal-token", request.BearerToken);
        Assert.Contains("organizationId=org-001", request.Query, StringComparison.Ordinal);
        Assert.Contains("environmentId=env-dev", request.Query, StringComparison.Ordinal);
        Assert.Contains("keyword=zhang", request.Query, StringComparison.Ordinal);
        // 候选只列启用账号：不得带 includeDisabled。
        Assert.DoesNotContain("includeDisabled", request.Query, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("login-account", data.GetProperty("directoryType").GetString());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("user-019a", item.GetProperty("id").GetString());
        Assert.Equal("张三", item.GetProperty("displayName").GetString());
        Assert.Equal("zhangsan", item.GetProperty("code").GetString());
        Assert.Equal("iam", item.GetProperty("sourceService").GetString());
    }

    // 账号名单是组织级人事事实：只持有受限范围（self / 车间）授权的主体不能借「目录无范围维度」读到整份名单。
    [Theory]
    [InlineData("self", "user-leader")]
    [InlineData("workshop", "WS-A")]
    public async Task Login_account_directory_rejects_principals_without_organization_wide_grant(string scopeKind, string scopeId)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant(scopeKind, scopeId, BusinessGatewayPermissions.MasterDataResourcesManage),
        ]);
        var iamHandler = new RecordingJsonHandler(MemberAccountsPayload(1, 20, 0));
        await using var lease = LeaseHost(auth, iamHandler);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/login-account?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(iamHandler.Requests);
    }

    [Fact]
    public async Task Login_account_directory_treats_disabled_account_from_owner_as_invalid_response()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
        [
            Grant("organization", "org-001", BusinessGatewayPermissions.MasterDataResourcesManage, organizationWide: true),
        ]);
        var iamHandler = new RecordingJsonHandler(MemberAccountsPayload(1, 20, 1, ("user-off", "off", null, false)));
        await using var lease = LeaseHost(auth, iamHandler);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/directories/login-account?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Create_worker_verifies_selected_account_with_iam_before_forwarding()
    {
        var iamHandler = new RecordingJsonHandler(MemberAccountsPayload(1, 1, 1, ("user-019a", "zhangsan", null, true)));
        var masterData = new RecordingMasterDataClient();
        await using var lease = LeaseHost(FakeBusinessGatewayAuthorizationClient.Allowed(), iamHandler, masterData);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync("/api/business-console/v1/master-data/workers", CreateWorkerBody(" user-019a "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(iamHandler.Requests);
        Assert.Equal("/internal/iam/v1/member-accounts", request.Path);
        Assert.Contains("userIds=user-019a", request.Query, StringComparison.Ordinal);
        Assert.Contains("organizationId=org-001", request.Query, StringComparison.Ordinal);
        Assert.Contains("environmentId=env-dev", request.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("includeDisabled", request.Query, StringComparison.Ordinal);
        Assert.Equal("user-019a", masterData.LastCreateWorkerRequest!.UserId);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("other-account")]
    [InlineData("disabled")]
    public async Task Create_worker_rejects_account_that_is_not_an_enabled_member(string iamAnswer)
    {
        var payload = iamAnswer switch
        {
            "none" => MemberAccountsPayload(1, 1, 0),
            "other-account" => MemberAccountsPayload(1, 1, 1, ("user-other", "other", null, true)),
            _ => MemberAccountsPayload(1, 1, 1, ("user-019a", "zhangsan", null, false)),
        };
        var iamHandler = new RecordingJsonHandler(payload);
        var masterData = new RecordingMasterDataClient();
        await using var lease = LeaseHost(FakeBusinessGatewayAuthorizationClient.Allowed(), iamHandler, masterData);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync("/api/business-console/v1/master-data/workers", CreateWorkerBody("user-019a"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(AccountNotLinkableMessage, document.RootElement.GetProperty("message").GetString());
        Assert.Null(masterData.LastCreateWorkerRequest);
    }

    [Fact]
    public async Task Create_worker_without_account_does_not_call_iam()
    {
        var iamHandler = new RecordingJsonHandler(MemberAccountsPayload(1, 1, 0));
        var masterData = new RecordingMasterDataClient();
        await using var lease = LeaseHost(FakeBusinessGatewayAuthorizationClient.Allowed(), iamHandler, masterData);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync("/api/business-console/v1/master-data/workers", CreateWorkerBody(null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(iamHandler.Requests);
        Assert.Null(masterData.LastCreateWorkerRequest!.UserId);
    }

    private const string RosterUrl =
        "/api/business-console/v1/master-data/workers?organizationId=org-001&environmentId=env-dev&pageIndex=1&pageSize=20&includeDisabled=true&includeLoginNames=true";

    // 登录名的可见范围按最小权限裁定（#3924 审核阻断 2）：只有员工维护权 + 组织级授权才看得到。
    [Fact]
    public async Task Worker_roster_shows_login_names_to_organization_wide_managers()
    {
        var iamHandler = new RecordingJsonHandler(MemberAccountsPayload(1, 2, 1, ("user-019a", "zhangsan", "张三", false)));
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants: [ManageGrant()]);
        await using var lease = LeaseHost(auth, iamHandler, RosterMasterData());
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(RosterUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 名册读权限照旧；是否可见登录名再按员工维护权单独核一次，并要回 scope grants。
        Assert.Contains(auth.Requirements, r => r.PermissionCode == BusinessGatewayPermissions.MasterDataResourcesRead);
        Assert.Contains(auth.Requirements, r =>
            r.PermissionCode == BusinessGatewayPermissions.MasterDataResourcesManage && r.IncludePrincipalContext);
        var request = Assert.Single(iamHandler.Requests);
        Assert.Contains("userIds=user-019a", request.Query, StringComparison.Ordinal);
        Assert.Contains("userIds=EMP-0002", request.Query, StringComparison.Ordinal);
        // 名册要如实显示已停用账号的登录名。
        Assert.Contains("includeDisabled=true", request.Query, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("loginNamesVisible").GetBoolean());
        var items = data.GetProperty("items");
        Assert.Equal("zhangsan", items[0].GetProperty("loginName").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("loginName").ValueKind);
    }

    [Theory]
    [InlineData("read-only")]
    [InlineData("manage-restricted-scope")]
    public async Task Worker_roster_hides_login_names_without_organization_wide_manage(string principal)
    {
        var iamHandler = new RecordingJsonHandler(MemberAccountsPayload(1, 2, 1, ("user-019a", "zhangsan", "张三", true)));
        var auth = principal == "read-only"
            ? FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MasterDataResourcesRead)
            : FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants:
            [
                Grant("workshop", "WS-A", BusinessGatewayPermissions.MasterDataResourcesManage),
            ]);
        await using var lease = LeaseHost(auth, iamHandler, RosterMasterData());
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(RosterUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(iamHandler.Requests);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("loginNamesVisible").GetBoolean());
        Assert.All(data.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal(JsonValueKind.Null, item.GetProperty("loginName").ValueKind));
    }

    // 各处员工选择器与 PDA 首页共用这个读接口：不显式请求就不多一跳 IAM、也不做员工维护权核验。
    [Fact]
    public async Task Worker_directory_without_login_name_request_never_calls_iam()
    {
        var iamHandler = new RecordingJsonHandler(MemberAccountsPayload(1, 1, 0));
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants: [ManageGrant()]);
        await using var lease = LeaseHost(auth, iamHandler, RosterMasterData());
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(
            "/api/business-console/v1/master-data/workers?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(iamHandler.Requests);
        Assert.DoesNotContain(auth.Requirements, r => r.PermissionCode == BusinessGatewayPermissions.MasterDataResourcesManage);
    }

    // IAM 读失败不拖垮名册：照常返回员工，但不标记登录名可见（界面显示「—」，不误报「未关联」）。
    [Fact]
    public async Task Worker_roster_degrades_when_iam_is_unavailable()
    {
        var iamHandler = new RecordingJsonHandler("{}", HttpStatusCode.ServiceUnavailable);
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed(scopeGrants: [ManageGrant()]);
        await using var lease = LeaseHost(auth, iamHandler, RosterMasterData());
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync(RosterUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(iamHandler.Requests);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("loginNamesVisible").GetBoolean());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
    }

    private static RecordingMasterDataClient RosterMasterData() => new()
    {
        ListWorkersHandler = (request, _) => Task.FromResult(new BusinessConsoleWorkerDirectoryResponse(
            request.PageIndex,
            request.PageSize,
            2,
            [
                Worker("user-019a", "EMP-0001", "张三"),
                Worker("EMP-0002", "EMP-0002", "李四"),
            ])),
    };

    private static AuthorizationScopeGrant ManageGrant() =>
        Grant("organization", "org-001", BusinessGatewayPermissions.MasterDataResourcesManage, organizationWide: true);

    private static object CreateWorkerBody(string? userId) => new
    {
        organizationId = "org-001",
        environmentId = "env-dev",
        code = (string?)null,
        name = "张三",
        userId,
        departmentCode = (string?)null,
        jobTitle = "库管",
        employmentStatus = "active",
        phone = (string?)null,
    };

    private static BusinessConsoleWorkerDirectoryItem Worker(string userId, string employeeNo, string name) =>
        new(userId, employeeNo, name, null, null, null, "active", null, true, [], [], "1");

    private static string MemberAccountsPayload(
        int pageIndex,
        int pageSize,
        int total,
        params (string UserId, string LoginName, string? DisplayName, bool Enabled)[] accounts) =>
        JsonSerializer.Serialize(new
        {
            success = true,
            message = string.Empty,
            code = 0,
            data = new
            {
                pageIndex,
                pageSize,
                totalCount = total,
                items = accounts.Select(account => new
                {
                    userId = account.UserId,
                    loginName = account.LoginName,
                    displayName = account.DisplayName,
                    enabled = account.Enabled,
                }),
            },
        });

    private static BusinessGatewayTestHostLease LeaseHost(
        IBusinessGatewayAuthorizationClient auth,
        RecordingJsonHandler iamHandler,
        IBusinessMasterDataClient? masterData = null) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessIamAccountDirectoryClient>();
            services.AddSingleton<IBusinessIamAccountDirectoryClient>(new HttpBusinessIamAccountDirectoryClient(
                new HttpClient(iamHandler) { BaseAddress = new Uri("http://iam.local") }));
            if (masterData is not null)
            {
                services.RemoveAll<IBusinessMasterDataClient>();
                services.AddSingleton(masterData);
            }

            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-token"));
        });

    private static AuthorizationScopeGrant Grant(
        string scopeKind,
        string scopeId,
        string permissionCode,
        bool organizationWide = false) =>
        new("role", "role-worker-registrar", scopeKind, scopeId, [permissionCode], organizationWide);

    private sealed record RecordedRequest(string Path, string Query, string? BearerToken);

    private sealed class RecordingJsonHandler(string payload, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        private readonly List<RecordedRequest> requests = [];

        public IReadOnlyList<RecordedRequest> Requests => requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                Uri.UnescapeDataString(request.RequestUri.Query),
                request.Headers.Authorization?.Parameter));
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }
}
