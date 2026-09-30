using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// #3856 条码模板文件上传门面。
///
/// 客户端层用 <see cref="StubHandler"/> 看发往 FileStorage 的线上形状：用途、owner、内容类型都必须由
/// 网关固定——打印时 BarcodeLabel 按这几项逐一校验模板文件（owner 必须等于模板编码），任何一项被调用方
/// 左右都会让模板在建批次时才失败。端点层用 Recording 桩看权限口径与路由落点。
/// </summary>
public sealed class BusinessConsoleBarcodeTemplateAssetFacadeTests
{
    private const string UploadSessionsRoute =
        "/api/business-console/v1/files/barcode-template-assets/upload-sessions";

    private const string TusRoute =
        "/api/business-console/v1/files/barcode-template-assets/tus/ups-template-1";

    private const string Checksum = "sha256:81e2fda190f162f1b985b5ffe710e9b271e3962b1edb77e26fd634bdfbe5d4a7";

    // =====================================================================
    // 客户端层：发往 FileStorage 的线上形状
    // =====================================================================

    [Fact]
    public async Task Upload_session_fixes_purpose_content_type_and_binds_the_owner_to_the_template_code()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/files/v1/upload-sessions" => Json(UploadSession("tus", "/api/files/v1/tus/ups-template-1")),
            var path => throw new InvalidOperationException($"Unexpected downstream call: {path}"),
        });
        var client = CreateClient(handler);

        var session = await client.CreateBarcodeTemplateAssetUploadSessionAsync(
            "internal-test-token",
            new BusinessConsoleCreateBarcodeTemplateAssetUploadSessionRequest(
                "org-001", "env-dev", " BOX_LABEL ", "box-label.json", 363, Checksum),
            CancellationToken.None);

        using var forwarded = JsonDocument.Parse(handler.Bodies[0]!);
        var root = forwarded.RootElement;
        Assert.Equal("barcode-label-template", root.GetProperty("filePurpose").GetString());
        Assert.Equal("application/vnd.nerv-iip.label-template+json", root.GetProperty("contentType").GetString());
        Assert.Equal(Checksum, root.GetProperty("checksum").GetString());
        Assert.Equal(363, root.GetProperty("expectedSizeBytes").GetInt64());
        var owner = root.GetProperty("owner");
        Assert.Equal("business-barcode-label", owner.GetProperty("ownerService").GetString());
        Assert.Equal("label-template", owner.GetProperty("ownerType").GetString());
        // 打印侧按 Ordinal 比对 owner 与已存模板编码（模板编码落库前会去空白）。
        Assert.Equal("BOX_LABEL", owner.GetProperty("ownerId").GetString());
        Assert.Equal("Bearer internal-test-token", handler.Requests[0].Headers.Authorization!.ToString());

        Assert.Equal("tus", session.UploadProtocol);
        Assert.Equal("/api/business-console/v1/files/barcode-template-assets/tus/ups-template-1", session.UploadUrl);
    }

    [Fact]
    public async Task Upload_session_fails_closed_when_file_storage_is_not_running_the_tus_protocol()
    {
        var client = CreateClient(new StubHandler(_ =>
            Json(UploadSession("server-proxy", "/api/files/v1/upload/ups-template-1"))));

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.CreateBarcodeTemplateAssetUploadSessionAsync(
                "internal-test-token", UploadRequest(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("filestorage-upload-protocol-unsupported", exception.Message);
    }

    [Fact]
    public async Task Upload_session_refuses_a_transfer_url_that_is_not_a_proxyable_internal_path()
    {
        var client = CreateClient(new StubHandler(_ =>
            Json(UploadSession("tus", "https://filestorage.internal/api/files/v1/tus/ups-template-1"))));

        var exception = await Assert.ThrowsAsync<BusinessServiceProxyException>(() =>
            client.CreateBarcodeTemplateAssetUploadSessionAsync(
                "internal-test-token", UploadRequest(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("filestorage-transfer-url-not-proxyable", exception.Message);
    }

    [Fact]
    public async Task Complete_declares_the_template_purpose_and_forwards_the_checksum()
    {
        var handler = new StubHandler(_ => Json(FileMetadata()));
        var client = CreateClient(handler);

        var asset = await client.CompleteBarcodeTemplateAssetUploadAsync(
            "internal-test-token",
            "ups-template-1",
            new BusinessConsoleCompleteBarcodeTemplateAssetUploadRequest("org-001", "env-dev", Checksum, 363),
            CancellationToken.None);

        Assert.Equal(
            "/api/files/v1/upload-sessions/ups-template-1/complete",
            handler.Requests[0].RequestUri!.AbsolutePath);
        using var forwarded = JsonDocument.Parse(handler.Bodies[0]!);
        Assert.Equal("barcode-label-template", forwarded.RootElement.GetProperty("filePurpose").GetString());
        Assert.Equal(Checksum, forwarded.RootElement.GetProperty("checksum").GetString());
        Assert.Equal(363, forwarded.RootElement.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(new BusinessConsoleBarcodeTemplateAsset("file-template-1", "box-label.json", 363), asset);
    }

    // =====================================================================
    // 端点层：权限口径与路由落点
    // =====================================================================

    [Fact]
    public async Task Upload_session_endpoint_is_gated_by_the_template_manage_permission()
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
            templateCode = "BOX_LABEL",
            fileName = "box-label.json",
            expectedSizeBytes = 363,
            checksum = Checksum,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.BarcodeTemplatesManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal("barcode-template-asset", auth.LastRequirement.ResourceType);
        Assert.Equal("BOX_LABEL", files.LastTemplateAssetSessionRequest!.TemplateCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal(
            "/api/business-console/v1/files/barcode-template-assets/tus/ups-template-1",
            data.GetProperty("uploadUrl").GetString());
    }

    [Theory]
    [InlineData("templateCode")]
    [InlineData("checksum")]
    public async Task Upload_session_endpoint_rejects_a_request_without_a_required_field(string missingField)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var body = new Dictionary<string, object?>
        {
            ["organizationId"] = "org-001",
            ["environmentId"] = "env-dev",
            ["templateCode"] = "BOX_LABEL",
            ["fileName"] = "box-label.json",
            ["expectedSizeBytes"] = 363,
            ["checksum"] = Checksum,
        };
        body[missingField] = "";

        var response = await client.PostAsJsonAsync(UploadSessionsRoute, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(files.LastTemplateAssetSessionRequest);
    }

    [Fact]
    public async Task Complete_endpoint_returns_the_uploaded_file_under_the_template_manage_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        await using var lease = LeaseHost(auth, files);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.PostAsJsonAsync(
            $"{UploadSessionsRoute}/ups-template-1/complete",
            new { organizationId = "org-001", environmentId = "env-dev", checksum = Checksum, sizeBytes = 363 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.BarcodeTemplatesManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal("ups-template-1", files.LastTemplateAssetCompletedUploadSessionId);
        Assert.Equal(Checksum, files.LastTemplateAssetCompleteRequest!.Checksum);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("file-template-1", data.GetProperty("fileId").GetString());
        Assert.Equal("box-label.json", data.GetProperty("fileName").GetString());
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("PATCH")]
    public async Task Tus_endpoints_reach_the_template_proxy_leg_under_the_template_manage_permission(string method)
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        var transfer = new RecordingBusinessFileTransferClient();
        await using var lease = LeaseHost(auth, files, transfer);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = new HttpRequestMessage(new HttpMethod(method), TusRoute);
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");
        if (method == "PATCH")
        {
            request.Content = new ByteArrayContent("{}"u8.ToArray());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.BarcodeTemplatesManage, auth.LastRequirement!.PermissionCode);
        Assert.Equal(method == "HEAD" ? "ups-template-1" : null, transfer.LastTemplateAssetTusHeadUploadSessionId);
        Assert.Equal(method == "PATCH" ? "ups-template-1" : null, transfer.LastTemplateAssetTusPatchUploadSessionId);
        // 模板门面不得落到交接班那条腿上。
        Assert.Null(transfer.LastTusHeadUploadSessionId);
        Assert.Null(transfer.LastTusPatchUploadSessionId);
    }

    [Fact]
    public async Task Tus_patch_rejects_a_principal_holding_only_the_handover_write_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MesHandoversManage);
        var files = new RecordingBusinessFileStorageClient();
        var transfer = new RecordingBusinessFileTransferClient();
        await using var lease = LeaseHost(auth, files, transfer);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = new HttpRequestMessage(HttpMethod.Patch, TusRoute);
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");
        request.Content = new ByteArrayContent("{}"u8.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.BarcodeTemplatesManage, auth.LastRequirement!.PermissionCode);
        Assert.Null(transfer.LastTemplateAssetTusPatchUploadSessionId);
    }

    // =====================================================================
    // 夹具
    // =====================================================================

    private static BusinessConsoleCreateBarcodeTemplateAssetUploadSessionRequest UploadRequest() =>
        new("org-001", "env-dev", "BOX_LABEL", "box-label.json", 363, Checksum);

    private static CreateUploadSessionResponse UploadSession(string provider, string uploadUrl) =>
        new(
            "ups-template-1",
            "file-template-1",
            provider,
            provider,
            DateTimeOffset.Parse("2026-09-28T08:15:00Z"),
            new TransferInstructions(uploadUrl, new Dictionary<string, string> { ["x-nerv-upload-mode"] = provider }));

    private static FileMetadataResponse FileMetadata() =>
        new(
            "file-template-1",
            "org-001",
            "env-dev",
            new OwnerReference("business-barcode-label", "label-template", "BOX_LABEL"),
            "barcode-label-template",
            "box-label.json",
            "application/vnd.nerv-iip.label-template+json",
            363,
            Checksum,
            "available",
            DateTimeOffset.Parse("2026-09-28T08:00:00Z"),
            DateTimeOffset.Parse("2026-09-28T08:01:00Z"));

    private static BusinessGatewayTestHostLease LeaseHost(
        FakeBusinessGatewayAuthorizationClient auth,
        IBusinessFileStorageClient files,
        IBusinessFileTransferClient? transfer = null) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessFileStorageClient>();
            services.AddSingleton(files);
            services.RemoveAll<IBusinessFileTransferClient>();
            services.AddSingleton(transfer ?? new RecordingBusinessFileTransferClient());
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
