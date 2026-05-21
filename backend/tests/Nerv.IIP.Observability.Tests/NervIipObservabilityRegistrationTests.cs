using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Observability;
using OpenTelemetry.Exporter;
using Serilog.Sinks.OpenTelemetry;

namespace Nerv.IIP.Observability.Tests;

public sealed class NervIipObservabilityRegistrationTests
{
    [Fact]
    public void AddNervIipObservability_ShouldRegisterServiceNameOptions()
    {
        var services = new ServiceCollection();

        services.AddNervIipObservability(CreateConfiguration(), "unit-test-service");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<NervIipObservabilityOptions>();
        Assert.Equal("unit-test-service", options.ServiceName);
    }

    [Fact]
    public void AddNervIipObservability_ShouldRegisterOpenTelemetryHostedServiceByDefault()
    {
        var services = new ServiceCollection();

        services.AddNervIipObservability(CreateConfiguration(), "unit-test-service");

        Assert.Contains(services, IsOpenTelemetryHostedServiceRegistration);
    }

    [Fact]
    public void AddNervIipObservability_ShouldAllowDisablingOpenTelemetry()
    {
        var services = new ServiceCollection();

        services.AddNervIipObservability(
            CreateConfiguration(new Dictionary<string, string?>
            {
                ["OpenTelemetry:Enabled"] = "false"
            }),
            "unit-test-service");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<NervIipObservabilityOptions>();
        Assert.Equal("unit-test-service", options.ServiceName);
        Assert.DoesNotContain(services, IsOpenTelemetryHostedServiceRegistration);
    }

    [Fact]
    public void ResolveOpenTelemetryOtlpEndpoint_ShouldAppendSignalPathsForHttpProtobufBaseEndpoint()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenTelemetry:Protocol"] = "HttpProtobuf"
        });

        var tracesEndpoint = NervIipObservabilityRegistration.ResolveOpenTelemetryOtlpEndpoint(
            configuration,
            "http://localhost:4318",
            NervIipOpenTelemetrySignal.Traces);
        var metricsEndpoint = NervIipObservabilityRegistration.ResolveOpenTelemetryOtlpEndpoint(
            configuration,
            "http://localhost:4318/",
            NervIipOpenTelemetrySignal.Metrics);

        Assert.Equal(new Uri("http://localhost:4318/v1/traces"), tracesEndpoint);
        Assert.Equal(new Uri("http://localhost:4318/v1/metrics"), metricsEndpoint);
    }

    [Fact]
    public void ResolveOpenTelemetryOtlpEndpoint_ShouldReplaceMismatchedSignalPathForHttpProtobuf()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenTelemetry:Protocol"] = "http/protobuf"
        });

        var tracesEndpoint = NervIipObservabilityRegistration.ResolveOpenTelemetryOtlpEndpoint(
            configuration,
            "http://collector:4318/v1/logs",
            NervIipOpenTelemetrySignal.Traces);

        Assert.Equal(new Uri("http://collector:4318/v1/traces"), tracesEndpoint);
    }

    [Fact]
    public void ResolveOpenTelemetryOtlpEndpoint_ShouldPreserveGrpcEndpoint()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenTelemetry:Protocol"] = "grpc"
        });

        var endpoint = NervIipObservabilityRegistration.ResolveOpenTelemetryOtlpEndpoint(
            configuration,
            "http://collector:4317",
            NervIipOpenTelemetrySignal.Traces);

        Assert.Equal(new Uri("http://collector:4317"), endpoint);
    }

    [Fact]
    public void ReadOpenTelemetryOtlpProtocol_ShouldSupportStandardHttpProtobufValue()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenTelemetry:Protocol"] = "http/protobuf"
        });

        var protocol = NervIipObservabilityRegistration.ReadOpenTelemetryOtlpProtocol(configuration, "http://collector:9999");

        Assert.Equal(OtlpExportProtocol.HttpProtobuf, protocol);
    }

    [Fact]
    public void ReadSerilogOtlpProtocol_ShouldSupportStandardHttpProtobufValue()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenTelemetry:Protocol"] = "http/protobuf"
        });

        var protocol = NervIipObservabilityRegistration.ReadSerilogOtlpProtocol(configuration, "http://collector:9999");

        Assert.Equal(OtlpProtocol.HttpProtobuf, protocol);
    }

    [Fact]
    public void ReadOpenTelemetryOtlpProtocol_ShouldPreferOtelProtocolEnvironmentVariable()
    {
        var previousValue = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL");
        try
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
            var configuration = CreateConfiguration(new Dictionary<string, string?>
            {
                ["OpenTelemetry:Protocol"] = "grpc"
            });

            var protocol = NervIipObservabilityRegistration.ReadOpenTelemetryOtlpProtocol(configuration, "http://collector:9999");

            Assert.Equal(OtlpExportProtocol.HttpProtobuf, protocol);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL", previousValue);
        }
    }

    private static bool IsOpenTelemetryHostedServiceRegistration(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType.FullName == "Microsoft.Extensions.Hosting.IHostedService"
            && descriptor.ImplementationType?.FullName?.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }
}
