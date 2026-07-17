using Nerv.IIP.ConnectorHost.Connectors.Mqtt;
using Nerv.IIP.ConnectorHost.Connectors.OpcUa;

namespace Nerv.IIP.ConnectorHost.Connectors.Mqtt.Tests;

public sealed class MqttTelemetryCollectorTests
{
    [Fact]
    public async Task Discover_uses_configured_collection_connector_id_for_instance_and_health()
    {
        var connector = CreateConnector(new FakeMqttSubscriptionClient(), new RecordingIndustrialTelemetrySamplesClient(), collectionConnectorId: "line-a-primary");

        var target = Assert.Single(await connector.DiscoverAsync(CancellationToken.None));

        Assert.Equal("line-a-primary", target.InstanceKey);
        Assert.Equal("line-a-primary", target.CollectionHealth!.ConnectorId);
    }

    [Fact]
    public async Task One_inbound_message_is_received_once_even_when_multiple_mappings_accept_it()
    {
        var message = new MqttInboundMessage("factory/line-1/temperature", """{"temperature":10}""", new DateTimeOffset(2026, 7, 5, 9, 0, 10, TimeSpan.Zero));
        var connector = new MqttConnector(
            new MqttConnectorOptions("mqtt-line-1", "host", "org", "env", "tcp://mqtt", "client", null,
            [
                new MqttTopicMapping("device-1", "temperature-a", "factory/line-1/temperature", "$.temperature", 60),
                new MqttTopicMapping("device-1", "temperature-b", "factory/line-1/temperature", "$.temperature", 60)
            ]),
            new FakeMqttSubscriptionClient(message),
            new RecordingIndustrialTelemetrySamplesClient(),
            () => new DateTimeOffset(2026, 7, 5, 9, 1, 1, TimeSpan.Zero));

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Equal(1, connector.CurrentState.ReceivedSamples);
        Assert.Equal(0, connector.CurrentState.DroppedSamples);
    }
    [Fact]
    public async Task Run_cycle_subscribes_topics_maps_json_path_payload_and_posts_bucketed_sample()
    {
        var now = new DateTimeOffset(2026, 7, 5, 9, 1, 1, TimeSpan.Zero);
        var mqtt = new FakeMqttSubscriptionClient(
            new MqttInboundMessage("factory/line-1/temperature", """{"temperature":10,"state":"running"}""", new DateTimeOffset(2026, 7, 5, 9, 0, 10, TimeSpan.Zero)),
            new MqttInboundMessage("factory/line-1/temperature", """{"temperature":20,"state":"running"}""", new DateTimeOffset(2026, 7, 5, 9, 0, 40, TimeSpan.Zero)));
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(mqtt, samples, () => now);
        var initialHealth = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!;
        Assert.Null(initialHealth.ReceivedCount);
        Assert.Null(initialHealth.DroppedCount);
        Assert.Null(initialHealth.ErrorCount);
        Assert.Null(initialHealth.LastSampleAtUtc);

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Equal("tcp://mqtt.local:1883", mqtt.ConnectedBroker);
        Assert.Equal(["factory/line-1/temperature"], mqtt.Subscriptions);
        var request = Assert.Single(samples.Requests);
        Assert.Equal("device-line-1", request.DeviceAssetId);
        Assert.Equal("temperature", request.TagKey);
        Assert.Equal(2, request.SampleCount);
        Assert.Equal(10m, request.MinValue);
        Assert.Equal(20m, request.MaxValue);
        Assert.Equal(15m, request.AverageValue);
        Assert.Equal("mqtt:mqtt-line-1:temperature:1783242000000", request.SourceSequence);
        Assert.Equal("mqtt", request.SourceSystem);
        Assert.Equal("connector-host-001/mqtt-line-1", request.SourceConnector);
        var health = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth;
        Assert.NotNull(health);
        Assert.Equal("mqtt-mqtt-line-1", health.ConnectorId);
        Assert.Equal("mqtt", health.SourceSystem);
        Assert.NotEqual(Guid.Empty, health.CounterEpoch);
        Assert.Equal(2, health.ReceivedCount);
        Assert.Equal(0, health.DroppedCount);
        Assert.Equal(0, health.ErrorCount);
        Assert.Equal(new DateTimeOffset(2026, 7, 5, 9, 0, 40, TimeSpan.Zero), health.LastSampleAtUtc);
    }

    [Fact]
    public async Task Run_cycle_drops_messages_that_do_not_match_topic_or_json_path_mapping()
    {
        var mqtt = new FakeMqttSubscriptionClient(
            new MqttInboundMessage("factory/line-1/humidity", """{"temperature":10}""", new DateTimeOffset(2026, 7, 5, 9, 0, 10, TimeSpan.Zero)),
            new MqttInboundMessage("factory/line-1/temperature", """{"other":20}""", new DateTimeOffset(2026, 7, 5, 9, 0, 20, TimeSpan.Zero)));
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(mqtt, samples);

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Empty(samples.Requests);
        Assert.Equal(2, connector.CurrentState.DroppedSamples);
    }

