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

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayConsoleFileStorageTests
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
    public async Task File_storage_http_client_lists_files_with_filters_and_internal_token()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new FileListResponse(
                1,
                [FileMetadata()]))
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var response = await files.ListFilesAsync(
            new ListFilesRequest(
                "org-001",
                "env-dev",
                "notification-attachment",
                "user-001",
                null,
                DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-06-08T00:00:00Z"),
                "available",
                10,
                20),
            CancellationToken.None);

        Assert.Equal(1, response.Total);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/api/files/v1/files?organizationId=org-001&environmentId=env-dev&filePurpose=notification-attachment&uploaderId=user-001&createdFromUtc=2026-06-01T00%3A00%3A00.0000000%2B00%3A00&createdToUtc=2026-06-08T00%3A00%3A00.0000000%2B00%3A00&status=available&skip=10&take=20",
            request.RequestUri.PathAndQuery);
        Assert.Equal("Bearer", request.Authorization!.Scheme);
        Assert.Equal("internal-test-token", request.Authorization.Parameter);
    }

    [Fact]
    public async Task File_storage_http_client_tus_head_forwards_organization_and_environment_headers()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var httpContext = new DefaultHttpContext();

        await files.ProxyTusHeadAsync("upload-session-001", "org-001", "env-dev", httpContext.Response, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Head, request.Method);
        Assert.Equal("/api/files/v1/tus/upload-session-001", request.RequestUri!.AbsolutePath);
        Assert.Equal("org-001", request.Headers["X-Organization-Id"]);
        Assert.Equal("env-dev", request.Headers["X-Environment-Id"]);
        Assert.Equal("Bearer internal-test-token", request.Authorization!.ToString());
    }

    [Fact]
    public async Task File_storage_http_client_tus_patch_forwards_organization_and_environment_headers()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "PATCH";
        httpContext.Request.Body = new MemoryStream([1, 2, 3]);

        await files.ProxyTusPatchAsync("upload-session-001", "org-001", "env-dev", httpContext.Request, httpContext.Response, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Equal("/api/files/v1/tus/upload-session-001", request.RequestUri!.AbsolutePath);
        Assert.Equal("org-001", request.Headers["X-Organization-Id"]);
        Assert.Equal("env-dev", request.Headers["X-Environment-Id"]);
        Assert.Equal("Bearer internal-test-token", request.Authorization!.ToString());
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
    /// #3314 的核心不变量（PlatformGateway 侧）：本网关不存在任何以调用方提供的 download grant id
    /// 为入参的路由，也不存在任何把 grant id 交给调用方的签发路由。
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
    /// 响应面零断言——审核把 grant URL 写进响应头，PG 117 条一条都不红。
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

    internal static void AssertNoGrantLeak(HttpResponseMessage response, string body, string grantId)
    {
        var rendered = string.Join(
            "\n",
            response.Headers
                .Concat(response.Content.Headers)
                .Select(header => $"{header.Key}: {string.Join(",", header.Value)}"));

        foreach (var (surface, text) in new[] { ("响应头", rendered), ("响应体", body) })
        {
            Assert.False(
                text.Contains("/download-grants/", StringComparison.OrdinalIgnoreCase),
                $"{surface}泄漏了 FileStorage 的 download-grant 路径：{text}");
            Assert.False(
                text.Contains(grantId, StringComparison.OrdinalIgnoreCase),
                $"{surface}泄漏了 download grant id：{text}");
        }
    }

    /// <summary>
    /// #3314 实测的越权方向之二：只持 <c>business.engineering.documents.read</c> 的主体曾经能在
    /// 本网关兑换 SOP 面签发的 grant。改造后它走不到任何 grant 入参，唯一的字节路由要
    /// <c>files.read</c>，缺码即被**本网关的权限门**拒绝，FileStorage 一发都收不到。
    ///
    /// 会失败的具体输入：把本路由的权限码换成别的、或不检查授权结果就继续代理。
    /// </summary>
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
    public async Task File_storage_http_client_uses_internal_token_and_rewrites_transfer_urls()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CreateUploadSessionResponse(
                "upload-session-001",
                "file-001",
                "tus",
                "tus",
                DateTimeOffset.UtcNow.AddMinutes(15),
                new TransferInstructions("/api/files/v1/tus/upload-session-001", new Dictionary<string, string>
                {
                    ["Tus-Resumable"] = "1.0.0"
                })))
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://files.local")
        };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var response = await files.CreateUploadSessionAsync(CreateUploadSessionRequest(), CancellationToken.None);

        Assert.Equal("/api/console/v1/files/tus/upload-session-001", response.Upload.Url);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/files/v1/upload-sessions", request.RequestUri.PathAndQuery);
        Assert.Equal("Bearer", request.Authorization!.Scheme);
        Assert.Equal("internal-test-token", request.Authorization.Parameter);
    }

    [Fact]
    public async Task File_storage_http_client_rejects_absolute_transfer_urls()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CreateUploadSessionResponse(
                "upload-session-001",
                "file-001",
                "tus",
                "tus",
                DateTimeOffset.UtcNow.AddMinutes(15),
                new TransferInstructions("https://files.example.test/direct/upload-session-001", new Dictionary<string, string>())))
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(
            () => files.CreateUploadSessionAsync(CreateUploadSessionRequest(), CancellationToken.None));

        Assert.Equal("filestorage-transfer-url-not-proxyable", exception.Reason);
    }

    [Fact]
    public async Task File_storage_http_client_rejects_network_path_transfer_urls()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CreateUploadSessionResponse(
                "upload-session-001",
                "file-001",
                "tus",
                "tus",
                DateTimeOffset.UtcNow.AddMinutes(15),
                new TransferInstructions("//files.example.test/direct/upload-session-001", new Dictionary<string, string>())))
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(
            () => files.CreateUploadSessionAsync(CreateUploadSessionRequest(), CancellationToken.None));

        Assert.Equal("filestorage-transfer-url-not-proxyable", exception.Reason);
    }

    [Theory]
    [InlineData("", "filestorage-empty-response")]
    [InlineData("{ not-valid-json", "filestorage-invalid-response")]
    public async Task File_storage_http_client_maps_invalid_json_responses(string payload, string expectedReason)
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload)
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(
            () => files.CreateUploadSessionAsync(CreateUploadSessionRequest(), CancellationToken.None));

        Assert.Equal(expectedReason, exception.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "filestorage-unexpected-status-400")]
    [InlineData(HttpStatusCode.Unauthorized, "filestorage-unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "filestorage-forbidden")]
    [InlineData(HttpStatusCode.NotFound, "filestorage-unexpected-status-404")]
    [InlineData(HttpStatusCode.Conflict, "filestorage-unexpected-status-409")]
    [InlineData(HttpStatusCode.InternalServerError, "filestorage-unavailable")]
    [InlineData(HttpStatusCode.BadGateway, "filestorage-unavailable")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "filestorage-unavailable")]
    public async Task File_storage_http_client_maps_downstream_status_codes(
        HttpStatusCode statusCode,
        string expectedReason)
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("downstream error")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(
            () => files.CreateUploadSessionAsync(CreateUploadSessionRequest(), CancellationToken.None));

        Assert.Equal(expectedReason, exception.Reason);
    }

    [Fact]
    public async Task File_storage_raw_proxy_filters_hop_by_hop_response_headers()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/download-grants", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(GrantFor("/api/files/v1/download-grants/download-grant-001/content"))
                };
            }

            var downstream = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello")
            };
            downstream.Headers.TransferEncodingChunked = true;
            downstream.Headers.Connection.Add("X-Private-Hop");
            downstream.Headers.Add("Keep-Alive", "timeout=5");
            downstream.Headers.Add("X-Private-Hop", "secret");
            downstream.Headers.Add("X-Trace-Id", "trace-001");
            return downstream;
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await files.StreamFileContentAsync("file-001", "org-001", "env-dev", context.Response, CancellationToken.None);

        var byteRequest = handler.Requests[1];
        Assert.Equal("/api/files/v1/download-grants/download-grant-001/content", byteRequest.RequestUri.AbsolutePath);
        Assert.Equal("org-001", byteRequest.Headers["X-Organization-Id"]);
        Assert.Equal("env-dev", byteRequest.Headers["X-Environment-Id"]);
        Assert.False(context.Response.Headers.ContainsKey("Transfer-Encoding"));
        Assert.False(context.Response.Headers.ContainsKey("Connection"));
        Assert.False(context.Response.Headers.ContainsKey("Keep-Alive"));
        Assert.False(context.Response.Headers.ContainsKey("X-Private-Hop"));
        Assert.False(context.Response.Headers.ContainsKey("Content-Length"));
        Assert.Equal("trace-001", context.Response.Headers["X-Trace-Id"].Single());
        body.Position = 0;
        using var reader = new StreamReader(body);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    /// <summary>
    /// #3314 路线 A 的结构不变量：本 client 取字节前必须自己签发 grant，且**不把 grant id 交出去**。
    ///
    /// 会失败的具体输入：把「先签发」那一跳去掉、直接拿调用方给的标识拼 content URL——那样
    /// <c>handler.Requests</c> 里就不会出现 <c>POST /api/files/v1/files/{fileId}/download-grants</c>，
    /// 第一条断言红。
    /// </summary>
    [Fact]
    public async Task File_content_signs_its_own_grant_server_side_and_never_takes_a_grant_id_from_the_caller()
    {
        var handler = new RecordingHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/download-grants", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(GrantFor("/api/files/v1/download-grants/grant-server-signed/content"))
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("bytes") });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await files.StreamFileContentAsync("file-001", "org-001", "env-dev", context.Response, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/api/files/v1/files/file-001/download-grants", handler.Requests[0].RequestUri.AbsolutePath);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("/api/files/v1/download-grants/grant-server-signed/content", handler.Requests[1].RequestUri.AbsolutePath);
    }

    /// <summary>
    /// 失败关闭：FileStorage 若回一个不可代理的下游地址（绝对 URL / 前缀不符），本 client 不得跟随。
    /// </summary>
    [Theory]
    [InlineData("https://filestorage.internal/api/files/v1/download-grants/g/content")]
    [InlineData("//filestorage.internal/api/files/v1/download-grants/g/content")]
    [InlineData("/api/files/v1/tus/g")]
    public async Task File_content_refuses_a_grant_url_that_is_not_a_proxyable_internal_download_path(string url)
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(GrantFor(url))
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://files.local") };
        var files = new HttpGatewayFileStorageClient(httpClient, new TestInternalServiceTokenProvider("internal-test-token"));
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        var exception = await Assert.ThrowsAsync<GatewayAuthException>(
            () => files.StreamFileContentAsync("file-001", "org-001", "env-dev", context.Response, CancellationToken.None));

        Assert.Equal("filestorage-transfer-url-not-proxyable", exception.Reason);
        // 拒绝必须发生在跟随之前
        Assert.Single(handler.Requests);
    }

    private static DownloadGrantResponse GrantFor(string url) =>
        new(
            "file-001",
            DateTimeOffset.Parse("2026-09-20T08:00:00Z"),
            new TransferInstructions(url, new Dictionary<string, string>
            {
                ["X-Organization-Id"] = "org-001",
                ["X-Environment-Id"] = "env-dev",
            }));

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

    private static WebApplicationFactory<Program> CreateFactory(
        FakeGatewayFileStorageClient files,
        FakeGatewayAuthorizationClient? auth = null)
    {
        return PlatformGatewayTestHost.CreateFactory()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGatewayFileStorageClient>();
                services.AddSingleton<IGatewayFileStorageClient>(files);
                services.RemoveAll<IGatewayAuthorizationClient>();
                services.AddSingleton<IGatewayAuthorizationClient>(auth ?? FakeGatewayAuthorizationClient.Allowed());
            }));
    }

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string requestUri)
    {
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());
        return request;
    }

    private static void AddTenantHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");
    }

    private static CreateUploadSessionRequest CreateUploadSessionRequest() =>
        new(
            "org-001",
            "env-dev",
            new OwnerReference("notification", "message", "msg-001"),
            "notification-attachment",
            "example.csv",
            "text/csv",
            42,
            null);

    private sealed class FakeGatewayFileStorageClient : IGatewayFileStorageClient
    {
        public CreateUploadSessionRequest? LastCreateRequest { get; private set; }
        public string? LastCompleteUploadSessionId { get; private set; }
        public CompleteUploadSessionRequest? LastCompleteRequest { get; private set; }
        public string? LastMetadataFileId { get; private set; }
        public ListFilesRequest? LastListRequest { get; private set; }
        public FileStorageUsageRequest? LastUsageRequest { get; private set; }
        public string? LastTusHeadUploadSessionId { get; private set; }
        public string? LastTusHeadOrganizationId { get; private set; }
        public string? LastTusHeadEnvironmentId { get; private set; }
        public string? LastTusPatchUploadSessionId { get; private set; }
        public string? LastTusPatchOrganizationId { get; private set; }
        public string? LastTusPatchEnvironmentId { get; private set; }
        public string? LastDownloadContentFileId { get; private set; }
        public string? LastDownloadContentOrganizationId { get; private set; }
        public string? LastDownloadContentEnvironmentId { get; private set; }
        public Exception? ExceptionToThrow { get; init; }

        public Task<CreateUploadSessionResponse> CreateUploadSessionAsync(
            CreateUploadSessionRequest request,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastCreateRequest = request;
            return Task.FromResult(new CreateUploadSessionResponse(
                "upload-session-001",
                "file-001",
                "tus",
                "tus",
                DateTimeOffset.UtcNow.AddMinutes(15),
                new TransferInstructions("/api/console/v1/files/tus/upload-session-001", new Dictionary<string, string>())));
        }

        public Task<FileMetadataResponse> CompleteUploadSessionAsync(
            string uploadSessionId,
            CompleteUploadSessionRequest request,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastCompleteUploadSessionId = uploadSessionId;
            LastCompleteRequest = request;
            return Task.FromResult(FileMetadata());
        }

        public Task<FileMetadataResponse> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastMetadataFileId = fileId;
            return Task.FromResult(FileMetadata());
        }

        public Task<FileListResponse> ListFilesAsync(ListFilesRequest request, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastListRequest = request;
            return Task.FromResult(new FileListResponse(1, [FileMetadata()]));
        }

        public Task<FileStorageUsageResponse> GetUsageAsync(FileStorageUsageRequest request, CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastUsageRequest = request;
            return Task.FromResult(new FileStorageUsageResponse(
                request.OrganizationId,
                request.EnvironmentId,
                request.FilePurpose,
                42,
                4096));
        }

        public Task ProxyTusHeadAsync(
            string uploadSessionId,
            string organizationId,
            string environmentId,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastTusHeadUploadSessionId = uploadSessionId;
            LastTusHeadOrganizationId = organizationId;
            LastTusHeadEnvironmentId = environmentId;
            response.Headers["Tus-Resumable"] = "1.0.0";
            response.Headers["Upload-Offset"] = "0";
            response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }

        public Task ProxyTusPatchAsync(
            string uploadSessionId,
            string organizationId,
            string environmentId,
            HttpRequest request,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastTusPatchUploadSessionId = uploadSessionId;
            LastTusPatchOrganizationId = organizationId;
            LastTusPatchEnvironmentId = environmentId;
            response.Headers["Tus-Resumable"] = "1.0.0";
            response.Headers["Upload-Offset"] = "3";
            response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        }

        public async Task StreamFileContentAsync(
            string fileId,
            string organizationId,
            string environmentId,
            HttpResponse response,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            LastDownloadContentFileId = fileId;
            LastDownloadContentOrganizationId = organizationId;
            LastDownloadContentEnvironmentId = environmentId;
            response.ContentType = "text/plain";
            await response.WriteAsync("hello", cancellationToken);
        }

        private void ThrowIfConfigured()
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
        }
    }

    private static FileMetadataResponse FileMetadata() =>
        new(
            "file-001",
            "org-001",
            "env-dev",
            new OwnerReference("notification", "message", "msg-001"),
            "notification-attachment",
            "example.csv",
            "text/csv",
            42,
            null,
            "completed",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow);

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization,
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => string.Join(",", header.Value),
                    StringComparer.OrdinalIgnoreCase)));
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri RequestUri,
        AuthenticationHeaderValue? Authorization,
        IReadOnlyDictionary<string, string> Headers);

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;

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
