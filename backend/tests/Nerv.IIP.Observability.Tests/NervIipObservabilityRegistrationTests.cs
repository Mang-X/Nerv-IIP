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
    public void ResolveOpenTelemetryOtlpEndpoint_ShouldApplyConfiguredLogsPathForVictoriaLogs()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["OpenTelemetry:Protocol"] = "HttpProtobuf",
            ["OpenTelemetry:Logs:Path"] = "/insert/opentelemetry/v1/logs"
        });

        var logsEndpoint = NervIipObservabilityRegistration.ResolveOpenTelemetryOtlpEndpoint(
            configuration,
            "http://victoria-logs:9428",
            NervIipOpenTelemetrySignal.Logs);

        Assert.Equal(new Uri("http://victoria-logs:9428/insert/opentelemetry/v1/logs"), logsEndpoint);
    }

    [Fact]
    public void VictoriaLogs_options_should_generate_endpoint_retention_and_resource_parameters()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["VictoriaLogs:BaseUrl"] = "http://victoria-logs:9428",
            ["VictoriaLogs:RetentionPeriod"] = "30d",
            ["VictoriaLogs:StorageDataPath"] = "/victoria-logs-data"
        });

        var options = VictoriaLogsOptions.FromConfiguration(configuration, "platform-gateway");

        Assert.Equal(new Uri("http://victoria-logs:9428/insert/opentelemetry/v1/logs"), options.OtlpLogsEndpoint);
        Assert.Equal(new Uri("http://victoria-logs:9428/select/logsql/query"), options.QueryEndpoint);
        Assert.Equal("30d", options.RetentionPeriod);
        Assert.Equal("/victoria-logs-data", options.StorageDataPath);
        Assert.Contains("-retentionPeriod=30d", options.ToCommandLineArgs());
        Assert.Contains("-storageDataPath=/victoria-logs-data", options.ToCommandLineArgs());
        Assert.Equal("platform-gateway", options.ResourceAttributes["service.name"]);
    }

    [Fact]
    public void VictoriaLogs_query_builder_should_generate_safe_logs_query_without_database_storage()
    {
        var request = new VictoriaLogsQueryRequest(
            DateTimeOffset.Parse("2026-06-10T01:00:00Z"),
            DateTimeOffset.Parse("2026-06-10T02:00:00Z"),
            50,
            0,
            new VictoriaLogsQueryFilter(
                "platform-gateway",
                "corr-001\" | delete",
                "trace-001",
                "Error",
                "timeout while calling IAM"));

        var form = VictoriaLogsQueryBuilder.BuildForm(request);

        Assert.Equal("2026-06-10T01:00:00.0000000+00:00", form["start"]);
        Assert.Equal("2026-06-10T02:00:00.0000000+00:00", form["end"]);
        Assert.Equal("50", form["limit"]);
        Assert.Equal("0", form["offset"]);
        Assert.Contains("service:=\"platform-gateway\"", form["query"]);
        Assert.Contains("correlationId:=\"corr-001\\\" | delete\"", form["query"]);
        Assert.Contains("traceId:=\"trace-001\"", form["query"]);
        Assert.Contains("level:=\"Error\"", form["query"]);
        Assert.Contains("\"timeout while calling IAM\"", form["query"]);
        Assert.Contains("fields _time, level, service, _msg", form["query"]);

        var projectText = File.ReadAllText(FindObservabilityProjectPath());
        var sourceText = File.ReadAllText(FindObservabilitySourcePath());
        Assert.DoesNotContain("EntityFrameworkCore", projectText);
        Assert.DoesNotContain("Npgsql", projectText);
        Assert.DoesNotContain("DbContext", sourceText);
    }

    [Fact]
    public void VictoriaLogs_query_builder_should_use_match_all_for_time_only_query()
    {
        var request = new VictoriaLogsQueryRequest(
            DateTimeOffset.Parse("2026-06-10T01:00:00Z"),
            DateTimeOffset.Parse("2026-06-10T02:00:00Z"),
            10,
            0,
            new VictoriaLogsQueryFilter(null, null, null, null, null));

        var form = VictoriaLogsQueryBuilder.BuildForm(request);

        Assert.StartsWith("* | fields", form["query"], StringComparison.Ordinal);
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

    private static string FindObservabilityProjectPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "backend",
                "common",
                "Observability",
                "Nerv.IIP.Observability",
                "Nerv.IIP.Observability.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find Nerv.IIP.Observability.csproj.");
    }

    private static string FindObservabilitySourcePath()
    {
        return Path.Combine(Path.GetDirectoryName(FindObservabilityProjectPath())!, "NervIipObservability.cs");
    }
}