    [Fact]
    public async Task Run_cycle_restores_bucket_after_downstream_failure_so_retry_keeps_same_source_sequence()
    {
        var mqtt = new SequencedMqttSubscriptionClient(
            [
                [new MqttInboundMessage("factory/line-1/temperature", """{"temperature":42}""", new DateTimeOffset(2026, 7, 5, 9, 0, 10, TimeSpan.Zero))],
                []
            ]);
        var samples = new FailOnceIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(mqtt, samples);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connector.RunCollectionCycleAsync(CancellationToken.None));
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var request = Assert.Single(samples.Requests);
        Assert.Equal("mqtt:mqtt-line-1:temperature:1783242000000", request.SourceSequence);
        Assert.Equal(2, samples.WriteAttempts);
        Assert.Equal(1, connector.CurrentState.ErrorCount);
    }

    [Fact]
    public async Task Environment_credential_resolver_resolves_broker_credentials_without_storing_secret_in_options()
    {
        using var variables = new TemporaryEnvironmentVariables(
            ("NERV_IIP_MQTT_LINE1_USERNAME", "collector"),
            ("NERV_IIP_MQTT_LINE1_PASSWORD", "secret-value")).Set();
        var resolver = new EnvironmentMqttCredentialResolver();

        var credential = await resolver.ResolveAsync("env:NERV_IIP_MQTT_LINE1", CancellationToken.None);

        Assert.NotNull(credential);
        Assert.Equal("collector", credential.UserName);
        Assert.Equal("secret-value", credential.Password);
    }

    private static MqttConnector CreateConnector(
        IMqttSubscriptionClient mqtt,
        IIndustrialTelemetrySamplesClient samples,
        Func<DateTimeOffset>? utcNow = null,
        string? collectionConnectorId = null)
    {
        return new MqttConnector(
            new MqttConnectorOptions(
                ConnectorId: "mqtt-line-1",
                ConnectorHostId: "connector-host-001",
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                Broker: "tcp://mqtt.local:1883",
                ClientId: "nerv-iip-line-1",
                CredentialReference: null,
                TopicMappings:
                [
                    new MqttTopicMapping(
                        DeviceAssetId: "device-line-1",
                        TagKey: "temperature",
                        TopicFilter: "factory/line-1/temperature",
                        ValueJsonPath: "$.temperature",
                        BucketSeconds: 60)
                ],
                CollectionConnectorId: collectionConnectorId),
            mqtt,
            samples,
            utcNow ?? (() => new DateTimeOffset(2026, 7, 5, 9, 1, 1, TimeSpan.Zero)));
    }

    private sealed class FakeMqttSubscriptionClient(params MqttInboundMessage[] messages) : IMqttSubscriptionClient
    {
        public string? ConnectedBroker { get; private set; }
        public List<string> Subscriptions { get; } = [];

        public async Task ConnectAndSubscribeAsync(
            MqttConnectionOptions options,
            IReadOnlyList<string> topicFilters,
            Func<MqttInboundMessage, CancellationToken, Task> onMessage,
            CancellationToken cancellationToken)
        {
            ConnectedBroker = options.Broker;
            Subscriptions.AddRange(topicFilters);
            foreach (var message in messages)
            {
                await onMessage(message, cancellationToken);
            }
        }
    }

    private sealed class SequencedMqttSubscriptionClient(IReadOnlyList<IReadOnlyList<MqttInboundMessage>> batches) : IMqttSubscriptionClient
    {
        private int _index;

        public async Task ConnectAndSubscribeAsync(
            MqttConnectionOptions options,
            IReadOnlyList<string> topicFilters,
            Func<MqttInboundMessage, CancellationToken, Task> onMessage,
            CancellationToken cancellationToken)
        {
            var batch = _index < batches.Count ? batches[_index++] : [];
            foreach (var message in batch)
            {
                await onMessage(message, cancellationToken);
            }
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

    private sealed class FailOnceIndustrialTelemetrySamplesClient : IIndustrialTelemetrySamplesClient
    {
        public int WriteAttempts { get; private set; }
        public List<RecordIndustrialTelemetrySampleRequest> Requests { get; } = [];

        public Task RecordSampleAsync(RecordIndustrialTelemetrySampleRequest request, CancellationToken cancellationToken)
        {
            WriteAttempts++;
            if (WriteAttempts == 1)
            {
                throw new InvalidOperationException("simulated downstream ingestion failure");
            }

            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryEnvironmentVariables(params (string Name, string Value)[] variables) : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = variables.ToDictionary(x => x.Name, x => Environment.GetEnvironmentVariable(x.Name));

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public TemporaryEnvironmentVariables Set()
        {
            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            return this;
        }
    }
}
