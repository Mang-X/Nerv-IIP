using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Sinks.OpenTelemetry;

namespace Nerv.IIP.Observability;

public static class NervIipObservabilityRegistration
{
    public static IServiceCollection AddNervIipObservability(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddSingleton(new NervIipObservabilityOptions(serviceName));
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

        var otlpEndpoint = FirstNonEmpty(
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"),
            configuration["OpenTelemetry:Endpoint"],
            configuration["Logging:OpenTelemetry:Endpoint"]);

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            loggerConfiguration.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = otlpEndpoint;
                options.Protocol = ReadOtlpProtocol(configuration, otlpEndpoint);
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

    private static OtlpProtocol ReadOtlpProtocol(IConfiguration configuration, string endpoint)
    {
        var configuredProtocol = FirstNonEmpty(configuration["OpenTelemetry:Protocol"], configuration["Logging:OpenTelemetry:Protocol"]);
        if (Enum.TryParse<OtlpProtocol>(configuredProtocol, ignoreCase: true, out var protocol))
        {
            return protocol;
        }

        return endpoint.Contains("4318", StringComparison.OrdinalIgnoreCase) || endpoint.Contains("/v1/logs", StringComparison.OrdinalIgnoreCase)
            ? OtlpProtocol.HttpProtobuf
            : OtlpProtocol.Grpc;
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
