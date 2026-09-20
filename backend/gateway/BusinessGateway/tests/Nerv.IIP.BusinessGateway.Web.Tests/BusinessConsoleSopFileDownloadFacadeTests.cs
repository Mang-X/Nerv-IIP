using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

/// <summary>
/// #3314 工程 SOP 下载门面。
///
/// 本票实测的缺陷是**跨权限口径兑换**：BusinessGateway 的
/// <c>GET /api/business-console/v1/files/download-grants/{downloadGrantId}/content</c>
/// （门：<c>business.engineering.documents.read</c>）与 PlatformGateway 的
/// <c>GET /api/console/v1/files/download-grants/{downloadGrantId}/content</c>
/// （门：<c>files.read</c>）都收调用方传入的 grant id 并代理到同一个下游；真栈里两个方向都 200。
/// IAM 对 user 主体只看权限码、不看资源，FileStorage 的 grant 记录里没有 owner/用途，
/// 三层没有一层能拒绝。
///
/// 修法是结构性的（ADR 0030 决策 3）：**grant id 根本不交给调用方**。下面钉住的就是这个结构，
/// 不是某条 if 的返回码——因为可跨路兑换的前提是「存在一条以 grant id 为入参的路由」，
/// 前提不成立时不需要任何一处运行期判断。
/// </summary>
public sealed class BusinessConsoleSopFileDownloadFacadeTests
{
    private const string SopContentRoute =
        "/api/business-console/v1/files/sop-documents/file-sop-v2/content";

