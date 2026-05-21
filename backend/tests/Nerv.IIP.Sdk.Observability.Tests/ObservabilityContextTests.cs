using System.Diagnostics;
using Nerv.IIP.Sdk.Observability;

namespace Nerv.IIP.Sdk.Observability.Tests;

public sealed class ObservabilityContextTests
{
    [Fact]
    public void CreateRequestContextGeneratesCorrelationIdWhenOmitted()
    {
        var context = ObservabilityContext.CreateRequestContext("org-1", "prod");

        Assert.Equal("org-1", context.OrganizationId);
        Assert.Equal("prod", context.EnvironmentId);
        Assert.NotNull(context.CorrelationId);
        Assert.NotEmpty(context.CorrelationId);
        Assert.Equal(32, context.CorrelationId.Length);
        Assert.Null(context.IdempotencyKey);
    }

    [Fact]
    public void CreateRequestContextPreservesExplicitCorrelationIdAndIdempotencyKey()
    {
        var context = ObservabilityContext.CreateRequestContext(
            "org-1",
            "prod",
            correlationId: "corr-123",
            idempotencyKey: "idem-456");

        Assert.Equal("corr-123", context.CorrelationId);
        Assert.Equal("idem-456", context.IdempotencyKey);
    }

    [Fact]
    public void CreateRequestContextCapturesCurrentActivityIdAsTraceParent()
    {
        using var activity = new Activity("sdk-observability-test");
        activity.Start();

        var context = ObservabilityContext.CreateRequestContext("org-1", "prod");

        Assert.Equal(activity.Id, context.TraceParent);
    }
}
