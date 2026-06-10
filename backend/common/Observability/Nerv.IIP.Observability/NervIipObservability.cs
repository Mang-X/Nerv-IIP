using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
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

    public static IServiceCollection AddVictoriaLogsClient(this IServiceCollection services, IConfiguration configuration)
    {
        var options = VictoriaLogsOptions.FromConfiguration(configuration, "platform-gateway");
        services.AddSingleton(options);
        services.AddHttpClient<IVictoriaLogsClient, VictoriaLogsClient>(client =>
        {
            client.BaseAddress = options.BaseUrl;
        });
        return services;
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
            var resolvedLogsEndpoint = ResolveOpenTelemetryOtlpEndpoint(configuration, otlpEndpoint, NervIipOpenTelemetrySignal.Logs);
            loggerConfiguration.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = resolvedLogsEndpoint.ToString();
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
        var configuredPath = configuration[$"OpenTelemetry:{signal}:Path"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var customBuilder = new UriBuilder(endpointUri)
            {
                Path = configuredPath.TrimStart('/')
            };
            return customBuilder.Uri;
        }

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

public sealed record VictoriaLogsOptions(
    Uri BaseUrl,
    string RetentionPeriod,
    string StorageDataPath,
    IReadOnlyDictionary<string, string> ResourceAttributes)
{
    public Uri OtlpLogsEndpoint => new(BaseUrl, "/insert/opentelemetry/v1/logs");
    public Uri QueryEndpoint => new(BaseUrl, "/select/logsql/query");

    public static VictoriaLogsOptions FromConfiguration(IConfiguration configuration, string serviceName)
    {
        var baseUrl = configuration["VictoriaLogs:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://victoria-logs:9428";
        }

        return new VictoriaLogsOptions(
            new Uri(baseUrl, UriKind.Absolute),
            string.IsNullOrWhiteSpace(configuration["VictoriaLogs:RetentionPeriod"]) ? "30d" : configuration["VictoriaLogs:RetentionPeriod"]!,
            string.IsNullOrWhiteSpace(configuration["VictoriaLogs:StorageDataPath"]) ? "/victoria-logs-data" : configuration["VictoriaLogs:StorageDataPath"]!,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["service.name"] = serviceName,
                ["service"] = serviceName
            });
    }

    public string[] ToCommandLineArgs() =>
    [
        $"-storageDataPath={StorageDataPath}",
        $"-retentionPeriod={RetentionPeriod}"
    ];
}

public sealed record VictoriaLogsQueryFilter(
    string? Service,
    string? CorrelationId,
    string? TraceId,
    string? Level,
    string? Text);

public sealed record VictoriaLogsQueryRequest(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int Limit,
    int Offset,
    VictoriaLogsQueryFilter Filter);

public sealed record VictoriaLogsLogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Service,
    string Message,
    string? InstanceKey,
    string? OperationTaskId,
    string? CorrelationId,
    string? TraceId,
    string Source,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyDictionary<string, string> Fields);

public sealed record VictoriaLogsQueryResponse(
    IReadOnlyList<VictoriaLogsLogEntry> Items,
    int? NextOffset,
    bool Partial,
    string BackendStatus);

public interface IVictoriaLogsClient
{
    Task<VictoriaLogsQueryResponse> QueryAsync(VictoriaLogsQueryRequest request, CancellationToken cancellationToken);
}

public static class VictoriaLogsQueryBuilder
{
    public static IReadOnlyDictionary<string, string> BuildForm(VictoriaLogsQueryRequest request)
    {
        var predicates = new List<string>();
        AddDualFieldFilter(predicates, "service", "service.name", request.Filter.Service);
        AddExactFilter(predicates, "correlationId", request.Filter.CorrelationId);
        AddDualFieldFilter(predicates, "traceId", "trace_id", request.Filter.TraceId);
        AddDualFieldFilter(predicates, "level", "severity_text", request.Filter.Level);
        AddPhraseFilter(predicates, request.Filter.Text);

        var queryParts = predicates.Count == 0 ? new List<string> { "*" } : predicates;
        queryParts.Add("| fields _time, level, service, _msg, message, instanceKey, operationTaskId, correlationId, traceId, severity_text, \"service.name\", trace_id");
        queryParts.Add("| sort by (_time desc)");

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["query"] = string.Join(' ', queryParts.Where(filter => !string.IsNullOrWhiteSpace(filter))),
            ["start"] = request.FromUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["end"] = request.ToUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["limit"] = request.Limit.ToString(CultureInfo.InvariantCulture),
            ["offset"] = request.Offset.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static void AddDualFieldFilter(List<string> filters, string firstField, string secondField, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        filters.Add($"({FormatField(firstField)}:={Quote(value)} OR {FormatField(secondField)}:={Quote(value)})");
    }

    private static void AddExactFilter(List<string> filters, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            filters.Add($"{FormatField(field)}:={Quote(value)}");
        }
    }

    private static void AddPhraseFilter(List<string> filters, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            filters.Add(Quote(value));
        }
    }

    private static string FormatField(string field) =>
        field.All(character => char.IsLetterOrDigit(character) || character is '_' or '.')
            ? field
            : Quote(field);

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

internal sealed class VictoriaLogsClient(HttpClient httpClient, VictoriaLogsOptions options) : IVictoriaLogsClient
{
    public async Task<VictoriaLogsQueryResponse> QueryAsync(VictoriaLogsQueryRequest request, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(VictoriaLogsQueryBuilder.BuildForm(request));
        using var response = await httpClient.PostAsync(options.QueryEndpoint.PathAndQuery, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var entries = new List<VictoriaLogsLogEntry>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            entries.Add(ParseEntry(line));
        }

        int? nextOffset = entries.Count == request.Limit ? request.Offset + entries.Count : null;
        return new VictoriaLogsQueryResponse(entries, nextOffset, false, "victoriaLogs");
    }

    private static VictoriaLogsLogEntry ParseEntry(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var fields = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.ToString(), StringComparer.Ordinal);
        var timestamp = DateTimeOffset.TryParse(Get(fields, "_time"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UnixEpoch;
        var level = FirstNonEmpty(Get(fields, "level"), Get(fields, "severity_text"));
        var service = FirstNonEmpty(Get(fields, "service"), Get(fields, "service.name"));
        var message = FirstNonEmpty(Get(fields, "_msg"), Get(fields, "message"));

        return new VictoriaLogsLogEntry(
            timestamp,
            level,
            service,
            Redact(message),
            Get(fields, "instanceKey"),
            Get(fields, "operationTaskId"),
            Get(fields, "correlationId"),
            FirstNonEmpty(Get(fields, "traceId"), Get(fields, "trace_id")),
            "victoriaLogs",
            new Dictionary<string, string>(StringComparer.Ordinal),
            fields.ToDictionary(pair => pair.Key, pair => Redact(pair.Value), StringComparer.Ordinal));
    }

    private static string Get(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : string.Empty;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var sensitiveTokens = new[] { "password", "secret", "token", "connection string", "connectionstring" };
        return sensitiveTokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase))
            ? "[redacted]"
            : value;
    }
}

internal enum NervIipOpenTelemetrySignal
{
    Traces,
    Metrics,
    Logs
}
