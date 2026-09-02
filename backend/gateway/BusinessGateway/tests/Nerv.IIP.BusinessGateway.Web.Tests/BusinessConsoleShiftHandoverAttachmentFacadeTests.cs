using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// #3085 交接班附件门面。这里钉的是「换得出真实通路」而不是「端点存在」：
/// 上传面必须真的开出 tus 会话并把字节代理到 FileStorage，下载面必须让只持
/// <c>business.mes.handovers.read</c> 的主体换到字节，同时不能借这条门面读别的用途的文件。
/// </summary>
public sealed class BusinessConsoleShiftHandoverAttachmentFacadeTests
{
    private const string UploadSessionsRoute =
        "/api/business-console/v1/files/shift-handover-attachments/upload-sessions";

    private const string TusRoute =
        "/api/business-console/v1/files/shift-handover-attachments/tus/ups-handover-1";

    private const string ContentRoute =
        "/api/business-console/v1/files/shift-handover-attachments/download-grants/grant-handover-1/content";

    // ---------------------------------------------------------------------
    // 客户端层：门面对 FileStorage 的实际线上行为
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Upload_session_fixes_the_purpose_and_owner_and_returns_a_gateway_owned_tus_url()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/files/v1/upload-sessions" => Json(new CreateUploadSessionResponse(
                "ups-handover-1",
                "file-handover-1",
                "tus",
                "tus",
                DateTimeOffset.Parse("2026-09-02T08:15:00Z"),
                new TransferInstructions(
                    "/api/files/v1/tus/ups-handover-1",
                    new Dictionary<string, string> { ["x-nerv-upload-mode"] = "tus" }))),
            var path => throw new InvalidOperationException($"Unexpected downstream call: {path}"),
        });
        var client = CreateClient(handler);

        var session = await client.CreateShiftHandoverAttachmentUploadSessionAsync(
            "internal-test-token",
            "user-admin",
            new BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest(
                "org-001",
                "env-dev",
                "handover.jpg",
                "image/jpeg",
                2048),
            CancellationToken.None);

        using var forwarded = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal("shift-handover-photo", forwarded.RootElement.GetProperty("filePurpose").GetString());
        var owner = forwarded.RootElement.GetProperty("owner");
        Assert.Equal("business-mes", owner.GetProperty("ownerService").GetString());
        Assert.Equal("shift-handover-attachment", owner.GetProperty("ownerType").GetString());
        Assert.Equal("user-admin", owner.GetProperty("ownerId").GetString());

        Assert.Equal("tus", session.UploadProtocol);
        Assert.Equal(
            "/api/business-console/v1/files/shift-handover-attachments/tus/ups-handover-1",
            session.UploadUrl);
    }

    // 仓库默认 FileStorage:UploadProvider 就是 server-proxy，而 server-proxy 没有任何字节 endpoint。
    // 把那份占位指令原样交出去 = 调用方拿到一个必然写不进字节的 URL，所以这里必须失败关闭。
    [Fact]
    public async Task Upload_session_fails_closed_when_file_storage_is_not_running_the_tus_protocol()
    {
        var handler = new StubHandler(_ => Json(new CreateUploadSessionResponse(
            "ups-handover-1",
            "file-handover-1",
            "server-proxy",
            "server-proxy",
            DateTimeOffset.Parse("2026-09-02T08:15:00Z"),
            new TransferInstructions("/api/files/v1/upload/ups-handover-1", new Dictionary<string, string>()))));
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.CreateShiftHandoverAttachmentUploadSessionAsync(
                "internal-test-token",
                "user-admin",
                new BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest(
                    "org-001",
                    "env-dev",
                    "handover.jpg",
                    "image/jpeg",
                    2048),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("filestorage-upload-protocol-unsupported", exception.Message);
    }

    // 真栈实测出来的输入：business-gateway 拿不到 FileStorage 地址时连接直接失败。
    // 不映射的话 HttpRequestException 逃逸成 500「未知错误」，调用方无从判断是下游不可用。
    [Fact]
    public async Task Upload_session_reports_file_storage_unavailable_instead_of_leaking_a_transport_failure()
    {
        var client = CreateClient(new StubHandler(_ => throw new HttpRequestException("connection refused")));

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.CreateShiftHandoverAttachmentUploadSessionAsync(
                "internal-test-token",
                "user-admin",
                new BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest(
                    "org-001",
                    "env-dev",
                    "handover.jpg",
                    "image/jpeg",
                    2048),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal("filestorage-unavailable", exception.Message);
    }

    [Fact]
    public async Task Complete_declares_the_shift_handover_purpose_and_returns_the_attachment_record()
    {
        var handler = new StubHandler(_ => Json(new FileMetadataResponse(
            "file-handover-1",
            "org-001",
            "env-dev",
            new OwnerReference("business-mes", "shift-handover-attachment", "user-admin"),
            "shift-handover-photo",
            "handover.jpg",
            "image/jpeg",
            2048,
            "sha256:" + new string('a', 64),
            "available",
            DateTimeOffset.Parse("2026-09-02T08:00:00Z"),
            DateTimeOffset.Parse("2026-09-02T08:01:00Z"))));
        var client = CreateClient(handler);

        var attachment = await client.CompleteShiftHandoverAttachmentUploadAsync(
            "internal-test-token",
            "ups-handover-1",
            new BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest("org-001", "env-dev", null, 2048),
            CancellationToken.None);

        Assert.Equal(
            "/api/files/v1/upload-sessions/ups-handover-1/complete",
            handler.Requests[0].RequestUri!.AbsolutePath);
        using var forwarded = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal("shift-handover-photo", forwarded.RootElement.GetProperty("filePurpose").GetString());
        Assert.Equal(
            new BusinessConsoleMesShiftHandoverAttachment("file-handover-1", "handover.jpg", "image/jpeg", 2048),
            attachment);
    }

    // #3085 的下载口径：FileStorage 的 download-grant 不看用途，如果门面也不看，
    // 持 business.mes.handovers.read 的主体就能拿任意 fileId（例如工程 SOP 文件）换出字节。
    [Fact]
    public async Task Download_grant_refuses_a_file_whose_purpose_is_not_a_shift_handover_photo()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/files/v1/files/file-sop-v2" => Json(new FileMetadataResponse(
                "file-sop-v2",
                "org-001",
                "env-dev",
                new OwnerReference("business-product-engineering", "sop-document", "DOC-001"),
                "engineering-document",
                "sop.pdf",
                "application/pdf",
                4096,
                null,
                "available",
                DateTimeOffset.Parse("2026-09-02T08:00:00Z"),
                DateTimeOffset.Parse("2026-09-02T08:01:00Z"))),
            var path => throw new InvalidOperationException($"Unexpected downstream call: {path}"),
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.CreateShiftHandoverAttachmentDownloadGrantAsync(
                "internal-test-token",
                "file-sop-v2",
                new BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest("org-001", "env-dev"),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("filestorage-file-not-shift-handover-attachment", exception.Message);
        // 用途不符时不得向 FileStorage 发出 download-grant 请求。
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Download_grant_rewrites_the_content_url_to_the_shift_handover_console_route()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/files/v1/files/file-handover-1" => Json(new FileMetadataResponse(
                "file-handover-1",
                "org-001",
                "env-dev",
                new OwnerReference("business-mes", "shift-handover-attachment", "user-admin"),
                "shift-handover-photo",
                "handover.jpg",
                "image/jpeg",
                2048,
                null,
                "available",
                DateTimeOffset.Parse("2026-09-02T08:00:00Z"),
                DateTimeOffset.Parse("2026-09-02T08:01:00Z"))),
            "/api/files/v1/files/file-handover-1/download-grants" => Json(new DownloadGrantResponse(
                "file-handover-1",
                DateTimeOffset.Parse("2026-09-02T08:10:00Z"),
                new TransferInstructions(
                    "/api/files/v1/download-grants/grant-handover-1/content",
                    new Dictionary<string, string>
                    {
                        ["X-Organization-Id"] = "org-001",
                        ["X-Environment-Id"] = "env-dev",
                    }))),
            var path => throw new InvalidOperationException($"Unexpected downstream call: {path}"),
        });
        var client = CreateClient(handler);

        var grant = await client.CreateShiftHandoverAttachmentDownloadGrantAsync(
            "internal-test-token",
            "file-handover-1",
            new BusinessConsoleCreateShiftHandoverAttachmentDownloadGrantRequest("org-001", "env-dev"),
            CancellationToken.None);

        Assert.Equal(ContentRoute, grant.DownloadUrl);
        Assert.Equal("org-001", grant.DownloadHeaders["X-Organization-Id"]);
    }

    [Fact]
    public async Task Tus_patch_proxy_forwards_the_resume_headers_and_the_chunk_bytes()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/api/files/v1/tus/ups-handover-1", request.RequestUri!.AbsolutePath);
            Assert.Equal("1.0.0", request.Headers.GetValues("Tus-Resumable").Single());
            Assert.Equal("512", request.Headers.GetValues("Upload-Offset").Single());
            Assert.Equal("sha256 abc", request.Headers.GetValues("Upload-Checksum").Single());
            var response = new HttpResponseMessage(HttpStatusCode.NoContent);
            response.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
            response.Headers.TryAddWithoutValidation("Upload-Offset", "1024");
            response.Content = new ByteArrayContent([]);
            return response;
        });
        var client = CreateClient(handler);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "PATCH";
        httpContext.Request.ContentType = "application/offset+octet-stream";
        httpContext.Request.Body = new MemoryStream("chunk"u8.ToArray());
        httpContext.Request.Headers["Tus-Resumable"] = "1.0.0";
        httpContext.Request.Headers["Upload-Offset"] = "512";
        httpContext.Request.Headers["Upload-Checksum"] = "sha256 abc";
        httpContext.Response.Body = new MemoryStream();

        await client.ProxyShiftHandoverAttachmentTusAsync(
            "internal-test-token",
            "ups-handover-1",
            httpContext.Request,
            httpContext.Response,
            CancellationToken.None);

        Assert.Equal("chunk", handler.Bodies[0]);
        Assert.Equal(StatusCodes.Status204NoContent, httpContext.Response.StatusCode);
        Assert.Equal("1024", httpContext.Response.Headers["Upload-Offset"]);
        Assert.Equal("1.0.0", httpContext.Response.Headers["Tus-Resumable"]);
    }

    // ---------------------------------------------------------------------
    // 端点层：权限口径与门面接线
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Upload_session_endpoint_binds_the_owner_to_the_authenticated_principal()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(UploadSessionsRoute, new
        {
            organizationId = "org-001",
            environmentId = "env-dev",
            fileName = "handover.jpg",
            contentType = "image/jpeg",
            expectedSizeBytes = 2048,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MesHandoversManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal("mes-shift-handover-attachment", auth.LastRequirement.ResourceType);
        Assert.Equal("user-admin", files.LastUploadOwnerId);
        Assert.Equal("internal-test-token", files.LastInternalToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("tus", data.GetProperty("uploadProtocol").GetString());
        Assert.Equal(
            "/api/business-console/v1/files/shift-handover-attachments/tus/ups-handover-1",
            data.GetProperty("uploadUrl").GetString());
    }

    [Fact]
    public async Task Complete_endpoint_returns_the_attachment_record_the_handover_write_face_consumes()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            $"{UploadSessionsRoute}/ups-handover-1/complete",
            new { organizationId = "org-001", environmentId = "env-dev", sizeBytes = 2048 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MesHandoversManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal("ups-handover-1", files.LastCompletedUploadSessionId);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("file-handover-1", data.GetProperty("fileId").GetString());
        Assert.Equal("handover.jpg", data.GetProperty("fileName").GetString());
        Assert.Equal("image/jpeg", data.GetProperty("contentType").GetString());
        Assert.Equal(2048, data.GetProperty("sizeBytes").GetInt64());
    }

    [Theory]
    [InlineData("HEAD", false)]
    [InlineData("PATCH", true)]
    public async Task Tus_endpoints_reach_the_file_storage_proxy_under_the_handover_write_permission(
        string method,
        bool expectsBody)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = new HttpRequestMessage(new HttpMethod(method), TusRoute);
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");
        if (expectsBody)
        {
            request.Content = new ByteArrayContent("chunk"u8.ToArray());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("1024", response.Headers.GetValues("Upload-Offset").Single());
        Assert.Equal(BusinessGatewayPermissions.MesHandoversManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal("ups-handover-1", files.LastTusUploadSessionId);
        Assert.Equal(expectsBody, files.LastTusRequestHadBody);
    }

    // HEAD 进不了 BusinessConsoleRoutes 的 Forbidden 理论（那条理论对非 GET 一律挂 JSON body），
    // 所以它的拒绝路径在这里单独钉住：只持 handovers.read 的主体不能续传字节。
    [Fact]
    public async Task Tus_offset_endpoint_rejects_a_principal_without_the_handover_write_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesHandoversRead);
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = new HttpRequestMessage(HttpMethod.Head, TusRoute);
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MesHandoversManage, auth.LastRequirement!.PermissionCode);
        Assert.Null(files.LastTusUploadSessionId);
    }

    [Fact]
    public async Task Download_grant_endpoint_is_gated_on_the_handover_read_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/files/shift-handover-attachments/file-handover-1/download-grants",
            new { organizationId = "org-001", environmentId = "env-dev" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MesHandoversRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("mes-shift-handover-attachment", auth.LastRequirement.ResourceType);
        Assert.Equal("file-handover-1", auth.LastRequirement.ResourceId);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ContentRoute, document.RootElement.GetProperty("data").GetProperty("downloadUrl").GetString());
    }

    // #3085 票面写死的失败输入：只持 business.mes.handovers.read 的主体在旧口径下换不出下载地址。
    [Fact]
    public async Task A_principal_holding_only_handovers_read_can_reach_the_attachment_bytes()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesHandoversRead);
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var grant = await client.PostAsJsonAsync(
            "/api/business-console/v1/files/shift-handover-attachments/file-handover-1/download-grants",
            new { organizationId = "org-001", environmentId = "env-dev" });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var grantBody = JsonDocument.Parse(await grant.Content.ReadAsStringAsync());
        var contentUrl = grantBody.RootElement.GetProperty("data").GetProperty("downloadUrl").GetString()!;

        using var contentRequest = new HttpRequestMessage(HttpMethod.Get, contentUrl);
        contentRequest.Headers.Add("X-Organization-Id", "org-001");
        contentRequest.Headers.Add("X-Environment-Id", "env-dev");
        var content = await client.SendAsync(contentRequest);

        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("handover photo bytes", await content.Content.ReadAsStringAsync());
        Assert.Equal("grant-handover-1", files.LastAttachmentContentGrantId);
        Assert.Equal("org-001", files.LastAttachmentContentOrganizationId);
        Assert.Equal("env-dev", files.LastAttachmentContentEnvironmentId);
    }

    // 反向：SOP 下载面不因为本票而对交接班读者开门。
    [Fact]
    public async Task A_principal_holding_only_handovers_read_still_cannot_use_the_sop_download_face()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesHandoversRead);
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            "/api/business-console/v1/files/file-sop-v2/download-grants",
            new { organizationId = "org-001", environmentId = "env-dev" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.EngineeringDocumentsRead, auth.LastRequirement!.PermissionCode);
    }

    private static BusinessGatewayTestHostLease LeaseHost(
        FakeBusinessGatewayAuthorizationClient auth,
        IBusinessFileStorageClient files) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessFileStorageClient>();
            services.AddSingleton(files);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(
                new TestInternalServiceTokenProvider("internal-test-token"));
        });

    private static HttpBusinessFileStorageClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://file-storage.local") });

    private static HttpResponseMessage Json<T>(T payload) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(request);
            return responseFactory(request);
        }
    }
}
