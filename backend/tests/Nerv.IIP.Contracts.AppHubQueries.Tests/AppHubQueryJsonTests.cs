using System.Text.Json;
using Nerv.IIP.Contracts.AppHubQueries;

namespace Nerv.IIP.Contracts.AppHubQueries.Tests;

public sealed class AppHubQueryJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void InstanceListQuery_round_trips_with_unified_pagination_fields()
    {
        var source = new InstanceListQuery(
            "org-001",
            "env-dev",
            2,
            10,
            "instanceName",
            "desc",
            "demo");

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<InstanceListQuery>(json, JsonOptions);

        Assert.Contains("\"pageIndex\":2", json);
        Assert.Contains("\"filterSearch\":\"demo\"", json);
        Assert.NotNull(result);
        Assert.Equal(2, result.PageIndex);
        Assert.Equal("instanceName", result.SortBy);
        Assert.Equal("desc", result.SortOrder);
        Assert.Equal("demo", result.FilterSearch);
    }

    [Fact]
    public void InstanceDetailResponse_round_trips_with_web_json_options()
    {
        var source = new InstanceDetailResponse(
            "demo-api",
            "Demo API",
            "1.0.0",
            "node-001",
            "local-docker",
            "demo-api-001",
            "demo-api",
            "running",
            "healthy",
            DateTimeOffset.Parse("2026-05-14T00:00:00Z"),
            DateTimeOffset.Parse("2026-05-14T00:00:10Z"),
            [new CapabilitySummary("lifecycle.restart", "1.0", "lifecycle", ["restart"])],
            new Dictionary<string, string> { ["containerId"] = "abc123" });

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<InstanceDetailResponse>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("demo-api-001", result.InstanceKey);
        Assert.Equal("running", result.ReportedStatus);
        Assert.Equal("lifecycle.restart", result.Capabilities.Single().CapabilityCode);
    }
}
