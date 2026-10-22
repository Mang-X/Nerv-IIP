using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.Caching;

namespace Nerv.IIP.Caching.Tests;

// #2140: deployment choices must take effect and invalid configuration must fail at startup.
[Trait("Contract", "Regression")]
public sealed class AppCacheConfigurationTests
{
    [Theory]
    [InlineData("Caching:Provider", "unknown")]
    [InlineData("Caching:Provider", "Redis")]
    [InlineData("Caching:L1MaxEntries", "0")]
    [InlineData("Caching:L1MaxEntries", "invalid")]
    public void Invalid_configuration_fails_during_registration_without_echoing_values(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNervIipCaching(configuration, "gateway"));
        Assert.Contains(key, error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void L1_selection_rejects_conflicting_Redis_credentials_without_disclosing_them()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Caching:Provider"] = "L1",
            ["Caching:Redis"] = "localhost,password=private-secret"
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNervIipCaching(configuration, "gateway"));
        Assert.DoesNotContain("private-secret", error.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("L1")]
    public async Task L1_configuration_caches_locally(string? provider)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Caching:Provider"] = provider }).Build();
        using var services = new ServiceCollection().AddNervIipCaching(configuration, "gateway").BuildServiceProvider();
        var cache = services.GetRequiredService<IAppCache>();
        Assert.Equal(42, await cache.GetOrCreateAsync("key", () => Task.FromResult(42), TimeSpan.FromMinutes(1)));
        Assert.Equal(42, await cache.GetOrCreateAsync<int>("key", () => throw new InvalidOperationException("must hit"), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Cache_observations_include_hits_misses_and_errors_without_sensitive_keys_or_exception_messages()
    {
        var logger = new SafeLogProbe();
        var configuration = new ConfigurationBuilder().Build();
        using var services = new ServiceCollection().AddSingleton<ILogger<FusionAppCache>>(logger)
            .AddNervIipCaching(configuration, "gateway").BuildServiceProvider();
        var cache = services.GetRequiredService<IAppCache>();
        await cache.GetOrCreateAsync("token-secret-key", () => Task.FromResult(42), TimeSpan.FromMinutes(1));
        await cache.GetOrCreateAsync<int>("token-secret-key", () => throw new InvalidOperationException(), TimeSpan.FromMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync<int>("other-token-secret-key",
            () => throw new InvalidOperationException("password=secret-error-value"), TimeSpan.FromMinutes(1)));
        Assert.Contains(logger.Messages, message => message.Contains("miss"));
        Assert.Contains(logger.Messages, message => message.Contains("hit"));
        Assert.Contains(logger.Messages, message => message.Contains("failed"));
        Assert.All(logger.Messages, message => Assert.DoesNotContain("secret", message));
    }

    private sealed class SafeLogProbe : ILogger<FusionAppCache>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception?.ToString());
    }

    [Theory]
    [InlineData("localhost,password=private-secret", null)]
    [InlineData("localhost,password=private-secret", "")]
    [InlineData("localhost,password=private-secret,unknown=private-secret", "Production")]
    public void Redis_requires_environment_and_valid_connection_options(string redis, string? environment)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Caching:Redis"] = redis,
            ["Caching:Environment"] = environment
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNervIipCaching(configuration, "gateway"));
        Assert.DoesNotContain("private-secret", error.ToString());
    }
}
