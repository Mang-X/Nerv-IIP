using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

namespace Nerv.IIP.Caching;

// FusionCache 2.9 catches initial Subscribe failures even with WaitForInitialBackplaneSubscribe.
// Observe the official provider's completed subscription so startup cannot silently accept that failure.
internal sealed class SubscriptionCheckedRedisBackplane(RedisBackplane backplane) : IFusionCacheBackplane
{
    public bool IsSubscribed { get; private set; }
    public void Subscribe(BackplaneSubscriptionOptions options)
    {
        backplane.Subscribe(options);
        IsSubscribed = true;
    }
    public async ValueTask SubscribeAsync(BackplaneSubscriptionOptions options)
    {
        await backplane.SubscribeAsync(options);
        IsSubscribed = true;
    }
    public void Unsubscribe() => backplane.Unsubscribe();
    public ValueTask UnsubscribeAsync() => backplane.UnsubscribeAsync();
    public void Publish(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) =>
        backplane.Publish(message, options, token);
    public ValueTask PublishAsync(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) =>
        backplane.PublishAsync(message, options, token);
}
