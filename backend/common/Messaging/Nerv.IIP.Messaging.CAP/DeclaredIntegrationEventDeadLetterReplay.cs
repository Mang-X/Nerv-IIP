using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Messaging.CAP;

public static class DeclaredIntegrationEventDeadLetterReplayExtensions
{
    /// <summary>Declare one original consumer, its event contract, and its durable completion fact.</summary>
    public static IServiceCollection AddIntegrationEventDeadLetterReplay<TEvent, TConsumer>(
        this IServiceCollection services,
        string consumerName,
        Func<TConsumer, TEvent, CancellationToken, Task> consume,
        Func<IServiceProvider, TEvent, CancellationToken, Task<bool>> completed)
        where TEvent : IIntegrationEventEnvelope
        where TConsumer : class
    {
        services.TryAddScoped<TConsumer>();
        services.AddSingleton<IIntegrationEventDeadLetterReplayHandler>(provider =>
            new DeclaredReplayHandler<TEvent, TConsumer>(provider.GetRequiredService<IServiceScopeFactory>(), consumerName, consume, completed));
        return services;
    }

    /// <summary>Observe rejection in the replay scope without changing the consumer's persistence or transaction.</summary>
    public static IServiceCollection AddObservedIntegrationEventDeadLetterStore(this IServiceCollection services)
    {
        if (services.Any(x => x.ServiceType == typeof(ReplayConsumption))) return services;
        var original = services.Last(x => x.ServiceType == typeof(IIntegrationEventDeadLetterStore));
        var key = new object();
        services.Remove(original);
        services.Add(original.ImplementationInstance is { } instance
            ? ServiceDescriptor.KeyedSingleton(typeof(IIntegrationEventDeadLetterStore), key, instance)
            : original.ImplementationFactory is { } factory
                ? ServiceDescriptor.DescribeKeyed(typeof(IIntegrationEventDeadLetterStore), key, (provider, _) => factory(provider), original.Lifetime)
                : ServiceDescriptor.DescribeKeyed(typeof(IIntegrationEventDeadLetterStore), key, original.ImplementationType!, original.Lifetime));
        services.AddScoped<ReplayConsumption>();
        services.AddScoped<IIntegrationEventDeadLetterStore>(provider => new ObservedDeadLetterStore(
            provider.GetRequiredKeyedService<IIntegrationEventDeadLetterStore>(key), provider.GetRequiredService<ReplayConsumption>()));
        return services;
    }
}

internal sealed class DeclaredReplayHandler<TEvent, TConsumer>(
    IServiceScopeFactory scopeFactory,
    string consumerName,
    Func<TConsumer, TEvent, CancellationToken, Task> consume,
    Func<IServiceProvider, TEvent, CancellationToken, Task<bool>> completed) : IIntegrationEventDeadLetterReplayHandler
    where TEvent : IIntegrationEventEnvelope
    where TConsumer : class
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public bool CanReplay(IntegrationEventDeadLetterMessage message) =>
        string.Equals(message.ConsumerName, consumerName, StringComparison.Ordinal)
        && string.Equals(message.EventClrType, typeof(TEvent).FullName, StringComparison.Ordinal);

    public async Task ReplayAsync(IntegrationEventDeadLetterMessage message, CancellationToken cancellationToken)
    {
        var integrationEvent = JsonSerializer.Deserialize<TEvent>(message.EventJson, SerializerOptions)
            ?? throw new InvalidOperationException("Dead-letter payload could not be deserialized.");
        await using var scope = scopeFactory.CreateAsyncScope();
        var consumption = scope.ServiceProvider.GetRequiredService<ReplayConsumption>();
        consumption.Target = message;
        await consume(scope.ServiceProvider.GetRequiredService<TConsumer>(), integrationEvent, cancellationToken);
        if (consumption.Rejection is { } rejection)
            throw new InvalidOperationException($"{rejection.FailureCode}: {rejection.FailureMessage}");
        if (!await completed(scope.ServiceProvider, integrationEvent, cancellationToken))
            throw new InvalidOperationException("The original consumer did not complete the integration event.");
    }
}

internal sealed class ReplayConsumption
{
    internal IntegrationEventDeadLetterMessage? Target { get; set; }
    internal IntegrationEventDeadLetterMessage? Rejection { get; private set; }

    internal void Observe(IntegrationEventDeadLetterMessage message)
    {
        if (Target is { } target
            && message.ConsumerName == target.ConsumerName
            && message.EventClrType == target.EventClrType
            && message.EventId == target.EventId)
            Rejection = message;
    }
}

internal sealed class ObservedDeadLetterStore(IIntegrationEventDeadLetterStore inner, ReplayConsumption consumption)
    : IIntegrationEventDeadLetterStore
{
    public async Task<IntegrationEventDeadLetterMessage> AddAsync(IntegrationEventDeadLetterMessage message, CancellationToken token)
    {
        var result = await inner.AddAsync(message, token);
        consumption.Observe(message);
        return result;
    }

    public async Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> AddRangeAsync(IReadOnlyCollection<IntegrationEventDeadLetterMessage> messages, CancellationToken token)
    {
        var result = await inner.AddRangeAsync(messages, token);
        foreach (var message in messages) consumption.Observe(message);
        return result;
    }

    public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(string? consumerName, IntegrationEventDeadLetterStatus? status, CancellationToken token) => inner.ListAsync(consumerName, status, token);
    public Task<IReadOnlyList<IntegrationEventDeadLetterMessage>> ListAsync(IntegrationEventDeadLetterQuery query, CancellationToken token) => inner.ListAsync(query, token);
    public Task<IntegrationEventDeadLetterMetrics> GetMetricsAsync(CancellationToken token) => inner.GetMetricsAsync(token);
    public Task<IntegrationEventDeadLetterMessage?> GetAsync(Guid id, CancellationToken token) => inner.GetAsync(id, token);
    public Task MarkReplayedAsync(Guid id, DateTimeOffset at, CancellationToken token) => inner.MarkReplayedAsync(id, at, token);
    public Task MarkFailedAsync(Guid id, string code, string message, DateTimeOffset at, CancellationToken token) => inner.MarkFailedAsync(id, code, message, at, token);
    public Task MarkIgnoredAsync(Guid id, string reason, DateTimeOffset at, CancellationToken token) => inner.MarkIgnoredAsync(id, reason, at, token);
}
