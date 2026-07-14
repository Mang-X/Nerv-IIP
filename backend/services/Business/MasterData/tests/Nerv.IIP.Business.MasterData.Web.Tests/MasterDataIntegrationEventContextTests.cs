using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Security.Claims;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataIntegrationEventContextTests
{
    [Fact]
    public void Http_context_accessor_ignores_untrusted_actor_header()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Correlation-Id"] = "corr-http-001";
        httpContext.Request.Headers["X-Causation-Id"] = "cmd-http-001";
        httpContext.Request.Headers["X-Actor"] = "user:planner-001";
        var accessor = new HttpMasterDataIntegrationEventContextAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        var context = accessor.GetContext();

        Assert.Equal("corr-http-001", context.CorrelationId);
        Assert.Equal("cmd-http-001", context.CausationId);
        Assert.Equal("system:business-masterdata", context.Actor);
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

        var context = accessor.GetContext();

        Assert.Equal("corr-http-002", context.CorrelationId);
        Assert.NotEmpty(context.CausationId);
        Assert.Equal("user:user-001", context.Actor);
    }

    [Fact]
    public void Http_context_accessor_prefers_authenticated_subject_over_actor_header()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "user-001")],
                "test"))
        };
        httpContext.Request.Headers["X-Correlation-Id"] = "corr-http-003";
        httpContext.Request.Headers["X-Actor"] = "user:spoofed";
        var accessor = new HttpMasterDataIntegrationEventContextAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        var context = accessor.GetContext();

        Assert.Equal("user:user-001", context.Actor);
    }

    [Fact]
    public void Http_context_accessor_uses_activity_correlation_tag_before_generating_fallback()
    {
        using var activity = new Activity("masterdata-test").Start();
        activity.SetTag("correlationId", "corr-activity-001");
        var accessor = new HttpMasterDataIntegrationEventContextAccessor(new HttpContextAccessor());

        var context = accessor.GetContext();

        Assert.Equal("corr-activity-001", context.CorrelationId);
    }
}
