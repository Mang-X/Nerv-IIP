using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nerv.IIP.Messaging.CAP;

public static class IntegrationEventDeadLetterEndpointServiceCollectionExtensions
{
    /// <summary>
    /// 注册死信读取/重放端点所需的服务。<see cref="IIntegrationEventDeadLetterStore"/> 仍由各服务按
    /// 自己的持久化形态注册（Postgres / 内存 / 服务本地实现），这里只补齐出口侧共用的两项。
    /// </summary>
    public static IServiceCollection AddIntegrationEventDeadLetterEndpoints(this IServiceCollection services)
    {
        // Ignore 端点与执行器都要读当前时间；已经注册过自己 TimeProvider 的服务保持不变。
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IntegrationEventDeadLetterReplayExecutor>();
        return services;
    }
}
