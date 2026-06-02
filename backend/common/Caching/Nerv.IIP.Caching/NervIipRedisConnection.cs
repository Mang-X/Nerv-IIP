using StackExchange.Redis;

namespace Nerv.IIP.Caching;

public static class NervIipRedisConnection
{
    public static async Task<IConnectionMultiplexer> ConnectAsync(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        return await ConnectionMultiplexer.ConnectAsync(options);
    }
}
