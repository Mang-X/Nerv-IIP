using Nerv.IIP.ConnectorHost.Connectors.OpcUa;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa.Tests;

public sealed class OpcUaTelemetryCollectorTests
{
    [Fact]
    public async Task Connection_stays_unknown_until_subscription_apply_acknowledgement_then_becomes_alive()
    {
        var opcUa = new AcknowledgementControlledOpcUaClient();
        var connector = CreateConnector(opcUa, new RecordingIndustrialTelemetrySamplesClient());

        var cycle = connector.RunCollectionCycleAsync(CancellationToken.None);
        await opcUa.WaitForSubscribeAsync();

        var pending = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!;
        Assert.Equal("unknown", pending.Connection!.Status);

        opcUa.AcknowledgeSubscription();
        await cycle;

        var acknowledged = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!;
        Assert.Equal("alive", acknowledged.Connection!.Status);
    }

    [Fact]
    public async Task Bad_keepalive_marks_lost_immediately_and_reconnect_recovers()
    {
        var opcUa = new AcknowledgementControlledOpcUaClient(autoAcknowledge: true);
        var connector = CreateConnector(opcUa, new RecordingIndustrialTelemetrySamplesClient());
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        var firstAlive = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!.Connection!;

        opcUa.FailKeepAlive();

        var lost = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!.Connection!;
        Assert.Equal("lost", lost.Status);
        Assert.Equal("transport", lost.ReasonCategory);
        Assert.Equal("opcua.bad-keepalive", lost.DiagnosticCode);

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var recovered = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!.Connection!;
        Assert.Equal("alive", recovered.Status);
        Assert.NotEqual(firstAlive.ConnectedSinceUtc, recovered.ConnectedSinceUtc);
        Assert.Null(recovered.DisconnectedSinceUtc);
    }

    [Fact]
    public async Task Bad_keepalive_during_subscription_completion_is_not_overwritten_by_stale_alive()
    {
        var opcUa = new AcknowledgementControlledOpcUaClient(autoAcknowledge: true, failKeepAliveBeforeReturn: true);
        var connector = CreateConnector(opcUa, new RecordingIndustrialTelemetrySamplesClient());

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var connection = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!.Connection!;
        Assert.Equal("lost", connection.Status);
        Assert.Equal("opcua.bad-keepalive", connection.DiagnosticCode);
    }

    [Fact]
    public async Task Discover_uses_configured_collection_connector_id_for_instance_and_health()
    {
        var connector = CreateConnector(new FakeOpcUaClient([], []), new RecordingIndustrialTelemetrySamplesClient(), collectionConnectorId: "line-a-primary");

        var target = Assert.Single(await connector.DiscoverAsync(CancellationToken.None));

        Assert.Equal("line-a-primary", target.InstanceKey);
        Assert.Equal("line-a-primary", target.CollectionHealth!.ConnectorId);
    }

