using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OpsSkeletonTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Ops_service_exposes_only_health_in_first_iteration()
    {
        var client = factory.CreateClient();

        Assert.Equal("Healthy", await client.GetStringAsync("/health"));
        Assert.Contains("first-iteration-skeleton", await client.GetStringAsync("/internal/ops/v1/build-info"));
    }
}
