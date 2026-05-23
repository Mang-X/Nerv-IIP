using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubCodeAnalysisEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CodeAnalysis_returns_html_with_apphub_flow_types()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/code-analysis");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.Contains("RegisterApplicationCommand", body);
        Assert.Contains("ApplicationInstance", body);
    }
}