    /// <summary>
    /// 承接 #3314 的核心不变量：本网关不存在任何以调用方提供的 download grant id 为入参的路由，
    /// 也不存在任何把 grant id 交给调用方的签发路由。
    ///
    /// 会失败的具体输入：把 #3314 之前那两条路由中的任意一条加回来（
    /// <c>GET .../files/download-grants/{id}/content</c> 或 <c>POST .../files/{fileId}/download-grants</c>），
    /// 对应那一格立刻不再是 404/405。
    ///
    /// 阴性对照：同一个主体、同一套桩，走**新形状**的 fileId 路由必须仍然 200——
    /// 否则本用例会退化成「网关什么都不响应也能通过」。
    /// </summary>
    [Fact]
    public async Task No_business_console_route_redeems_a_download_grant_id_supplied_by_the_caller()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        var transfer = new RecordingBusinessFileTransferClient();
        await using var lease = LeaseHost(auth, files, transfer);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        // ① 以 grant id 为入参的兑换面：不得存在
        using (var redeem = Scoped(HttpMethod.Get, "/api/business-console/v1/files/download-grants/grant-sop-v2/content"))
        {
            var response = await client.SendAsync(redeem);
            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"以 grant id 为入参的兑换面不得存在，实际 {(int)response.StatusCode}");
        }

        // ② 把 grant id 交出去的签发面：不得存在
        {
            var response = await client.PostAsJsonAsync(
                "/api/business-console/v1/files/file-sop-v2/download-grants",
                new { organizationId = "org-001", environmentId = "env-dev" });
            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"把 grant id 交给调用方的签发面不得存在，实际 {(int)response.StatusCode}");
        }

        // 以上两发都不得触达 FileStorage
        Assert.Null(files.LastSopAuthorizedFileId);
        Assert.Null(transfer.LastStreamedTicket);

        // 阴性对照：新形状必须仍然通
        using (var ok = Scoped(HttpMethod.Get, SopContentRoute))
        {
            var response = await client.SendAsync(ok);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("SOP PDF bytes", await response.Content.ReadAsStringAsync());
        }
    }

    /// <summary>
    /// #3314 实测的越权方向之一：只持平台级 <c>files.read</c>（PlatformGateway 文件面的口径）的主体
    /// 曾经能兑换 SOP 面签发的 grant 拿到 SOP 字节。改造后它在 BusinessGateway 这一侧被
    /// **网关自己的权限门**挡住，根本走不到 FileStorage。
    ///
    /// 会失败的具体输入：把本路由的权限码从 <c>business.engineering.documents.read</c> 改成
    /// <c>files.read</c>（或去掉 <see cref="BusinessGatewayAuthorization.RequirePermissionAsync"/> 的返回值判断），
    /// 本用例立刻红。
    /// </summary>
    [Fact]
    public async Task Sop_content_route_rejects_a_principal_that_only_holds_the_platform_files_read_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly("files.read");
        var files = new RecordingBusinessFileStorageClient();
        var transfer = new RecordingBusinessFileTransferClient();
        await using var lease = LeaseHost(auth, files, transfer);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = Scoped(HttpMethod.Get, SopContentRoute);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // 拒绝的是网关的权限门，不是下游：FileStorage 一发都不该收到。
        Assert.Equal(BusinessGatewayPermissions.EngineeringDocumentsRead, auth.LastRequirement!.PermissionCode);
        Assert.Null(files.LastSopAuthorizedFileId);
        Assert.Null(transfer.LastStreamedTicket);
    }

    /// <summary>
    /// 反向的同一类风险：持 <c>business.engineering.documents.read</c> 的主体不得借这条门面
    /// 取到**不是工程文档**的文件字节（例如交接班照片）。用途复核在 JSON 面一处把关。
    ///
    /// 会失败的具体输入：目标文件的 <c>filePurpose</c> 是 <c>shift-handover-photo</c>。
    /// </summary>
    [Fact]
    public async Task Sop_content_route_refuses_a_file_whose_purpose_is_not_an_engineering_document()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new PurposeAwareFileStorageClient();
        var transfer = new RecordingBusinessFileTransferClient();
        await using var lease = LeaseHost(auth, files, transfer);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = Scoped(
            HttpMethod.Get,
            "/api/business-console/v1/files/sop-documents/file-handover-1/content");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(transfer.LastStreamedTicket);
    }

    /// <summary>
    /// #3314 第 1 轮审核 E2 的承担方（BG 侧）：「grant id 不出网关进程」此前只有契约形状被钉住，
    /// 响应面零断言——审核把 ticket 的下游 URL 写进响应头，BG 1590 条一条都不红。
    ///
    /// 旧缺陷的实际形态就是**响应字段**（旧 `downloadUrl` 里带 `/files/download-grants/{id}/content`）。
    /// 这里断言真正交给调用方的那一面：字节响应的头与体都不得出现 `/download-grants/` 片段或 grant id。
    ///
    /// **走真实的 <see cref="HttpBusinessFileTransferClient"/>**（下游由 <see cref="LeakProbeHandler"/> 桩住），
    /// 不走 Recording 替身——第一版用替身写，结果对真实客户端的泄漏变异零鉴别力（本轮 H4 实测 GREEN）。
    ///
    /// 阴性对照：下游真实回的字节必须原样到达，且下游**故意**回一个带 grant id 的自定义头，
    /// 用来证明这条链路确实会转发下游响应头——否则本断言会退化成「什么头都不转发也能过」。
    /// </summary>
    [Fact]
    public async Task Sop_content_response_leaks_neither_the_grant_id_nor_the_downstream_grant_url()
    {
        const string grantId = "grant-sop-v2";
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        var files = new RecordingBusinessFileStorageClient();
        var handler = new LeakProbeHandler();
        var transfer = new HttpBusinessFileTransferClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://file-storage.local") });
        await using var lease = LeaseHost(auth, files, transfer);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        using var request = Scoped(HttpMethod.Get, SopContentRoute);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // 阴性对照 ①：真实字节必须到达。
        Assert.Equal("SOP PDF bytes", body);
        // 阴性对照 ②：下游的自定义响应头确实被转发了——证明这条链路有转发头的能力。
        Assert.Equal("forwarded", response.Headers.GetValues("X-Downstream-Marker").Single());
        // 桩客户端签发的下游 URL 带 grant id，它必须留在进程内。
        Assert.Contains(grantId, handler.LastRequestPath);
        AssertNoGrantLeak(response, body, grantId);
    }

    private static void AssertNoGrantLeak(HttpResponseMessage response, string body, string grantId)
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

    /// <summary>取字节那一跳的下游桩：回真实字节 + 一个可被断言转发到的自定义头。</summary>
    private sealed class LeakProbeHandler : HttpMessageHandler
    {
        public string LastRequestPath { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestPath = request.RequestUri!.AbsolutePath;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("SOP PDF bytes"u8.ToArray()),
            };
            response.Headers.TryAddWithoutValidation("X-Downstream-Marker", "forwarded");
            return Task.FromResult(response);
        }
    }

    private static HttpRequestMessage Scoped(HttpMethod method, string route)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Add("X-Organization-Id", "org-001");
        request.Headers.Add("X-Environment-Id", "env-dev");
        return request;
    }

    private static BusinessGatewayTestHostLease LeaseHost(
        FakeBusinessGatewayAuthorizationClient auth,
        IBusinessFileStorageClient files,
        IBusinessFileTransferClient transfer) =>
        BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessFileStorageClient>();
            services.AddSingleton(files);
            services.RemoveAll<IBusinessFileTransferClient>();
            services.AddSingleton(transfer);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(
                new TestInternalServiceTokenProvider("internal-test-token"));
        });

    /// <summary>用途不符时按真实 client 的口径抛 404，用来在端点层观察拒绝而不是在 client 层。</summary>
    private sealed class PurposeAwareFileStorageClient : IBusinessFileStorageClient
    {
        public Task<BusinessFileDownloadTicket> AuthorizeSopFileDownloadAsync(
            string internalBearerToken,
            string fileId,
            string organizationId,
            string environmentId,
            CancellationToken cancellationToken) =>
            throw BusinessServiceProxyException.FromSafeDownstreamMessage(
                HttpStatusCode.NotFound,
                "filestorage-file-not-engineering-document");

        public Task<BusinessConsoleShiftHandoverAttachmentUploadSessionResponse> CreateShiftHandoverAttachmentUploadSessionAsync(
            string internalBearerToken,
            string ownerId,
            BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BusinessConsoleMesShiftHandoverAttachment> CompleteShiftHandoverAttachmentUploadAsync(
            string internalBearerToken,
            string uploadSessionId,
            BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BusinessFileDownloadTicket> AuthorizeShiftHandoverAttachmentDownloadAsync(
            string internalBearerToken,
            string fileId,
            string organizationId,
            string environmentId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
