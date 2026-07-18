using System.Buffers;
using System.Text;
using MQTTnet;
using MQTTnet.Protocol;

namespace Nerv.IIP.ConnectorHost.Connectors.Mqtt;

public sealed class MqttNetSubscriptionClient(
    IMqttCredentialResolver credentialResolver,
    TimeSpan? connectionDetectionBudget = null) : IMqttSubscriptionClient, IDisposable
{
    private readonly MqttClientFactory _factory = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMqttClient? _client;
    private Func<MqttApplicationMessageReceivedEventArgs, Task>? _messageHandler;
    private Func<MqttClientDisconnectedEventArgs, Task>? _disconnectedHandler;
    private string? _subscriptionKey;
    private readonly TimeSpan _connectionDetectionBudget = connectionDetectionBudget ?? TimeSpan.FromSeconds(4);

    public async Task ConnectAndSubscribeAsync(
        MqttConnectionOptions options,
        IReadOnlyList<string> topicFilters,
        Func<MqttInboundMessage, CancellationToken, Task> onMessage,
        Action onDisconnected,
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

            if (_disconnectedHandler is not null)
            {
                _client.DisconnectedAsync -= _disconnectedHandler;
            }

            _messageHandler = HandleMessageAsync;
            _disconnectedHandler = HandleDisconnectedAsync;
            _client.ApplicationMessageReceivedAsync += _messageHandler;
            _client.DisconnectedAsync += _disconnectedHandler;

            if (!_client.IsConnected)
            {
                var connectionOptions = await BuildOptionsAsync(options, cancellationToken);
                var connectResult = await _client.ConnectAsync(connectionOptions, cancellationToken);
                if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                {
                    throw new InvalidOperationException("MQTT broker rejected the connection acknowledgement.");
                }
            }

            var subscribeBuilder = _factory.CreateSubscribeOptionsBuilder();
            foreach (var topicFilter in topicFilters)
            {
                subscribeBuilder.WithTopicFilter(topicFilter);
            }

            var subscribeResult = await _client.SubscribeAsync(subscribeBuilder.Build(), cancellationToken);
            var requiredFilterCount = topicFilters.Distinct(StringComparer.Ordinal).Count();
            if (subscribeResult.Items.Count != requiredFilterCount
                || subscribeResult.Items.Any(item => item.ResultCode is not (
                    MqttClientSubscribeResultCode.GrantedQoS0
                    or MqttClientSubscribeResultCode.GrantedQoS1
                    or MqttClientSubscribeResultCode.GrantedQoS2)))
            {
                throw new InvalidOperationException("MQTT broker rejected one or more required subscriptions.");
            }

            _subscriptionKey = subscriptionKey;

            Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
            {
                var payload = Encoding.UTF8.GetString(args.ApplicationMessage.Payload.ToArray());
                return onMessage(new MqttInboundMessage(args.ApplicationMessage.Topic, payload, DateTimeOffset.UtcNow), cancellationToken);
            }

            Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs _)
            {
                _subscriptionKey = null;
                onDisconnected();
                return Task.CompletedTask;
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
            .WithKeepAlivePeriod(TimeSpan.FromMilliseconds(Math.Max(500, _connectionDetectionBudget.TotalMilliseconds / 2)))
            .WithTimeout(_connectionDetectionBudget)
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
