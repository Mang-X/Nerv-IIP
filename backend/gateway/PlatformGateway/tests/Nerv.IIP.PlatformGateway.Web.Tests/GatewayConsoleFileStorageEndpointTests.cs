using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.FileStorage;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Files;
using Nerv.IIP.ServiceAuth;
using static Nerv.IIP.PlatformGateway.Web.Tests.ConsoleFileStorageTestFixtures;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

/// <summary>
/// SUT：Console 文件面的**端点**（路由、权限门、租户口径、响应面），经
/// <see cref="WebApplicationFactory{T}"/> 驱动，下游用 <see cref="FakeGatewayFileStorageClient"/> 替身。
/// 发往 FileStorage 的线上形状由 <see cref="GatewayFileStorageClientTests"/> 承担。
/// </summary>
public sealed class GatewayConsoleFileStorageEndpointTests
{
    [Fact]
    public async Task Create_upload_session_forwards_payload_and_requires_upload_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/files/upload-sessions");
        request.Content = JsonContent.Create(CreateUploadSessionRequest());

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<CreateUploadSessionResponse>(response);
        Assert.Equal("upload-session-001", body.UploadSessionId);
        Assert.Equal("/api/console/v1/files/tus/upload-session-001", body.Upload.Url);
        Assert.Equal("example.csv", files.LastCreateRequest!.FileName);
        Assert.Equal(GatewayPermissions.FilesUpload, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Create_upload_session_maps_console_request_to_contract_with_principal_org_env()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/files/upload-sessions");
        // 端点接收本地 console request（不含 org/env），映射到共享 contract 时添加 principal org/env
        request.Content = JsonContent.Create(new ConsoleCreateUploadSessionRequest(
            new OwnerReference("notification", "message", "msg-001"),
            "notification-attachment",
            "example.csv",
            "text/csv",
            42,
            null));

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<CreateUploadSessionResponse>(response);
        Assert.Equal("upload-session-001", body.UploadSessionId);
        // 验证转发给 FileStorage 的 contract request 正确填充了 principal org/env
        Assert.Equal("org-001", files.LastCreateRequest!.OrganizationId);
        Assert.Equal("env-dev", files.LastCreateRequest.EnvironmentId);
        Assert.Equal("example.csv", files.LastCreateRequest.FileName);
    }

    [Fact]
    public async Task Complete_upload_session_forwards_session_id_and_requires_upload_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/files/upload-sessions/upload-session-001/complete");
        request.Content = JsonContent.Create(new CompleteUploadSessionRequest("org-001", "env-dev", "notification-attachment"));

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<FileMetadataResponse>(response);
        Assert.Equal("file-001", body.FileId);
        Assert.Equal("upload-session-001", files.LastCompleteUploadSessionId);
        Assert.Equal(GatewayPermissions.FilesUpload, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Complete_upload_session_maps_console_request_to_contract_with_principal_org_env()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/files/upload-sessions/upload-session-001/complete");
        // 端点接收本地 console request（不含 org/env），映射到共享 contract 时添加 principal org/env
        request.Content = JsonContent.Create(new ConsoleCompleteUploadSessionRequest("notification-attachment"));

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<FileMetadataResponse>(response);
        Assert.Equal("file-001", body.FileId);
        // 验证转发给 FileStorage 的 contract request 正确填充了 principal org/env
        Assert.NotNull(files.LastCompleteRequest);
        Assert.Equal("org-001", files.LastCompleteRequest.OrganizationId);
        Assert.Equal("env-dev", files.LastCompleteRequest.EnvironmentId);
    }

