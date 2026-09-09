using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Nerv.IIP.BusinessGateway.Web;

internal static class BusinessGatewayHttpClientResilience
{
    /// <summary>
    /// 字节流档。<see cref="AddBusinessGatewayNonIdempotentSafeResilience"/> 的 10 秒总超时是按单次
    /// JSON 调用定的，会切断合法的大文件传输（ADR 0015 决策 2 的参数由 ADR 0030 决策 5 部分修订）。
    /// 本档去掉总超时，保留 ADR 0015 决策 2 的熔断参数与「不自动重试」——ADR 0015 决策 3.2 对
    /// 「能发起非幂等写」的客户端仍然适用，字节面的 tus <c>PATCH</c> 正是非幂等写。
    /// 单次传输的时限由调用方的 <see cref="CancellationToken"/> 承担。
    /// </summary>
    public static IHttpResiliencePipelineBuilder AddBusinessGatewayStreamingSafeResilience(
        this IHttpClientBuilder builder)
    {
        return builder.AddResilienceHandler("streaming-safe", pipeline =>
        {
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(15)
            });
        });
    }

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
