using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OpsCodeAnalysisEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CodeAnalysis_returns_html_with_ops_flow_types()
    {
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/code-analysis");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.Contains("CreateOperationTaskCommand", body);
        Assert.Contains("OperationTask", body);
    }
}