    [Fact]
    public async Task Run_cycle_browses_nodes_subscribes_tags_and_posts_bucketed_sample_with_idempotency_fields()
    {
        var opcUa = new FakeOpcUaClient(
            [new OpcUaNode("ns=2;s=Line1.Temperature", "Line1 Temperature", true)],
            [
                new OpcUaDataChange(
                    "ns=2;s=Line1.Temperature",
                    10m,
                    new DateTimeOffset(2026, 7, 3, 0, 0, 1, TimeSpan.Zero),
                    "Good"),
                new OpcUaDataChange(
                    "ns=2;s=Line1.Temperature",
                    20m,
                    new DateTimeOffset(2026, 7, 3, 0, 0, 5, TimeSpan.Zero),
                    "Good")
            ]);
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(opcUa, samples);
        var initialHealth = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!;
        Assert.Null(initialHealth.ReceivedCount);
        Assert.Null(initialHealth.DroppedCount);
        Assert.Null(initialHealth.ErrorCount);
        Assert.Null(initialHealth.LastSampleAtUtc);

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Equal(["ns=0;i=85"], opcUa.BrowsedNodeIds);
        Assert.Equal(["ns=2;s=Line1.Temperature"], opcUa.SubscribedNodeIds);
        var request = Assert.Single(samples.Requests);
        Assert.Equal("org-001", request.OrganizationId);
        Assert.Equal("env-dev", request.EnvironmentId);
        Assert.Equal("device-line-1", request.DeviceAssetId);
        Assert.Equal("temperature", request.TagKey);
        Assert.Equal(new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero), request.BucketStartUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 3, 0, 1, 0, TimeSpan.Zero), request.BucketEndUtc);
        Assert.Equal(2, request.SampleCount);
        Assert.Equal(10m, request.MinValue);
        Assert.Equal(20m, request.MaxValue);
        Assert.Equal(15m, request.AverageValue);
        Assert.Equal("opcua:opcua-line-1:temperature:1783036800000", request.SourceSequence);
        Assert.Equal("opcua", request.SourceSystem);
        Assert.Equal("connector-host-001/opcua-line-1", request.SourceConnector);
        var health = Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth;
        Assert.NotNull(health);
        Assert.Equal("opcua-opcua-line-1", health.ConnectorId);
        Assert.Equal("opcua", health.SourceSystem);
        Assert.NotEqual(Guid.Empty, health.CounterEpoch);
        Assert.Equal(2, health.ReceivedCount);
        Assert.Equal(0, health.DroppedCount);
        Assert.Equal(0, health.ErrorCount);
        Assert.Equal(new DateTimeOffset(2026, 7, 3, 0, 0, 5, TimeSpan.Zero), health.LastSampleAtUtc);
    }

    [Fact]
    public async Task Run_cycle_reconnects_after_connection_loss_and_reports_health_metrics()
    {
        var opcUa = new FakeOpcUaClient(
            [],
            [new OpcUaDataChange("ns=2;s=Line1.Temperature", 42m, new DateTimeOffset(2026, 7, 3, 0, 0, 3, TimeSpan.Zero), "Good")])
        {
            ThrowConnectionLostOnFirstSubscribe = true
        };
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(opcUa, samples);

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Equal(2, opcUa.ConnectCount);
        Assert.Single(samples.Requests);
        var targets = await connector.DiscoverAsync(CancellationToken.None);
        var target = Assert.Single(targets);
        Assert.Equal("running", target.ReportedStatus);
        Assert.Equal("degraded", target.HealthStatus);
        Assert.Equal("1", target.Metadata["reconnectCount"]);
        Assert.Equal("1", target.Metadata["subscriptionRecoveries"]);
        Assert.Equal(1m, connector.CurrentState.Metrics["reconnectCount"]);
        Assert.Equal(1, target.CollectionHealth!.ErrorCount);
    }

    [Fact]
    public async Task Run_cycle_uses_stable_source_sequence_when_ingestion_retries_after_failure()
    {
        var opcUa = new SequencedFakeOpcUaClient(
            [
                [new OpcUaDataChange("ns=2;s=Line1.Temperature", 42m, new DateTimeOffset(2026, 7, 3, 0, 0, 3, TimeSpan.Zero), "Good")],
                []
            ]);
        var samples = new FailOnceIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(opcUa, samples);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connector.RunCollectionCycleAsync(CancellationToken.None));
        Assert.Equal("alive", Assert.Single(await connector.DiscoverAsync(CancellationToken.None)).CollectionHealth!.Connection!.Status);
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var request = Assert.Single(samples.Requests);
        Assert.Equal("opcua:opcua-line-1:temperature:1783036800000", request.SourceSequence);
        Assert.Equal(2, samples.WriteAttempts);
    }

    [Fact]
    public async Task Run_cycle_keeps_open_bucket_across_cycles_and_flushes_once_after_bucket_end()
    {
        var now = new DateTimeOffset(2026, 7, 3, 0, 0, 30, TimeSpan.Zero);
        var opcUa = new SequencedFakeOpcUaClient(
            [
                [new OpcUaDataChange("ns=2;s=Line1.Temperature", 10m, new DateTimeOffset(2026, 7, 3, 0, 0, 10, TimeSpan.Zero), "Good")],
                [new OpcUaDataChange("ns=2;s=Line1.Temperature", 20m, new DateTimeOffset(2026, 7, 3, 0, 0, 40, TimeSpan.Zero), "Good")],
                []
            ]);
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(opcUa, samples, () => now);

        await connector.RunCollectionCycleAsync(CancellationToken.None);
        Assert.Empty(samples.Requests);

        now = new DateTimeOffset(2026, 7, 3, 0, 0, 50, TimeSpan.Zero);
        await connector.RunCollectionCycleAsync(CancellationToken.None);
        Assert.Empty(samples.Requests);

        now = new DateTimeOffset(2026, 7, 3, 0, 1, 1, TimeSpan.Zero);
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var request = Assert.Single(samples.Requests);
        Assert.Equal(2, request.SampleCount);
        Assert.Equal(15m, request.AverageValue);
        Assert.Equal("opcua:opcua-line-1:temperature:1783036800000", request.SourceSequence);
    }

    [Fact]
    public async Task Run_cycle_does_not_block_notifications_while_posting_closed_bucket()
    {
        var now = new DateTimeOffset(2026, 7, 3, 0, 1, 1, TimeSpan.Zero);
        var opcUa = new CallbackCapturingOpcUaClient(
            [new OpcUaDataChange("ns=2;s=Line1.Temperature", 10m, new DateTimeOffset(2026, 7, 3, 0, 0, 10, TimeSpan.Zero), "Good")]);
        var samples = new BlockingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(opcUa, samples, () => now);

        var runTask = connector.RunCollectionCycleAsync(CancellationToken.None);
        await samples.WaitForRecordAttemptAsync();

        var emitTask = opcUa.EmitAsync(
            new OpcUaDataChange("ns=2;s=Line1.Temperature", 20m, new DateTimeOffset(2026, 7, 3, 0, 1, 2, TimeSpan.Zero), "Good"),
            CancellationToken.None);
        var completed = await Task.WhenAny(emitTask, Task.Delay(TimeSpan.FromMilliseconds(250)));

        samples.AllowRecord();
        await runTask;

        Assert.Same(emitTask, completed);
        Assert.Equal(2, connector.CurrentState.ReceivedSamples);
    }

    [Fact]
    public async Task Run_cycle_drops_late_samples_for_already_posted_bucket()
    {
        var now = new DateTimeOffset(2026, 7, 3, 0, 1, 1, TimeSpan.Zero);
        var opcUa = new SequencedFakeOpcUaClient(
            [
                [new OpcUaDataChange("ns=2;s=Line1.Temperature", 10m, new DateTimeOffset(2026, 7, 3, 0, 0, 10, TimeSpan.Zero), "Good")],
                [new OpcUaDataChange("ns=2;s=Line1.Temperature", 20m, new DateTimeOffset(2026, 7, 3, 0, 0, 20, TimeSpan.Zero), "Good")]
            ]);
        var samples = new IdempotentIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(opcUa, samples, () => now);

        await connector.RunCollectionCycleAsync(CancellationToken.None);
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var request = Assert.Single(samples.StoredRequests);
        Assert.Equal("opcua:opcua-line-1:temperature:1783036800000", request.SourceSequence);
        Assert.Equal(1, samples.AcceptedWriteAttempts);
        Assert.Equal(1, connector.CurrentState.DroppedSamples);
        Assert.Equal(2, connector.CurrentState.ReceivedSamples);
    }

    [Fact]
    public async Task Run_cycle_prunes_sealed_bucket_keys_after_late_sample_guard_window()
    {
        var now = new DateTimeOffset(2026, 7, 3, 0, 1, 1, TimeSpan.Zero);
        var opcUa = new SequencedFakeOpcUaClient(
            [
                [new OpcUaDataChange("ns=2;s=Line1.Temperature", 10m, new DateTimeOffset(2026, 7, 3, 0, 0, 10, TimeSpan.Zero), "Good")],
                []
            ]);
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(opcUa, samples, () => now);

        await connector.RunCollectionCycleAsync(CancellationToken.None);
        var targets = await connector.DiscoverAsync(CancellationToken.None);
        Assert.Equal("1", Assert.Single(targets).Metadata["sealedBucketCount"]);

        now = new DateTimeOffset(2026, 7, 3, 0, 7, 1, TimeSpan.Zero);
        await connector.RunCollectionCycleAsync(CancellationToken.None);

        targets = await connector.DiscoverAsync(CancellationToken.None);
        Assert.Equal("0", Assert.Single(targets).Metadata["sealedBucketCount"]);

        await opcUa.EmitAsync(
            new OpcUaDataChange("ns=2;s=Line1.Temperature", 20m, new DateTimeOffset(2026, 7, 3, 0, 0, 20, TimeSpan.Zero), "Good"),
            CancellationToken.None);

        Assert.Single(samples.Requests);
        Assert.Equal(1, connector.CurrentState.DroppedSamples);
        Assert.Equal(2, connector.CurrentState.ReceivedSamples);
    }

    [Fact]
    public async Task Run_cycle_accepts_common_opcua_unsigned_and_boolean_scalar_values()
    {
        var opcUa = new FakeOpcUaClient(
            [],
            [
                new OpcUaDataChange("ns=2;s=Line1.Temperature", (ushort)40, new DateTimeOffset(2026, 7, 3, 0, 0, 1, TimeSpan.Zero), "Good"),
                new OpcUaDataChange("ns=2;s=Line1.Temperature", true, new DateTimeOffset(2026, 7, 3, 0, 0, 2, TimeSpan.Zero), "Good")
            ]);
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(opcUa, samples);

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        var request = Assert.Single(samples.Requests);
        Assert.Equal(2, request.SampleCount);
        Assert.Equal(1m, request.MinValue);
        Assert.Equal(40m, request.MaxValue);
        Assert.Equal(20.5m, request.AverageValue);
        Assert.Equal(0, connector.CurrentState.DroppedSamples);
    }

    [Fact]
    public async Task Environment_credential_resolver_resolves_username_password_references_without_storing_secret_in_options()
    {
        using var variables = new TemporaryEnvironmentVariables(
            ("NERV_IIP_OPCUA_LINE1_USERNAME", "operator"),
            ("NERV_IIP_OPCUA_LINE1_PASSWORD", "secret-value")).Set();
        var resolver = new EnvironmentOpcUaCredentialResolver();

        var credential = await resolver.ResolveAsync("env:NERV_IIP_OPCUA_LINE1", CancellationToken.None);

        Assert.NotNull(credential);
        Assert.Equal("operator", credential.UserName);
        Assert.Equal("secret-value", credential.Password);
    }

    [Theory]
    [InlineData("None", "None", false)]
    [InlineData("Basic256Sha256", "None", false)]
    [InlineData("None", "SignAndEncrypt", false)]
    [InlineData("Basic256Sha256", "SignAndEncrypt", true)]
    public void Connection_options_require_application_certificate_only_for_secure_sessions(
        string securityPolicy,
        string securityMode,
        bool expected)
    {
        var options = new OpcUaConnectionOptions("opc.tcp://localhost:4840", securityPolicy, securityMode, null, false);

        Assert.Equal(expected, options.UsesSecurity);
    }

    [Fact]
    public async Task Run_cycle_counts_bad_or_non_numeric_notifications_as_dropped_samples()
    {
        var opcUa = new FakeOpcUaClient(
            [],
            [
                new OpcUaDataChange("ns=2;s=Line1.Temperature", 10m, new DateTimeOffset(2026, 7, 3, 0, 0, 1, TimeSpan.Zero), "Bad"),
                new OpcUaDataChange("ns=2;s=Line1.Temperature", "not-a-number", new DateTimeOffset(2026, 7, 3, 0, 0, 2, TimeSpan.Zero), "Good")
            ]);
        var samples = new RecordingIndustrialTelemetrySamplesClient();
        var connector = CreateConnector(opcUa, samples);

        await connector.RunCollectionCycleAsync(CancellationToken.None);

        Assert.Empty(samples.Requests);
        Assert.Equal(2, connector.CurrentState.DroppedSamples);
        Assert.Equal(2, connector.CurrentState.ReceivedSamples);
        var targets = await connector.DiscoverAsync(CancellationToken.None);
        var target = Assert.Single(targets);
        Assert.Equal("2", target.Metadata["droppedSamples"]);
        Assert.Equal("alive", target.CollectionHealth!.Connection!.Status);
    }

    private static OpcUaConnector CreateConnector(
        IOpcUaClient opcUa,
        IIndustrialTelemetrySamplesClient samples,
        Func<DateTimeOffset>? utcNow = null,
        string? collectionConnectorId = null)
    {
        return new OpcUaConnector(
            new OpcUaConnectorOptions(
                ConnectorId: "opcua-line-1",
                ConnectorHostId: "connector-host-001",
                OrganizationId: "org-001",
                EnvironmentId: "env-dev",
                EndpointUrl: "opc.tcp://fake-server:4840",
                SecurityPolicy: "None",
                SecurityMode: "None",
                CredentialReference: "local-dev-user-secret:opcua-line-1",
                BrowseRootNodeId: "ns=0;i=85",
                Tags:
                [
                    new OpcUaTagSubscription(
                        DeviceAssetId: "device-line-1",
                        TagKey: "temperature",
                        NodeId: "ns=2;s=Line1.Temperature",
                        SamplingIntervalMilliseconds: 1000,
                        BucketSeconds: 60)
                ],
                CollectionConnectorId: collectionConnectorId),
            opcUa,
            samples,
            utcNow ?? (() => new DateTimeOffset(2026, 7, 3, 0, 1, 1, TimeSpan.Zero)));
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

    private sealed class BlockingIndustrialTelemetrySamplesClient : IIndustrialTelemetrySamplesClient
    {
        private readonly TaskCompletionSource _recordAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RecordSampleAsync(RecordIndustrialTelemetrySampleRequest request, CancellationToken cancellationToken)
        {
            _recordAttempt.TrySetResult();
            return _release.Task.WaitAsync(cancellationToken);
        }

        public Task WaitForRecordAttemptAsync()
        {
            return _recordAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void AllowRecord()
        {
            _release.TrySetResult();
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

    private sealed class IdempotentIndustrialTelemetrySamplesClient : IIndustrialTelemetrySamplesClient
    {
        private readonly Dictionary<(string? SourceSystem, string? SourceConnector, string DeviceAssetId, string TagKey, string SourceSequence), RecordIndustrialTelemetrySampleRequest> _requests = [];

        public int AcceptedWriteAttempts { get; private set; }
        public IReadOnlyCollection<RecordIndustrialTelemetrySampleRequest> StoredRequests => _requests.Values;

        public Task RecordSampleAsync(RecordIndustrialTelemetrySampleRequest request, CancellationToken cancellationToken)
        {
            AcceptedWriteAttempts++;
            var key = (request.SourceSystem, request.SourceConnector, request.DeviceAssetId, request.TagKey, request.SourceSequence);
            if (_requests.TryGetValue(key, out var existing))
            {
                Assert.Equal(existing.BucketStartUtc, request.BucketStartUtc);
                Assert.Equal(existing.BucketEndUtc, request.BucketEndUtc);
                Assert.Equal(existing.SampleCount, request.SampleCount);
                Assert.Equal(existing.AverageValue, request.AverageValue);
                return Task.CompletedTask;
            }

            _requests[key] = request;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOpcUaClient(
        IReadOnlyList<OpcUaNode> nodes,
        IReadOnlyList<OpcUaDataChange> dataChanges) : IOpcUaClient
    {
        public bool ThrowConnectionLostOnFirstSubscribe { get; init; }
        public int ConnectCount { get; private set; }
        public List<string> BrowsedNodeIds { get; } = [];
        public List<string> SubscribedNodeIds { get; } = [];
        private bool _thrown;

        public Task ConnectAsync(OpcUaConnectionOptions options, CancellationToken cancellationToken)
        {
            ConnectCount++;
            return Task.CompletedTask;
        }

        public Task ConnectAsync(OpcUaConnectionOptions options, Action onConnectionLost, CancellationToken cancellationToken)
        {
            return ConnectAsync(options, cancellationToken);
        }

        public Task<IReadOnlyList<OpcUaNode>> BrowseAsync(string rootNodeId, CancellationToken cancellationToken)
        {
            BrowsedNodeIds.Add(rootNodeId);
            return Task.FromResult(nodes);
        }

        public async Task SubscribeAsync(
            IReadOnlyList<OpcUaTagSubscription> tags,
            Func<OpcUaDataChange, CancellationToken, Task> onDataChange,
            CancellationToken cancellationToken)
        {
            SubscribedNodeIds.AddRange(tags.Select(x => x.NodeId));
            if (ThrowConnectionLostOnFirstSubscribe && !_thrown)
            {
                _thrown = true;
                throw new OpcUaConnectionLostException("simulated disconnect");
            }

            foreach (var change in dataChanges)
            {
                await onDataChange(change, cancellationToken);
            }
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CallbackCapturingOpcUaClient(IReadOnlyList<OpcUaDataChange> initialDataChanges) : IOpcUaClient
    {
        private Func<OpcUaDataChange, CancellationToken, Task>? _onDataChange;

        public Task ConnectAsync(OpcUaConnectionOptions options, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ConnectAsync(OpcUaConnectionOptions options, Action onConnectionLost, CancellationToken cancellationToken)
        {
            return ConnectAsync(options, cancellationToken);
        }

        public Task<IReadOnlyList<OpcUaNode>> BrowseAsync(string rootNodeId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<OpcUaNode>>([]);
        }

        public async Task SubscribeAsync(
            IReadOnlyList<OpcUaTagSubscription> tags,
            Func<OpcUaDataChange, CancellationToken, Task> onDataChange,
            CancellationToken cancellationToken)
        {
            _onDataChange = onDataChange;
            foreach (var change in initialDataChanges)
            {
                await onDataChange(change, cancellationToken);
            }
        }

        public Task EmitAsync(OpcUaDataChange change, CancellationToken cancellationToken)
        {
            if (_onDataChange is null)
            {
                throw new InvalidOperationException("OPC UA subscription callback has not been captured.");
            }

            return _onDataChange(change, cancellationToken);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SequencedFakeOpcUaClient(IReadOnlyList<IReadOnlyList<OpcUaDataChange>> batches) : IOpcUaClient
    {
        private int _index;
        private Func<OpcUaDataChange, CancellationToken, Task>? _onDataChange;

        public Task ConnectAsync(OpcUaConnectionOptions options, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ConnectAsync(OpcUaConnectionOptions options, Action onConnectionLost, CancellationToken cancellationToken)
        {
            return ConnectAsync(options, cancellationToken);
        }

        public Task<IReadOnlyList<OpcUaNode>> BrowseAsync(string rootNodeId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<OpcUaNode>>([]);
        }

        public async Task SubscribeAsync(
            IReadOnlyList<OpcUaTagSubscription> tags,
            Func<OpcUaDataChange, CancellationToken, Task> onDataChange,
            CancellationToken cancellationToken)
        {
            _onDataChange = onDataChange;
            var changes = _index < batches.Count ? batches[_index++] : [];
            foreach (var change in changes)
            {
                await onDataChange(change, cancellationToken);
            }
        }

        public Task EmitAsync(OpcUaDataChange change, CancellationToken cancellationToken)
        {
            if (_onDataChange is null)
            {
                throw new InvalidOperationException("OPC UA subscription callback has not been captured.");
            }

            return _onDataChange(change, cancellationToken);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class AcknowledgementControlledOpcUaClient(
        bool autoAcknowledge = false,
        bool failKeepAliveBeforeReturn = false) : IOpcUaClient
    {
        private readonly TaskCompletionSource _subscribeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _acknowledgement = CreateAcknowledgement(autoAcknowledge);
        private Action? _onConnectionLost;

        public Task ConnectAsync(OpcUaConnectionOptions options, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ConnectAsync(OpcUaConnectionOptions options, Action onConnectionLost, CancellationToken cancellationToken)
        {
            _onConnectionLost = onConnectionLost;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OpcUaNode>> BrowseAsync(string rootNodeId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<OpcUaNode>>([]);
        }

        public async Task SubscribeAsync(
            IReadOnlyList<OpcUaTagSubscription> tags,
            Func<OpcUaDataChange, CancellationToken, Task> onDataChange,
            CancellationToken cancellationToken)
        {
            _subscribeStarted.TrySetResult();
            await _acknowledgement.Task.WaitAsync(cancellationToken);
            if (failKeepAliveBeforeReturn)
            {
                _onConnectionLost?.Invoke();
            }
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task WaitForSubscribeAsync() => _subscribeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void AcknowledgeSubscription() => _acknowledgement.TrySetResult();

        public void FailKeepAlive()
        {
            _acknowledgement = CreateAcknowledgement(autoAcknowledge: true);
            _onConnectionLost?.Invoke();
        }

        private static TaskCompletionSource CreateAcknowledgement(bool autoAcknowledge)
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (autoAcknowledge)
            {
                source.SetResult();
            }

            return source;
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
