using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Nerv.IIP.BusinessGateway.Web;

internal static class BusinessGatewayHttpClientResilience
{
    public static IHttpResiliencePipelineBuilder AddBusinessGatewayNonIdempotentSafeResilience(
        this IHttpClientBuilder builder)
    {
        return builder.AddResilienceHandler("non-idempotent-safe", pipeline =>
        {
            pipeline
                .AddTimeout(TimeSpan.FromSeconds(10))
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 10,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(15)
                });
        });
    }
}
