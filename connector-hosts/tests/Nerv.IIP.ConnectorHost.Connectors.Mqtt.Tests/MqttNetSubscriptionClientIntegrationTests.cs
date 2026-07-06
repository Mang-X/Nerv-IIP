using MQTTnet;
using MQTTnet.Server;
using Nerv.IIP.ConnectorHost.Connectors.Mqtt;
using Nerv.IIP.ConnectorHost.Connectors.OpcUa;

namespace Nerv.IIP.ConnectorHost.Connectors.Mqtt.Tests;

public sealed class MqttNetSubscriptionClientIntegrationTests
{
    [Fact]
    public async Task Mqttnet_client_subscribes_local_broker_and_maps_published_payload()
    {
        await using var broker = await MqttBroker.StartAsync();
        using var client = new MqttNetSubscriptionClient(new EnvironmentMqttCredentialResolver());
        var received = new TaskCompletionSource<MqttInboundMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await client.ConnectAndSubscribeAsync(
            new MqttConnectionOptions(broker.Endpoint, "nerv-iip-test-client", null),
            ["factory/line-1/temperature"],
            (message, _) =>
            {
                received.TrySetResult(message);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await broker.PublishAsync("factory/line-1/temperature", """{"temperature":42.5}""");

        var inbound = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("factory/line-1/temperature", inbound.Topic);
        Assert.Equal("""{"temperature":42.5}""", inbound.Payload);
    }

    [Fact]
    public async Task Mqtt_connector_subscribes_local_broker_maps_payload_and_posts_bucketed_sample()
    {
        var now = DateTimeOffset.UtcNow;
        await using var broker = await MqttBroker.StartAsync();
        using var client = new MqttNetSubscriptionClient(new EnvironmentMqttCredentialResolver());
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = new MqttConnector(
            new MqttConnectorOptions(
                ConnectorId: "mqtt-line-1",
                ConnectorHostId: "connector-host-001",
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                Broker: broker.Endpoint,
                ClientId: "nerv-iip-test-connector",
                CredentialReference: null,
                TopicMappings:
                [
                    new MqttTopicMapping(
                        DeviceAssetId: "device-line-1",
                        TagKey: "temperature",
                        TopicFilter: "factory/line-1/temperature",
                        ValueJsonPath: "$.temperature",
                        BucketSeconds: 60)
                ]),
            client,
            samples,
            () => now);

        await connector.RunCollectionCycleAsync(CancellationToken.None);
        await broker.PublishAsync("factory/line-1/temperature", """{"temperature":42.5}""");
        await WaitUntilAsync(() => connector.CurrentState.ReceivedSamples == 1);

        now = DateTimeOffset.UtcNow.AddMinutes(2);
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var request = Assert.Single(samples.Requests);
        Assert.Equal("device-line-1", request.DeviceAssetId);
        Assert.Equal("temperature", request.TagKey);
        Assert.Equal(42.5m, request.AverageValue);
        Assert.StartsWith("mqtt:mqtt-line-1:temperature:", request.SourceSequence, StringComparison.Ordinal);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    private sealed class RecordingIndustrialTelemetrySamplesClient : IIndustrialTelemetrySamplesClient
    {
        public List<RecordIndustrialTelemetrySampleRequest> Requests { get; } = [];

        public Task RecordSampleAsync(RecordIndustrialTelemetrySampleRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class MqttBroker : IAsyncDisposable
    {
        private readonly MqttServer _server;

        private MqttBroker(MqttServer server, int port)
        {
            _server = server;
            Endpoint = $"tcp://127.0.0.1:{port}";
        }

        public string Endpoint { get; }

        public static async Task<MqttBroker> StartAsync()
        {
            var port = GetFreeTcpPort();
            var factory = new MqttServerFactory();
            var options = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()
                .WithDefaultEndpointPort(port)
                .Build();
            var server = factory.CreateMqttServer(options);
            await server.StartAsync();
            return new MqttBroker(server, port);
        }

        public Task PublishAsync(string topic, string payload)
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .Build();
            return _server.InjectApplicationMessage(
                new InjectedMqttApplicationMessage(message)
                {
                    SenderClientId = "nerv-iip-test-broker"
                });
        }

        public async ValueTask DisposeAsync()
        {
            await _server.StopAsync();
            _server.Dispose();
        }

        private static int GetFreeTcpPort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
