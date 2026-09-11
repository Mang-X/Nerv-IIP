using DotNetCore.CAP;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Nerv.IIP.Messaging.CAP;

/// <summary>
/// Wraps the transport's <see cref="IConsumerClientFactory"/> and every <see cref="IConsumerClient"/> it creates
/// so that later work (#3351 首轮订阅闸门、#3352 <see cref="IConsumerClient.ListeningAsync"/> 专用线程) has one
/// place to hook into. <b>This skeleton forwards every member verbatim and adds no behaviour of its own.</b>
///
/// <para>Registration mechanics: the transport package registers <see cref="IConsumerClientFactory"/> from its own
/// <see cref="ICapOptionsExtension.AddServices"/>, and <c>AddCap</c> runs the extensions in registration order.
/// Registering this extension immediately after the transport extension therefore guarantees the transport
/// descriptor already exists when <see cref="AddServices"/> runs.</para>
/// </summary>
internal sealed class ConsumerClientDecorationExtension : ICapOptionsExtension
{
    public void AddServices(IServiceCollection services)
    {
        var transportDescriptor = services.LastOrDefault(
            descriptor => descriptor.ServiceType == typeof(IConsumerClientFactory));
        if (transportDescriptor?.ImplementationType is null)
        {
            // Fail closed. Silently skipping would leave the decorator unmounted while every gate stays green,
            // which would turn #3351/#3352 into no-ops that nothing reports on.
            throw new InvalidOperationException(
                $"The CAP transport did not register an {nameof(IConsumerClientFactory)} with a concrete implementation type, "
                + $"so {nameof(ConsumerClientDecorationExtension)} cannot wrap it. "
                + "Registration order or the transport package shape changed; revisit the decoration seam.");
        }

        services.Remove(transportDescriptor);
        services.AddSingleton(provider => new TransportConsumerClientFactory(
            (IConsumerClientFactory)ActivatorUtilities.CreateInstance(provider, transportDescriptor.ImplementationType)));

        // Registered with a concrete implementation type (not a factory delegate) so that the resulting descriptor
        // keeps the shape callers already rely on: capture the single IConsumerClientFactory descriptor and rebuild
        // it through ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType).
        services.AddSingleton<IConsumerClientFactory, DecoratedConsumerClientFactory>();
    }
}

/// <summary>Holds the transport's own factory so the decorator can take it as a constructor dependency.</summary>
internal sealed class TransportConsumerClientFactory(IConsumerClientFactory inner)
{
    public IConsumerClientFactory Inner { get; } = inner;
}

internal sealed class DecoratedConsumerClientFactory(TransportConsumerClientFactory transport) : IConsumerClientFactory
{
    internal IConsumerClientFactory Inner => transport.Inner;

    public async Task<IConsumerClient> CreateAsync(string groupName, byte groupConcurrent) =>
        new DecoratedConsumerClient(await transport.Inner.CreateAsync(groupName, groupConcurrent));
}

internal sealed class DecoratedConsumerClient(IConsumerClient inner) : IConsumerClient
{
    internal IConsumerClient Inner => inner;

    public BrokerAddress BrokerAddress => inner.BrokerAddress;

    public Func<TransportMessage, object?, Task>? OnMessageCallback
    {
        get => inner.OnMessageCallback;
        set => inner.OnMessageCallback = value;
    }

    public Action<LogMessageEventArgs>? OnLogCallback
    {
        get => inner.OnLogCallback;
        set => inner.OnLogCallback = value;
    }

    public Task<ICollection<string>> FetchTopicsAsync(IEnumerable<string> topicNames) =>
        inner.FetchTopicsAsync(topicNames);

    public Task SubscribeAsync(IEnumerable<string> topics) => inner.SubscribeAsync(topics);

    public Task ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        inner.ListeningAsync(timeout, cancellationToken);

    public Task CommitAsync(object? sender) => inner.CommitAsync(sender);

    public Task RejectAsync(object? sender) => inner.RejectAsync(sender);

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
