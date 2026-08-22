using System.Net;
using Nerv.IIP.Business.Erp.Web.Application.Approval;
using Nerv.IIP.Business.Erp.Web.Application.MasterData;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpKnownExceptionDynamicBoundaryTests
{
    [Fact]
    public async Task Approval_client_does_not_preserve_downstream_message()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "{\"data\":null,\"success\":false,\"message\":\"downstream internal exception\",\"code\":500}",
            "application/json"))
        {
            BaseAddress = new Uri("http://approval.test"),
        };
        var client = new HttpPurchaseOrderApprovalClient(httpClient, new TestInternalServiceTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => client.StartApprovalAsync(
            new PurchaseOrderApprovalRequest(
                "org-001",
                "env-dev",
                "erp-purchase-order-release",
                "business-erp",
                "purchase-order",
                "PO-001",
                null,
                "system:test",
                "chain-001"),
            CancellationToken.None));

        Assert.Equal("审批服务未返回审批链，请稍后重试。", exception.Message);
        Assert.DoesNotContain("downstream", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Credit_profile_reader_does_not_preserve_failed_envelope_message()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            HttpStatusCode.OK,
            "{\"data\":null,\"success\":false,\"message\":\"stack trace leaked\",\"code\":500}",
            "application/json"))
        {
            BaseAddress = new Uri("http://masterdata.test"),
        };
        var reader = new HttpCustomerCreditProfileReader(httpClient, new TestInternalServiceTokenProvider());

        var exception = await Assert.ThrowsAsync<KnownException>(() => reader.GetAsync(
            "org-001",
            "env-dev",
            "CUST-001",
            CancellationToken.None));

        Assert.Equal("客户『CUST-001』的信用额度主数据不可用，请先维护客户信用额度。", exception.Message);
        Assert.DoesNotContain("stack trace", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
