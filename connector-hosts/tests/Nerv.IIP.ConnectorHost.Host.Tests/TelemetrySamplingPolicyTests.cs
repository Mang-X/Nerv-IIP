using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Host.Tests;

public sealed class TelemetrySamplingPolicyTests
{
    [Theory]
    [InlineData("sample-10s", 10)]
    [InlineData("sample-1m", 60)]
    [InlineData("10s", 10)]
    [InlineData("1m", 60)]
    [InlineData("bucket=30s;raw=7d;hourly=90d;daily=730d", 30)]
    public void Connector_sampling_policy_parser_derives_bucket_seconds(string samplingPolicy, int expectedBucketSeconds)
    {
        var policy = TelemetrySamplingPolicy.Parse(samplingPolicy);

        Assert.Equal(expectedBucketSeconds, policy.BucketSeconds);
    }
}
