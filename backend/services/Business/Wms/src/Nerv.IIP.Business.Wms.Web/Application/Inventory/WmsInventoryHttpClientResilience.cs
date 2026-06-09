using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Nerv.IIP.Business.Wms.Web.Application.Inventory;

internal static class WmsInventoryHttpClientResilience
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultSamplingDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultBreakDuration = TimeSpan.FromSeconds(15);

    public static IHttpResiliencePipelineBuilder AddWmsInventoryPostingResilience(
        this IHttpClientBuilder builder,
        IConfiguration configuration)
    {
        var timeout = ReadPositiveTimeSpan(configuration, "Inventory:HttpClient:TimeoutSeconds", DefaultTimeout);
        var samplingDuration = ReadPositiveTimeSpan(
            configuration,
            "Inventory:HttpClient:CircuitBreaker:SamplingDurationSeconds",
            DefaultSamplingDuration);
        var breakDuration = ReadPositiveTimeSpan(
            configuration,
            "Inventory:HttpClient:CircuitBreaker:BreakDurationSeconds",
            DefaultBreakDuration);
        var minimumThroughput = configuration.GetValue(
            "Inventory:HttpClient:CircuitBreaker:MinimumThroughput",
            10);
        var failureRatio = configuration.GetValue(
            "Inventory:HttpClient:CircuitBreaker:FailureRatio",
            0.5);

        if (minimumThroughput <= 0)
        {
            throw new InvalidOperationException("Inventory:HttpClient:CircuitBreaker:MinimumThroughput must be greater than zero.");
        }

        if (failureRatio <= 0 || failureRatio > 1)
        {
            throw new InvalidOperationException("Inventory:HttpClient:CircuitBreaker:FailureRatio must be greater than zero and less than or equal to one.");
        }

        return builder.AddResilienceHandler("inventory-posting", pipeline =>
        {
            pipeline
                .AddTimeout(timeout)
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = failureRatio,
                    MinimumThroughput = minimumThroughput,
                    SamplingDuration = samplingDuration,
                    BreakDuration = breakDuration
                });
        });
    }

    private static TimeSpan ReadPositiveTimeSpan(IConfiguration configuration, string key, TimeSpan fallback)
    {
        var seconds = configuration.GetValue<double?>(key);
        if (seconds is null)
        {
            return fallback;
        }

        if (seconds <= 0)
        {
            throw new InvalidOperationException($"{key} must be greater than zero.");
        }

        return TimeSpan.FromSeconds(seconds.Value);
    }
}
