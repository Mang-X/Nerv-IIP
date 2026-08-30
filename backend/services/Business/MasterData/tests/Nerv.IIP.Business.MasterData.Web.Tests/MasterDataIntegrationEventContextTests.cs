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
        var accessor = new ToolingOperationAuditContext.ToolingAuditSafeText.HttpAdmission(new HttpContextAccessor
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
    [InlineData("actor", "user:eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhIn0.c2lnbmF0dXJl")]
    [InlineData("correlation", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhIn0.c2lnbmF0dXJl")]
    public void Tooling_context_rejects_unapproved_actor_or_sensitive_identity(string field, string invalidValue)
    {
        var actor = field == "actor" ? invalidValue : "user:operator-001";
        var correlation = field == "correlation" ? invalidValue : "corr-001";
        var causation = field == "causation" ? invalidValue : "cause-001";
        var operation = field == "operation" ? invalidValue : "operation-001";

        var admission = CreateToolingAdmission(actor, correlation, causation, operation);

        Assert.Throws<KnownException>(() => admission.GetRequiredContext());
    }

    [Fact]
    public void Tooling_context_rejects_overlong_identity()
    {
        var admission = CreateToolingAdmission(correlationId: new string('x', 201));

        Assert.Throws<KnownException>(() => admission.GetRequiredContext());
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("correlation")]
    [InlineData("causation")]
    [InlineData("operation")]
    public void Tooling_context_rejects_the_current_opaque_authorization_credential(string field)
    {
        const string credential = "opaque-current-credential-7ff1";
        var admission = CreateToolingAdmission(
            field == "actor" ? $"user:{credential}" : "user:operator-001",
            field == "correlation" ? credential : "corr-001",
            field == "causation" ? credential : "cause-001",
            field == "operation" ? credential : "operation-001",
            credential);

        Assert.Throws<KnownException>(() => admission.GetRequiredContext());
    }

    [Fact]
    public void Tooling_admission_allows_plain_language_that_only_mentions_sensitive_terms()
    {
        var admission = CreateToolingAdmission(
            "user:password-rotation",
            "authorization-plan",
            "secret-review",
            "bearer-migration");
        _ = admission.GetRequiredContext();

        Assert.Equal(
            "password rotation planned",
            admission.RequireAuditSafeText(" password rotation planned ", "reason").Value);
    }

    [Theory]
    [InlineData("PASSWORD : changed-value")]
    [InlineData("token=another-value")]
    [InlineData("bEaReR another-opaque-value")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhIn0.c2lnbmF0dXJl")]
    [InlineData("Database=next;HOST=db;Username=changed")]
    [InlineData("User ID=changed;Initial Catalog=next;Server=db")]
    [InlineData("postgresql://svc:pass@db/prod")]
    [InlineData("POSTGRES://other:changed@next/catalog")]
    [InlineData("planned\u0001service")]
    public void Tooling_admission_rejects_bounded_sensitive_text_categories(string value)
    {
        Assert.Throws<KnownException>(() =>
            CreateToolingAdmission().RequireAuditSafeText(value, "reason"));
    }

    [Fact]
    public void Tooling_admission_rejects_actual_bearer_and_overlong_audit_text()
    {
        const string credential = "opaque-current-credential-7ff1";
        var admission = CreateToolingAdmission(credential: credential);

        Assert.Throws<KnownException>(() => admission.RequireAuditSafeText(credential, "reason"));
        Assert.Throws<KnownException>(() => admission.RequireAuditSafeText(new string('x', 501), "reason"));
    }

    private static IToolingOperationAdmission CreateToolingAdmission(
        string actor = "user:operator-001",
        string correlationId = "corr-001",
        string causationId = "cause-001",
        string operationId = "operation-001",
        string credential = "admission-test-credential")
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("token_type", "internal_service")],
                "test"))
        };
        httpContext.Request.Headers["Authorization"] = $"Bearer {credential}";
        httpContext.Request.Headers["X-Authenticated-Actor"] = actor;
        httpContext.Request.Headers["X-Correlation-Id"] = correlationId;
        httpContext.Request.Headers["X-Causation-Id"] = causationId;
        httpContext.Request.Headers["X-Idempotency-Key"] = operationId;
        return new ToolingOperationAuditContext.ToolingAuditSafeText.HttpAdmission(new HttpContextAccessor
        {
            HttpContext = httpContext
        });
    }
}
