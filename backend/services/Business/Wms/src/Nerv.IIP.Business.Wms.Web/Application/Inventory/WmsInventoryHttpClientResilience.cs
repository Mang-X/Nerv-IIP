using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Nerv.IIP.Business.Wms.Web.Application.Inventory;

internal static class WmsInventoryHttpClientResilience
{
    private const string TimeoutSecondsKey = "Inventory:HttpClient:TimeoutSeconds";
    private const string SamplingDurationSecondsKey = "Inventory:HttpClient:CircuitBreaker:SamplingDurationSeconds";
    private const string BreakDurationSecondsKey = "Inventory:HttpClient:CircuitBreaker:BreakDurationSeconds";
    private const string MinimumThroughputKey = "Inventory:HttpClient:CircuitBreaker:MinimumThroughput";
    private const string FailureRatioKey = "Inventory:HttpClient:CircuitBreaker:FailureRatio";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultSamplingDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultBreakDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinimumCircuitBreakerDuration = TimeSpan.FromMilliseconds(500);

    public static IHttpResiliencePipelineBuilder AddWmsInventoryPostingResilience(
        this IHttpClientBuilder builder,
        IConfiguration configuration)
    {
        var timeout = ReadPositiveTimeSpan(configuration, TimeoutSecondsKey, DefaultTimeout);
        var samplingDuration = ReadPositiveTimeSpan(
            configuration,
            SamplingDurationSecondsKey,
            DefaultSamplingDuration);
        var breakDuration = ReadPositiveTimeSpan(
            configuration,
            BreakDurationSecondsKey,
            DefaultBreakDuration);
        var minimumThroughput = configuration.GetValue(
            MinimumThroughputKey,
            10);
        var failureRatio = configuration.GetValue(
            FailureRatioKey,
            0.5);

        if (minimumThroughput < 2)
        {
            throw new InvalidOperationException($"{MinimumThroughputKey} must be greater than or equal to 2.");
        }

        if (samplingDuration < MinimumCircuitBreakerDuration)
        {
            throw new InvalidOperationException($"{SamplingDurationSecondsKey} must be greater than or equal to 0.5 seconds.");
        }

        if (breakDuration < MinimumCircuitBreakerDuration)
        {
            throw new InvalidOperationException($"{BreakDurationSecondsKey} must be greater than or equal to 0.5 seconds.");
        }

        if (failureRatio <= 0 || failureRatio > 1)
        {
            throw new InvalidOperationException($"{FailureRatioKey} must be greater than zero and less than or equal to one.");
        }

        return builder.AddResilienceHandler("inventory-posting", pipeline =>
        {
            pipeline
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = failureRatio,
                    MinimumThroughput = minimumThroughput,
                    SamplingDuration = samplingDuration,
                    BreakDuration = breakDuration
                })
                .AddTimeout(timeout);
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
