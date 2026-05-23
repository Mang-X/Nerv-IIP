namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class BusinessFullChainAcceptanceHarnessBaselineTests
{
    private static readonly string[] AllowedHttpMethods = ["GET", "POST", "PUT"];

    [Fact]
    public void Issue77_harness_baseline_defines_seven_chain_surfaces()
    {
        Assert.Equal(7, BusinessFullChainAcceptanceSurface.Chains.Count);
        Assert.All(BusinessFullChainAcceptanceSurface.Chains, chain =>
            Assert.StartsWith("#77 harness baseline:", chain.ChainName, StringComparison.Ordinal));
    }

    [Fact]
    public void Issue77_harness_baseline_required_endpoints_exist_in_public_business_contract_metadata()
    {
        var catalog = PublicBusinessEndpointCatalog.All;

        var missing = BusinessFullChainAcceptanceSurface.Chains
            .SelectMany(chain => chain.RequiredEndpoints.Select(endpoint => new { chain.ChainName, Endpoint = endpoint }))
            .Where(item => !catalog.Contains(item.Endpoint))
            .Select(item => $"{item.ChainName}: {item.Endpoint.HttpMethod} {item.Endpoint.Route} ({item.Endpoint.OperationId}) on {item.Endpoint.Service}")
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void Issue77_harness_baseline_catalog_includes_quality_ncr_contracts()
    {
        var qualityOperationIds = PublicBusinessEndpointCatalog.All
            .Where(endpoint => endpoint.Service == "BusinessQuality")
            .Select(endpoint => endpoint.OperationId)
            .ToArray();

        Assert.Contains("createBusinessQualityNcr", qualityOperationIds);
        Assert.Contains("listBusinessQualityNcrs", qualityOperationIds);
        Assert.Contains("getBusinessQualityNcr", qualityOperationIds);
        Assert.Contains("submitBusinessQualityNcrDisposition", qualityOperationIds);
        Assert.Contains("closeBusinessQualityNcr", qualityOperationIds);
    }

    [Fact]
    public void Issue77_harness_baseline_uses_public_http_surface_only()
    {
        var endpoints = BusinessFullChainAcceptanceSurface.Chains.SelectMany(chain => chain.RequiredEndpoints).ToArray();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint =>
        {
            Assert.StartsWith("/api/", endpoint.Route, StringComparison.Ordinal);
            Assert.Contains(endpoint.HttpMethod, AllowedHttpMethods);
            Assert.Matches("^[a-z][A-Za-z0-9]*$", endpoint.OperationId);
        });
    }
}