    [Fact]
    public async Task Get_file_metadata_forwards_file_id_and_requires_read_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files/file-001");

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<FileMetadataResponse>(response);
        Assert.Equal("file-001", body.FileId);
        Assert.Equal("file-001", files.LastMetadataFileId);
        Assert.Equal(GatewayPermissions.FilesRead, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task List_files_forwards_filters_and_requires_read_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(
            HttpMethod.Get,
            "/api/console/v1/files?filePurpose=notification-attachment&uploaderId=user-001&createdFromUtc=2026-06-01T00%3A00%3A00Z&createdToUtc=2026-06-08T00%3A00%3A00Z&status=available&skip=10&take=20");
        AddTenantHeaders(request);

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<FileListResponse>(response);
        Assert.Equal(1, body.Total);
        Assert.Equal("file-001", Assert.Single(body.Items).FileId);
        Assert.NotNull(files.LastListRequest);
        Assert.Equal("org-001", files.LastListRequest.OrganizationId);
        Assert.Equal("env-dev", files.LastListRequest.EnvironmentId);
        Assert.Equal("notification-attachment", files.LastListRequest.FilePurpose);
        Assert.Equal("user-001", files.LastListRequest.UploaderId);
        Assert.Equal(DateTimeOffset.Parse("2026-06-01T00:00:00Z"), files.LastListRequest.CreatedFromUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-06-08T00:00:00Z"), files.LastListRequest.CreatedToUtc);
        Assert.Equal("available", files.LastListRequest.Status);
        Assert.Equal(10, files.LastListRequest.Skip);
        Assert.Equal(20, files.LastListRequest.Take);
        Assert.Equal(GatewayPermissions.FilesRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
        Assert.Equal("file", auth.LastRequirement.ResourceType);
    }

    [Fact]
    public async Task List_files_requires_explicit_tenant_headers()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<object>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Equal("X-Organization-Id and X-Environment-Id headers are required.", envelope.Message);
        Assert.Null(files.LastListRequest);
        Assert.Null(auth.LastRequirement);
    }

    [Fact]
    public async Task Get_file_storage_usage_forwards_scope_and_requires_read_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files/usage?filePurpose=application-package");
        AddTenantHeaders(request);

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await ReadResponseDataAsync<FileStorageUsageResponse>(response);
        Assert.Equal("org-001", body.OrganizationId);
        Assert.Equal("env-dev", body.EnvironmentId);
        Assert.Equal("application-package", body.FilePurpose);
        Assert.Equal(42, body.UsedBytes);
        Assert.NotNull(files.LastUsageRequest);
        Assert.Equal("org-001", files.LastUsageRequest.OrganizationId);
        Assert.Equal("env-dev", files.LastUsageRequest.EnvironmentId);
        Assert.Equal("application-package", files.LastUsageRequest.FilePurpose);
        Assert.Equal(GatewayPermissions.FilesRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("file-usage", auth.LastRequirement.ResourceType);
    }

    [Fact]
    public async Task Get_tus_offset_proxies_and_requires_upload_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Head, "/api/console/v1/files/tus/upload-session-001");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("1.0.0", response.Headers.GetValues("Tus-Resumable").Single());
        Assert.Equal("0", response.Headers.GetValues("Upload-Offset").Single());
        Assert.Equal("upload-session-001", files.LastTusHeadUploadSessionId);
        Assert.Equal("org-001", files.LastTusHeadOrganizationId);
        Assert.Equal("env-dev", files.LastTusHeadEnvironmentId);
        Assert.Equal(GatewayPermissions.FilesUpload, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task Patch_tus_upload_proxies_and_requires_upload_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Patch, "/api/console/v1/files/tus/upload-session-001");
        request.Content = new ByteArrayContent([1, 2, 3]);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Offset", "0");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("3", response.Headers.GetValues("Upload-Offset").Single());
        Assert.Equal("upload-session-001", files.LastTusPatchUploadSessionId);
        Assert.Equal("org-001", files.LastTusPatchOrganizationId);
        Assert.Equal("env-dev", files.LastTusPatchEnvironmentId);
        Assert.Equal(GatewayPermissions.FilesUpload, auth.LastRequirement!.PermissionCode);
    }

    [Fact]
    public async Task File_content_proxies_stream_and_requires_read_permission()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files/file-001/content");

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
        Assert.Equal("file-001", files.LastDownloadContentFileId);
        // 组织/环境取自 principal，不由调用方声明。
        Assert.Equal("org-001", files.LastDownloadContentOrganizationId);
        Assert.Equal("env-dev", files.LastDownloadContentEnvironmentId);
        // 最后一问是兑换那个码；两码都被问过由
        // File_content_route_asks_authorization_for_both_required_codes 承担。
        Assert.Equal(GatewayPermissions.FilesRead, auth.LastRequirement!.PermissionCode);
    }

    /// <summary>
    /// #3314 的**回归护栏**：本票点名的那两条历史路由不得被加回来。
    ///
    /// **本用例只探两个字面 URL，不承担「不存在任何以 grant id 为入参的路由」这个类级不变量**
    /// ——那是白名单式的覆盖，换个路由名就探不到（#3314 第 3 轮审核实测过）。类级不变量由两处承担：
    /// 契约面是 <c>GatewayOpenApiTests</c> 的三条判据（入参必须是 fileId / 不得含 download-grants /
    /// 文件面闭集，扫描面是整份文档），可表达性面是 <c>FileStorageDownstreamAddress</c>
    /// （代理入口不接受字符串）。
    ///
    /// 缺陷原状：本网关的 <c>GET /api/console/v1/files/download-grants/{downloadGrantId}/content</c>
    /// （门 <c>files.read</c>）与 BusinessGateway 的同形路由（门
    /// <c>business.engineering.documents.read</c>）代理到同一个下游；真栈实测两个方向都 200，
    /// IAM 对 user 主体只看权限码不看资源，FileStorage 的 grant 记录也没有归属字段。
    ///
    /// 会失败的具体输入：把那两条路由中的任意一条加回来，对应那一格不再是 404/405。
    /// 阴性对照：同一套桩走新形状的 fileId 路由必须仍然 200。
    /// </summary>
    [Fact]
    public async Task No_console_route_redeems_a_download_grant_id_supplied_by_the_caller()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        var client = factory.CreateClient();

        using (var redeem = AuthorizedRequest(
            HttpMethod.Get,
            "/api/console/v1/files/download-grants/download-grant-001/content"))
        {
            AddTenantHeaders(redeem);
            var response = await client.SendAsync(redeem);
            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"以 grant id 为入参的兑换面不得存在，实际 {(int)response.StatusCode}");
        }

        using (var issue = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/files/file-001/download-grants"))
        {
            issue.Content = JsonContent.Create(new { organizationId = "org-001", environmentId = "env-dev" });
            var response = await client.SendAsync(issue);
            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"把 grant id 交给调用方的签发面不得存在，实际 {(int)response.StatusCode}");
        }

        Assert.Null(files.LastDownloadContentFileId);

        // 阴性对照：新形状必须仍然通。
        using (var ok = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files/file-001/content"))
        {
            var response = await client.SendAsync(ok);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("hello", await response.Content.ReadAsStringAsync());
        }
    }

    /// <summary>
    /// #3314 第 1 轮审核 E2 的承担方：「grant id 不出网关进程」此前**只有契约形状**被钉住，
    /// 响应面零断言——审核把 grant URL 写进响应头，PG 一条都不红（head <c>7b025daf6</c> 上的读数，
    /// 当时 117 条）。
    ///
    /// 旧缺陷的实际形态恰恰是**响应字段**（旧 `DownloadGrantResponse.download.url` 里带
    /// `/download-grants/{id}/content`），不是路径模板。所以这里断言的是真正交给调用方的那一面：
    /// **字节响应的头与体都不得出现 `/download-grants/` 片段或 grant id 本身。**
    ///
    /// 会失败的具体输入：任何把 `downstreamUrl`（或从中截出的 grant id）写进响应头/响应体的改动。
    /// 阴性对照：下游真实回的内容 `hello` 必须原样到达，否则本断言会退化成「什么都不回也能过」。
    /// </summary>
    [Fact]
    public async Task File_content_response_leaks_neither_the_grant_id_nor_the_downstream_grant_url()
    {
        const string grantId = "dgr-leak-probe-3314";
        var handler = new RecordingHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/download-grants", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(GrantFor($"/api/files/v1/download-grants/{grantId}/content"))
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("hello") });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        await using var factory = PlatformGatewayTestHost.CreateFactory()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGatewayFileStorageClient>();
                services.AddSingleton<IGatewayFileStorageClient>(files);
                services.RemoveAll<IGatewayAuthorizationClient>();
                services.AddSingleton<IGatewayAuthorizationClient>(FakeGatewayAuthorizationClient.Allowed());
            }));
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files/file-001/content");

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        // 阴性对照：真实字节必须到达。
        Assert.Equal("hello", body);
        AssertNoGrantLeak(response, body, grantId);
    }

    /// <summary>
    /// #3314 第 2 轮审核建议：正向用例喂的是**不带头部**的请求，它区分不了「读 principal」
    /// 与「读头部、缺失时回落到 principal」——那是等价输入。这里喂一个与 principal **冲突**的
    /// 头部，断言下游收到的仍是 principal 的值。
    ///
    /// 会失败的具体输入：任何改成从 `Request.Headers` 取租户范围（或取头部、缺失才回落）的实现。
    /// 取值刻意与 principal 的 `org-001` / `env-dev` 互不相等，否则断言两边同值、零鉴别力。
    /// </summary>
    [Theory]
    [InlineData("org-attacker", "env-dev")]
    [InlineData("org-001", "env-attacker")]
    public async Task File_content_route_ignores_caller_supplied_tenant_headers_and_uses_the_principal(
        string headerOrganizationId,
        string headerEnvironmentId)
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files/file-001/content");
        request.Headers.Add("X-Organization-Id", headerOrganizationId);
        request.Headers.Add("X-Environment-Id", headerEnvironmentId);

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal("org-001", files.LastDownloadContentOrganizationId);
        Assert.Equal("env-dev", files.LastDownloadContentEnvironmentId);
    }

    /// <summary>
    /// #3314 第 1 轮审核 P1：本路由把「签发 + 兑换」两跳并成一跳，**所需权限码不得因此收窄**。
    /// 合并前自助取字节要同时持 <c>files.download-grants.create</c>（签发）与 <c>files.read</c>（兑换）；
    /// 只校验其中一个，等于让只持另一个的角色新获得字节能力。
    ///
    /// 会失败的具体输入：主体只持其中**一个**码 —— 两种缺法各一格，任一格 200 都说明门收窄了。
    /// 阴性对照在 <see cref="File_content_proxies_stream_and_requires_read_permission"/>：
    /// 两码齐全时必须 200，否则本用例会退化成「什么都拒也能过」。
    /// </summary>
    [Theory]
    [InlineData(GatewayPermissions.FilesRead)]
    [InlineData(GatewayPermissions.FilesDownloadGrantsCreate)]
    public async Task File_content_route_rejects_a_principal_holding_only_one_of_the_two_required_codes(
        string onlyHeldCode)
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.AllowOnly(onlyHeldCode);
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files/file-001/content");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // 拒绝发生在网关的权限门，FileStorage 一发都收不到。
        Assert.Null(files.LastDownloadContentFileId);
    }

    /// <summary>
    /// 两个码都必须被真的问过 —— 否则「要求两个码」可以退化成「声明了两个、只校验第一个」。
    /// </summary>
    [Fact]
    public async Task File_content_route_asks_authorization_for_both_required_codes()
    {
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(files, auth);
        using var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files/file-001/content");

        var response = await factory.CreateClient().SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Equal(
            new[] { GatewayPermissions.FilesDownloadGrantsCreate, GatewayPermissions.FilesRead },
            auth.Requirements.Select(requirement => requirement.PermissionCode).ToArray());
    }

    [Fact]
    public async Task File_storage_downstream_validation_error_preserves_status_and_message()
    {
        var files = new FakeGatewayFileStorageClient
        {
            ExceptionToThrow = new GatewayAuthException(HttpStatusCode.BadRequest, "upload session request is invalid")
        };
        await using var factory = CreateFactory(files);
        using var request = AuthorizedRequest(HttpMethod.Post, "/api/console/v1/files/upload-sessions");
        request.Content = JsonContent.Create(CreateUploadSessionRequest());

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<object>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Equal(400, envelope.Code);
        Assert.Equal("upload session request is invalid", envelope.Message);
    }

    [Fact]
    public async Task ListFiles_OwnerId_EquivalentToUploaderId_SameFilterParameter()
    {
        // Verify that both ownerId and uploaderId parameters are correctly transmitted
        // to downstream, proving they filter the same owner_id column (expand phase equivalence).
        var files = new FakeGatewayFileStorageClient();
        var auth = FakeGatewayAuthorizationClient.Allowed();
        using var factory = CreateFactory(files, auth);
        using var client = factory.CreateClient();

        // Request with uploaderId (legacy parameter)
        using (var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files?uploaderId=user-123"))
        {
            AddTenantHeaders(request);
            var uploadByIdResponse = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, uploadByIdResponse.StatusCode);
            Assert.NotNull(files.LastListRequest);
            Assert.Equal("user-123", files.LastListRequest.UploaderId);
            Assert.Null(files.LastListRequest.OwnerId);
        }

        // Request with ownerId (new parameter)
        using (var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files?ownerId=user-123"))
        {
            AddTenantHeaders(request);
            var ownByIdResponse = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, ownByIdResponse.StatusCode);
            Assert.NotNull(files.LastListRequest);
            Assert.Null(files.LastListRequest.UploaderId);
            Assert.Equal("user-123", files.LastListRequest.OwnerId);
        }

        // Request with both parameters (service layer applies independent filters for each)
        using (var request = AuthorizedRequest(HttpMethod.Get, "/api/console/v1/files?uploaderId=user-old&ownerId=user-new"))
        {
            AddTenantHeaders(request);
            var bothResponse = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, bothResponse.StatusCode);
            Assert.NotNull(files.LastListRequest);
            Assert.Equal("user-old", files.LastListRequest.UploaderId);
            Assert.Equal("user-new", files.LastListRequest.OwnerId);
        }
    }
}
