using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OpsCodeAnalysisEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CodeAnalysis_returns_html_with_ops_flow_types()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            InternalServiceAuthentication.DefaultDevelopmentBearerToken);

        using var response = await client.GetAsync("/code-analysis");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.Contains("CreateOperationTaskCommand", body);
        Assert.Contains("OperationTask", body);
    }
}
