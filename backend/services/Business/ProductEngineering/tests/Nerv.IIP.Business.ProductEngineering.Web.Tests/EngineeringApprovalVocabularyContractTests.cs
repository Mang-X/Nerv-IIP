using System.Net;
using System.Text;
using System.Text.Json;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class EngineeringApprovalVocabularyContractTests
{
    /// <summary>
    /// #1857 变异哨兵：桩里的 <c>status</c> 是**写死的字面量** <c>"approved"</c>，
    /// 生产代码比的是契约常量 <c>ApprovalChainStatuses.Approved</c>——
    /// 把契约常量改成事故值，本用例立刻红（其余用例本就期望抛异常，抓不住这一变异）。
    /// </summary>
    [Fact]
    public async Task Release_consumer_accepts_the_matching_approved_chain()
    {
        var verifier = CreateVerifier(ApprovalTemplateCodes.EngineeringChangeOrder, "approved");

        await verifier.EnsureApprovedAsync(
            "org-001",
            "env-dev",
            "0190c0b4-3d3b-7f41-bf6a-0e9a6d5a0001",
            "ECO-20260801-000001",
            CancellationToken.None);
    }

    /// <summary>
    /// 审批链状态是链状态词表（含 <c>pending</c> / <c>withdrawn</c> 两个「审批结果」词表
    /// 根本没有的成员），非 <c>approved</c> 一律不得放行发布。
    /// </summary>
    [Theory]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("returned")]
    [InlineData("withdrawn")]
    public async Task Release_consumer_rejects_a_chain_that_is_not_approved(string status)
    {
        var verifier = CreateVerifier(ApprovalTemplateCodes.EngineeringChangeOrder, status);

        var exception = await Assert.ThrowsAsync<KnownException>(() => verifier.EnsureApprovedAsync(
            "org-001",
            "env-dev",
            "0190c0b4-3d3b-7f41-bf6a-0e9a6d5a0001",
            "ECO-20260801-000001",
            CancellationToken.None));

        Assert.Contains("same ECO document", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Release_consumer_rejects_a_chain_with_a_different_template_code()
    {
        var verifier = CreateVerifier(ApprovalTemplateCodes.PurchaseOrderRelease, "approved");

        var exception = await Assert.ThrowsAsync<KnownException>(() => verifier.EnsureApprovedAsync(
            "org-001",
            "env-dev",
            "0190c0b4-3d3b-7f41-bf6a-0e9a6d5a0001",
            "ECO-20260801-000001",
            CancellationToken.None));

        Assert.Contains("same ECO document", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpEngineeringApprovalVerifier CreateVerifier(string templateCode, string status) =>
        new(
            new HttpClient(new StubHandler(templateCode, status))
            {
                BaseAddress = new Uri("http://approval.test"),
            },
            new StubTokenProvider());

    private sealed class StubHandler(string templateCode, string status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.Serialize(new
            {
                success = true,
                message = string.Empty,
                code = 0,
                data = new
                {
                    chainId = "0190c0b4-3d3b-7f41-bf6a-0e9a6d5a0001",
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    status,
                    templateCode,
                    sourceService = "product-engineering",
                    documentType = "engineering-change-order",
                    documentId = "ECO-20260801-000001",
                },
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-token";
    }
}
