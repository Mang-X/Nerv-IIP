using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayWmsTrustedCompletionContractTests
{
    [Fact]
    public void Public_completion_requests_accept_scope_and_version_but_not_trusted_identity()
    {
        foreach (var requestType in new[]
                 {
                     typeof(BusinessConsoleCompleteWmsInboundOrderRequest),
                     typeof(BusinessConsoleCompleteWmsOutboundOrderRequest),
                     typeof(BusinessConsoleCompleteWmsCountExecutionRequest),
                 })
        {
            Assert.NotNull(requestType.GetProperty("ScopeKind"));
            Assert.NotNull(requestType.GetProperty("ScopeId"));
            Assert.NotNull(requestType.GetProperty("ExpectedVersion"));
            Assert.Null(requestType.GetProperty("ActorPrincipalId"));
            Assert.Null(requestType.GetProperty("AuthorizedSiteCodes"));
        }
    }
}
