using System.Buffers;
using System.Text;
using MQTTnet;

namespace Nerv.IIP.ConnectorHost.Connectors.Mqtt;

public sealed class MqttNetSubscriptionClient(IMqttCredentialResolver credentialResolver) : IMqttSubscriptionClient, IDisposable
{
    private readonly MqttClientFactory _factory = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMqttClient? _client;
    private Func<MqttApplicationMessageReceivedEventArgs, Task>? _messageHandler;
    private string? _subscriptionKey;

    public async Task ConnectAndSubscribeAsync(
        MqttConnectionOptions options,
        IReadOnlyList<string> topicFilters,
        Func<MqttInboundMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _client ??= _factory.CreateMqttClient();
            var subscriptionKey = string.Join('\n', topicFilters.Order(StringComparer.Ordinal));
            if (_client.IsConnected && string.Equals(_subscriptionKey, subscriptionKey, StringComparison.Ordinal))
            {
                return;
            }

            if (_messageHandler is not null)
            {
                _client.ApplicationMessageReceivedAsync -= _messageHandler;
            }

            _messageHandler = HandleMessageAsync;
            _client.ApplicationMessageReceivedAsync += _messageHandler;

            if (!_client.IsConnected)
            {
                var connectionOptions = await BuildOptionsAsync(options, cancellationToken);
                await _client.ConnectAsync(connectionOptions, cancellationToken);
            }

            var subscribeBuilder = _factory.CreateSubscribeOptionsBuilder();
            foreach (var topicFilter in topicFilters)
            {
                subscribeBuilder.WithTopicFilter(topicFilter);
            }

            await _client.SubscribeAsync(subscribeBuilder.Build(), cancellationToken);
            _subscriptionKey = subscriptionKey;

            Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
            {
                var payload = Encoding.UTF8.GetString(args.ApplicationMessage.Payload.ToArray());
                return onMessage(new MqttInboundMessage(args.ApplicationMessage.Topic, payload, DateTimeOffset.UtcNow), cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _gate.Dispose();
    }

    private async Task<MqttClientOptions> BuildOptionsAsync(MqttConnectionOptions options, CancellationToken cancellationToken)
    {
        var broker = ParseBroker(options.Broker);
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(options.ClientId)
            .WithTcpServer(broker.Host, broker.Port)
            .WithCleanSession();

        var credential = await credentialResolver.ResolveAsync(options.CredentialReference, cancellationToken);
        if (credential is not null)
        {
            builder.WithCredentials(credential.UserName, credential.Password);
        }

        return builder.Build();
    }

    private static (string Host, int Port) ParseBroker(string broker)
    {
        var uri = broker.Contains("://", StringComparison.Ordinal)
            ? new Uri(broker)
            : new Uri($"tcp://{broker}");
        return (uri.Host, uri.Port > 0 ? uri.Port : 1883);
    }
}
