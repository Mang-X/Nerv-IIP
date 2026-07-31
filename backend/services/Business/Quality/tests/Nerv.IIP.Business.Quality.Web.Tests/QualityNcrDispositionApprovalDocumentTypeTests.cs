using System.Net;
using System.Text.Json;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// #1327 回归锁：NCR 处置审批的 <c>documentType</c> 三方（种子模板 / 界面发起面 / Quality 白名单）
/// 必须共用同一个权威码值。此前白名单只认 <c>quality-ncr</c>，而种子模板挂的是 <c>ncr-disposition</c>，
/// 于是种子态下处置审批永远判不通过——本测试让任何一方的漂移直接变红。
/// </summary>
public sealed class QualityNcrDispositionApprovalDocumentTypeTests
{
    [Fact]
    public async Task Seed_document_type_is_accepted_by_the_quality_approval_whitelist()
    {
        var client = CreateClient(ApprovalDocumentTypes.NcrDisposition);

        Assert.True(await client.IsApprovedForNcrDispositionAsync(
            "chain-001",
            "org-001",
            "env-dev",
            "NCR-2026-0001",
            CancellationToken.None));
    }

    [Theory]
    [InlineData("quality-ncr")]
    [InlineData("nonconformance-report")]
    [InlineData("nonconformance-report-disposition")]
    public async Task Legacy_document_types_stay_accepted_for_chains_started_before_the_convergence(string documentType)
    {
        var client = CreateClient(documentType);

        Assert.True(await client.IsApprovedForNcrDispositionAsync(
            "chain-001",
            "org-001",
            "env-dev",
            "NCR-2026-0001",
            CancellationToken.None));
    }

    [Fact]
    public async Task Unrelated_document_type_is_still_rejected()
    {
        var client = CreateClient("purchase-order");

        Assert.False(await client.IsApprovedForNcrDispositionAsync(
            "chain-001",
            "org-001",
            "env-dev",
            "NCR-2026-0001",
            CancellationToken.None));
    }

    /// <summary>
    /// 审批链响应缺 <c>documentType</c> 字段时反序列化出 null：判定必须是「不通过」，
    /// 而不是让 <c>HashSet.Contains(null)</c> 抛 ArgumentNullException 冒成 500。
    /// </summary>
    [Fact]
    public async Task Missing_document_type_is_rejected_instead_of_throwing()
    {
        var client = CreateClient(documentType: null);

        Assert.False(await client.IsApprovedForNcrDispositionAsync(
            "chain-001",
            "org-001",
            "env-dev",
            "NCR-2026-0001",
            CancellationToken.None));
    }

    [Fact]
    public void Canonical_document_type_is_the_single_source_of_truth()
    {
        Assert.Equal("ncr-disposition", ApprovalDocumentTypes.NcrDisposition);
        Assert.Contains(ApprovalDocumentTypes.NcrDisposition, ApprovalDocumentTypes.NcrDispositionAliases);
        Assert.Contains(ApprovalDocumentTypes.CapaClosure, ApprovalDocumentTypes.CapaClosureAliases);
    }

    private static HttpApprovalChainStatusClient CreateClient(string? documentType)
    {
        var httpClient = new HttpClient(new StubHandler(documentType))
        {
            BaseAddress = new Uri("http://approval.test"),
        };
        return new HttpApprovalChainStatusClient(httpClient, new StubTokenProvider());
    }

    private sealed class StubHandler(string? documentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new
            {
                success = true,
                message = string.Empty,
                code = 0,
                data = new
                {
                    chainId = "chain-001",
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    status = "approved",
                    sourceService = "quality",
                    documentType,
                    documentId = "NCR-2026-0001",
                },
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-token";
    }
}
