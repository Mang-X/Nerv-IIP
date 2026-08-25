using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.Security.Claims;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using NetCorePal.Extensions.Primitives;

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

    [Fact]
    public void Internal_service_without_canonical_forwarded_actor_is_not_trusted_for_user_audit()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("token_type", "internal_service"),
                    new Claim(ClaimTypes.NameIdentifier, "business-gateway"),
                ],
                "test"))
        };
        var accessor = new HttpToolingOperationAuditContextAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        Assert.Throws<KnownException>(() => accessor.GetRequiredContext());
    }

    [Fact]
    public void Unauthenticated_subject_claim_is_not_trusted_for_user_audit()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "untrusted-user")]))
        };
        var accessor = new HttpMasterDataIntegrationEventContextAccessor(new HttpContextAccessor
        {
            HttpContext = httpContext
        });

        var context = accessor.GetContext();

        Assert.Equal("system:business-masterdata", context.Actor);
    }

    [Theory]
    [InlineData("actor", "bearer:SENTINEL-TOKEN")]
    [InlineData("actor", "user:valid\u0001suffix")]
    [InlineData("correlation", "password=SENTINEL")]
    [InlineData("causation", "connection-string-SENTINEL")]
    [InlineData("operation", "authorization-SENTINEL")]
    public void Tooling_context_rejects_unapproved_actor_or_sensitive_identity(string field, string invalidValue)
    {
        var actor = field == "actor" ? invalidValue : "user:operator-001";
        var correlation = field == "correlation" ? invalidValue : "corr-001";
        var causation = field == "causation" ? invalidValue : "cause-001";
        var operation = field == "operation" ? invalidValue : "operation-001";

        Assert.Throws<KnownException>(() => ToolingOperationAuditContext.CreateFromTrustedBoundary(
            actor,
            correlation,
            causation,
            operation));
    }

    [Fact]
    public void Tooling_context_rejects_overlong_identity()
    {
        Assert.Throws<KnownException>(() => ToolingOperationAuditContext.CreateFromTrustedBoundary(
            "user:operator-001",
            new string('x', 201),
            "cause-001",
            "operation-001"));
    }
}
