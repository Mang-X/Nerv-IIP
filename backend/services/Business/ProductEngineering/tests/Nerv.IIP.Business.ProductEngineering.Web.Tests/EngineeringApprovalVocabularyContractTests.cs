using System.Net;
using System.Text;
using System.Text.Json;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class EngineeringApprovalVocabularyContractTests
{
    [Fact]
    public async Task Release_consumer_rejects_a_chain_with_a_different_template_code()
    {
        var verifier = new HttpEngineeringApprovalVerifier(
            new HttpClient(new StubHandler("APT-WB-PO-001"))
            {
                BaseAddress = new Uri("http://approval.test"),
            },
            new StubTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => verifier.EnsureApprovedAsync(
            "org-001",
            "env-dev",
            "0190c0b4-3d3b-7f41-bf6a-0e9a6d5a0001",
            "ECO-20260801-000001",
            CancellationToken.None));

        Assert.Contains("same ECO document", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHandler(string templateCode) : HttpMessageHandler
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
                    status = "approved",
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
