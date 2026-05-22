using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataIntegrationEventContextTests
{
    [Fact]
    public void Http_context_accessor_reads_correlation_causation_and_actor_headers()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Correlation-Id"] = "corr-http-001";
        httpContext.Request.Headers["X-Causation-Id"] = "cmd-http-001";
        httpContext.Request.Headers["X-Actor"] = "user:planner-001";
        var accessor = new HttpMasterDataIntegrationEventContextAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        var context = accessor.GetContext("fallback-cause");

        Assert.Equal("corr-http-001", context.CorrelationId);
        Assert.Equal("cmd-http-001", context.CausationId);
        Assert.Equal("user:planner-001", context.Actor);
    }

    [Fact]
    public void Http_context_accessor_uses_authenticated_subject_when_actor_header_is_missing()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "user-001")],
                "test"))
        };
        httpContext.Request.Headers["X-Correlation-Id"] = "corr-http-002";
        var accessor = new HttpMasterDataIntegrationEventContextAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        var context = accessor.GetContext("cmd-fallback-002");

        Assert.Equal("corr-http-002", context.CorrelationId);
        Assert.Equal("cmd-fallback-002", context.CausationId);
        Assert.Equal("user:user-001", context.Actor);
    }
}
