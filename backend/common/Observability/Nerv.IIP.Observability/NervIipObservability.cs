using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Nerv.IIP.Observability;

public static class NervIipObservabilityRegistration
{
    public static IServiceCollection AddNervIipObservability(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddSingleton(new NervIipObservabilityOptions(serviceName));
        services.AddLogging(logging => logging.AddConfiguration(configuration.GetSection("Logging")));
        return services;
    }

    public static IApplicationBuilder UseNervIipCorrelation(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var correlationId = context.Request.Headers.TryGetValue("X-Correlation-Id", out var header) && !string.IsNullOrWhiteSpace(header)
                ? header.ToString()
                : Guid.NewGuid().ToString("n");
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            Activity.Current?.SetTag("correlationId", correlationId);
            using (context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Nerv.IIP.Correlation").BeginScope(new Dictionary<string, object> { ["correlationId"] = correlationId }))
            {
                await next();
            }
        });
    }
}

public sealed record NervIipObservabilityOptions(string ServiceName);
