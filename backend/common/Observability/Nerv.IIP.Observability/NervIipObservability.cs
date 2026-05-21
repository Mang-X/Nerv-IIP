using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Sinks.OpenTelemetry;
using SerilogOtlpProtocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Nerv.IIP.Observability.Tests")]

namespace Nerv.IIP.Observability;

public static class NervIipObservabilityRegistration
{
    public static IServiceCollection AddNervIipObservability(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddSingleton(new NervIipObservabilityOptions(serviceName));
        if (GetBoolean(configuration, "OpenTelemetry:Enabled", defaultValue: true))
        {
            services.AddNervIipOpenTelemetry(configuration, serviceName);
        }

        services.AddLogging(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));

            if (!GetBoolean(configuration, "Logging:Serilog:Enabled", defaultValue: true))
            {
                return;
            }

            logging.ClearProviders();
            logging.AddSerilog(CreateSerilogLogger(configuration, serviceName), dispose: true);
        });

        return services;
    }

    public static IApplicationBuilder UseNervIipCorrelation(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var observabilityOptions = context.RequestServices.GetRequiredService<NervIipObservabilityOptions>();
            var correlationId = context.Request.Headers.TryGetValue("X-Correlation-Id", out var header) && !string.IsNullOrWhiteSpace(header)
                ? header.ToString()
                : Guid.NewGuid().ToString("n");
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            Activity.Current?.SetTag("correlationId", correlationId);
            Activity.Current?.SetTag("service.name", observabilityOptions.ServiceName);

            using (context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Nerv.IIP.Correlation").BeginScope(new Dictionary<string, object>
            {
                ["correlationId"] = correlationId,
                ["service.name"] = observabilityOptions.ServiceName,
                ["service"] = observabilityOptions.ServiceName
            }))
            {
                await next();
            }
        });
    }

    private static IServiceCollection AddNervIipOpenTelemetry(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var traceOtlpEndpoint = ReadOtlpEndpoint(configuration, NervIipOpenTelemetrySignal.Traces);
        var hasTraceOtlpEndpoint = Uri.TryCreate(traceOtlpEndpoint, UriKind.Absolute, out _);
        var metricsOtlpEndpoint = ReadOtlpEndpoint(configuration, NervIipOpenTelemetrySignal.Metrics);
        var hasMetricsOtlpEndpoint = Uri.TryCreate(metricsOtlpEndpoint, UriKind.Absolute, out _);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (hasTraceOtlpEndpoint)
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Protocol = ReadOpenTelemetryOtlpProtocol(configuration, traceOtlpEndpoint);
                        options.Endpoint = ResolveOpenTelemetryOtlpEndpoint(configuration, traceOtlpEndpoint, NervIipOpenTelemetrySignal.Traces);
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (hasMetricsOtlpEndpoint)
                {
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Protocol = ReadOpenTelemetryOtlpProtocol(configuration, metricsOtlpEndpoint);
                        options.Endpoint = ResolveOpenTelemetryOtlpEndpoint(configuration, metricsOtlpEndpoint, NervIipOpenTelemetrySignal.Metrics);
                    });
                }
            });

        return services;
    }

    private static Serilog.ILogger CreateSerilogLogger(IConfiguration configuration, string serviceName)
    {
        var formatter = new JsonFormatter(renderMessage: true);
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(ReadMinimumLevel(configuration))
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service.name", serviceName)
            .Enrich.WithProperty("service", serviceName)
            .WriteTo.Console(formatter);

        if (GetBoolean(configuration, "Logging:LocalFile:Enabled", defaultValue: false))
        {
            var logDirectory = configuration["Logging:LocalFile:Directory"];
            if (string.IsNullOrWhiteSpace(logDirectory))
            {
                logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            }

            Directory.CreateDirectory(logDirectory);
            var filePath = Path.Combine(logDirectory, $"{SanitizeFileName(serviceName)}-.jsonl");
            loggerConfiguration.WriteTo.File(
                formatter,
                filePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: GetInteger(configuration, "Logging:LocalFile:RetainedFileCountLimit", 14),
                fileSizeLimitBytes: GetLong(configuration, "Logging:LocalFile:FileSizeLimitBytes", 10 * 1024 * 1024),
                rollOnFileSizeLimit: true,
                shared: true);
        }

        var otlpEndpoint = ReadOtlpEndpoint(configuration, NervIipOpenTelemetrySignal.Logs);

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            loggerConfiguration.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = otlpEndpoint;
                options.Protocol = ReadSerilogOtlpProtocol(configuration, otlpEndpoint);
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = serviceName
                };
            });
        }

        return loggerConfiguration.CreateLogger();
    }

    private static LogEventLevel ReadMinimumLevel(IConfiguration configuration)
    {
        var configuredLevel = FirstNonEmpty(configuration["Logging:Serilog:MinimumLevel"], configuration["Logging:LogLevel:Default"]);
        return Enum.TryParse<LogEventLevel>(configuredLevel, ignoreCase: true, out var level) ? level : LogEventLevel.Information;
    }

    internal static string ReadOtlpEndpoint(IConfiguration configuration, NervIipOpenTelemetrySignal signal)
    {
        var signalSpecificEndpoint = signal switch
        {
            NervIipOpenTelemetrySignal.Traces => FirstNonEmpty(
                Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"),
                configuration["OpenTelemetry:Traces:Endpoint"]),
            NervIipOpenTelemetrySignal.Metrics => FirstNonEmpty(
                Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"),
                configuration["OpenTelemetry:Metrics:Endpoint"]),
            NervIipOpenTelemetrySignal.Logs => FirstNonEmpty(
                Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"),
                configuration["OpenTelemetry:Logs:Endpoint"]),
            _ => string.Empty
        };

        return FirstNonEmpty(
            signalSpecificEndpoint,
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"),
            configuration["OpenTelemetry:Endpoint"],
            configuration["Logging:OpenTelemetry:Endpoint"]);
    }

    internal static SerilogOtlpProtocol ReadSerilogOtlpProtocol(IConfiguration configuration, string endpoint)
    {
        var configuredProtocol = ReadConfiguredOtlpProtocol(configuration);
        if (IsHttpProtobufProtocol(configuredProtocol))
        {
            return SerilogOtlpProtocol.HttpProtobuf;
        }

        if (Enum.TryParse<SerilogOtlpProtocol>(configuredProtocol, ignoreCase: true, out var protocol))
        {
            return protocol;
        }

        return endpoint.Contains("4318", StringComparison.OrdinalIgnoreCase) || endpoint.Contains("/v1/logs", StringComparison.OrdinalIgnoreCase)
            ? SerilogOtlpProtocol.HttpProtobuf
            : SerilogOtlpProtocol.Grpc;
    }

    internal static OtlpExportProtocol ReadOpenTelemetryOtlpProtocol(IConfiguration configuration, string endpoint)
    {
        var configuredProtocol = ReadConfiguredOtlpProtocol(configuration);
        if (IsHttpProtobufProtocol(configuredProtocol))
        {
            return OtlpExportProtocol.HttpProtobuf;
        }

        if (Enum.TryParse<OtlpExportProtocol>(configuredProtocol, ignoreCase: true, out var protocol))
        {
            return protocol;
        }

        return endpoint.Contains("4318", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("/v1/traces", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("/v1/metrics", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("/v1/logs", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
    }

    internal static Uri ResolveOpenTelemetryOtlpEndpoint(IConfiguration configuration, string endpoint, NervIipOpenTelemetrySignal signal)
    {
        var endpointUri = new Uri(endpoint, UriKind.Absolute);
        if (ReadOpenTelemetryOtlpProtocol(configuration, endpoint) != OtlpExportProtocol.HttpProtobuf)
        {
            return endpointUri;
        }

        var signalPath = signal switch
        {
            NervIipOpenTelemetrySignal.Traces => "/v1/traces",
            NervIipOpenTelemetrySignal.Metrics => "/v1/metrics",
            NervIipOpenTelemetrySignal.Logs => "/v1/logs",
            _ => string.Empty
        };

        var builder = new UriBuilder(endpointUri);
        var path = builder.Path.TrimEnd('/');
        path = TrimKnownOtlpSignalPath(path);
        builder.Path = string.IsNullOrWhiteSpace(path) || path == "/"
            ? signalPath
            : $"{path}{signalPath}";
        return builder.Uri;
    }

    private static string ReadConfiguredOtlpProtocol(IConfiguration configuration)
    {
        return FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL"),
            configuration["OpenTelemetry:Protocol"],
            configuration["Logging:OpenTelemetry:Protocol"]);
    }

    private static bool IsHttpProtobufProtocol(string value)
    {
        return value.Equals("HttpProtobuf", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Http", StringComparison.OrdinalIgnoreCase)
            || value.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
            || value.Equals("http_protobuf", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimKnownOtlpSignalPath(string path)
    {
        foreach (var suffix in new[] { "/v1/traces", "/v1/metrics", "/v1/logs" })
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return path[..^suffix.Length];
            }
        }

        return path;
    }

    private static bool GetBoolean(IConfiguration configuration, string key, bool defaultValue)
    {
        return bool.TryParse(configuration[key], out var value) ? value : defaultValue;
    }

    private static int GetInteger(IConfiguration configuration, string key, int defaultValue)
    {
        return int.TryParse(configuration[key], out var value) && value > 0 ? value : defaultValue;
    }

    private static long GetLong(IConfiguration configuration, string key, long defaultValue)
    {
        return long.TryParse(configuration[key], out var value) && value > 0 ? value : defaultValue;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalidCharacters.Contains(character) ? '-' : character).ToArray());
    }
}

public sealed record NervIipObservabilityOptions(string ServiceName);

internal enum NervIipOpenTelemetrySignal
{
    Traces,
    Metrics,
    Logs
}
