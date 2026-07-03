using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa;

public sealed class OpcUaConnector(
    OpcUaConnectorOptions options,
    IOpcUaClient opcUaClient,
    IIndustrialTelemetrySamplesClient samplesClient) : IConnector, IOpcUaCollectionConnector
{
    private readonly Dictionary<(string NodeId, DateTimeOffset BucketStartUtc), TelemetryBucket> _buckets = [];
    private readonly Dictionary<string, OpcUaTagSubscription> _tagsByNodeId = options.Tags.ToDictionary(x => x.NodeId, StringComparer.Ordinal);

    public OpcUaConnectorState CurrentState { get; private set; } = new(
        "stopped",
        "unknown",
        "OPC UA collector has not run yet.",
        0,
        0,
        0,
        0,
        0,
        null,
        null);

    public async Task RunCollectionCycleAsync(CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (true)
        {
            try
            {
                await ConnectBrowseAndSubscribeAsync(cancellationToken);
                await FlushAllBucketsAsync(cancellationToken);
                MarkRunning();
                return;
            }
            catch (OpcUaConnectionLostException) when (attempts < options.MaxReconnectAttempts)
            {
                attempts++;
                CurrentState = CurrentState with
                {
                    HealthStatus = "degraded",
                    Summary = "OPC UA subscription disconnected; reconnecting.",
                    ReconnectCount = CurrentState.ReconnectCount + 1,
                    SubscriptionRecoveries = CurrentState.SubscriptionRecoveries + 1
                };
                await opcUaClient.DisconnectAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CurrentState = CurrentState with
                {
                    ReportedStatus = "stopped",
                    HealthStatus = "unhealthy",
                    Summary = $"OPC UA collection failed: {ex.Message}"
                };
                throw;
            }
        }
    }

    public Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var state = CurrentState;
        IReadOnlyList<ConnectorTarget> targets =
        [
            new(
                $"opcua-{options.ConnectorId}",
                $"OPC UA {options.ConnectorId}",
                "opcua",
                "opcua-collector",
                "OPC UA Collector",
                "1.0",
                $"opcua-{options.ConnectorId}",
                $"OPC UA {options.EndpointUrl}",
                state.ReportedStatus,
                state.HealthStatus,
                [
                    new ConnectorCapability("runtime.status", "1.0", "runtime", ["inspect"]),
                    new ConnectorCapability("industrial-telemetry.ingest", "1.0", "telemetry", ["browse", "subscribe", "sample"])
                ],
                CreateMetadata(state))
        ];
        return Task.FromResult(targets);
    }

    private async Task ConnectBrowseAndSubscribeAsync(CancellationToken cancellationToken)
    {
        await opcUaClient.ConnectAsync(new OpcUaConnectionOptions(
            options.EndpointUrl,
            options.SecurityPolicy,
            options.SecurityMode,
            options.CredentialReference,
            options.AutoAcceptUntrustedServerCertificates), cancellationToken);

        _ = await opcUaClient.BrowseAsync(options.BrowseRootNodeId, cancellationToken);

        if (options.Tags.Count == 0)
        {
            CurrentState = CurrentState with
            {
                ReportedStatus = "running",
                HealthStatus = "degraded",
                Summary = "OPC UA collector is connected but has no configured tag subscriptions."
            };
            return;
        }

        await opcUaClient.SubscribeAsync(options.Tags, HandleDataChangeAsync, cancellationToken);
    }

    private Task HandleDataChangeAsync(OpcUaDataChange change, CancellationToken cancellationToken)
    {
        if (!_tagsByNodeId.TryGetValue(change.NodeId, out var tag)
            || !string.Equals(change.Status, "Good", StringComparison.OrdinalIgnoreCase)
            || !OpcUaValueConversion.TryConvertDecimal(change.Value, out var value))
        {
            CurrentState = CurrentState with
            {
                DroppedSamples = CurrentState.DroppedSamples + 1,
                HealthStatus = "degraded",
                Summary = "OPC UA collector dropped one or more invalid samples."
            };
            return Task.CompletedTask;
        }

        var bucketStartUtc = FloorToBucket(change.SourceTimestampUtc, tag.BucketSeconds);
        var bucketKey = (tag.NodeId, bucketStartUtc);
        if (!_buckets.TryGetValue(bucketKey, out var bucket))
        {
            bucket = new TelemetryBucket(tag, bucketStartUtc, bucketStartUtc.AddSeconds(tag.BucketSeconds));
            _buckets[bucketKey] = bucket;
        }

        bucket.Add(value);
        CurrentState = CurrentState with
        {
            ReceivedSamples = CurrentState.ReceivedSamples + 1,
            LastSampleAtUtc = change.SourceTimestampUtc
        };
        return Task.CompletedTask;
    }

    private async Task FlushAllBucketsAsync(CancellationToken cancellationToken)
    {
        foreach (var bucket in _buckets.Values.OrderBy(x => x.BucketStartUtc).ThenBy(x => x.Tag.TagKey))
        {
            await samplesClient.RecordSampleAsync(CreateRequest(bucket), cancellationToken);
            CurrentState = CurrentState with
            {
                PostedBuckets = CurrentState.PostedBuckets + 1,
                LastPostedBucketEndUtc = bucket.BucketEndUtc
            };
        }

        _buckets.Clear();
    }

    private RecordIndustrialTelemetrySampleRequest CreateRequest(TelemetryBucket bucket)
    {
        var normalizedTagKey = bucket.Tag.TagKey.Trim().ToLowerInvariant();
        var bucketStartUnixMilliseconds = bucket.BucketStartUtc.ToUnixTimeMilliseconds();
        return new RecordIndustrialTelemetrySampleRequest(
            options.OrganizationId,
            options.EnvironmentId,
            bucket.Tag.DeviceAssetId,
            normalizedTagKey,
            bucket.BucketStartUtc,
            bucket.BucketEndUtc,
            bucket.SampleCount,
            bucket.MinValue,
            bucket.MaxValue,
            bucket.AverageValue,
            $"opcua:{options.ConnectorId}:{normalizedTagKey}:{bucketStartUnixMilliseconds}",
            "opcua",
            $"{options.ConnectorHostId}/{options.ConnectorId}");
    }

    private void MarkRunning()
    {
        var health = CurrentState.DroppedSamples > 0 || CurrentState.ReconnectCount > 0 ? "degraded" : "healthy";
        CurrentState = CurrentState with
        {
            ReportedStatus = "running",
            HealthStatus = health,
            Summary = health == "healthy"
                ? "OPC UA collector is connected and sampling."
                : "OPC UA collector is connected with recoverable sampling issues."
        };
    }

    private IReadOnlyDictionary<string, string> CreateMetadata(OpcUaConnectorState state)
    {
        var metadata = new Dictionary<string, string>
        {
            ["endpointUrl"] = options.EndpointUrl,
            ["securityPolicy"] = options.SecurityPolicy,
            ["securityMode"] = options.SecurityMode,
            ["credentialReference"] = options.CredentialReference ?? string.Empty,
            ["browseRootNodeId"] = options.BrowseRootNodeId,
            ["tagCount"] = options.Tags.Count.ToString(),
            ["receivedSamples"] = state.ReceivedSamples.ToString(),
            ["postedBuckets"] = state.PostedBuckets.ToString(),
            ["droppedSamples"] = state.DroppedSamples.ToString(),
            ["reconnectCount"] = state.ReconnectCount.ToString(),
            ["subscriptionRecoveries"] = state.SubscriptionRecoveries.ToString()
        };

        if (state.LastSampleAtUtc is not null)
        {
            metadata["lastSampleAtUtc"] = state.LastSampleAtUtc.Value.ToString("O");
        }

        if (state.LastPostedBucketEndUtc is not null)
        {
            metadata["lastPostedBucketEndUtc"] = state.LastPostedBucketEndUtc.Value.ToString("O");
        }

        return metadata;
    }

    private static DateTimeOffset FloorToBucket(DateTimeOffset timestampUtc, int bucketSeconds)
    {
        if (bucketSeconds <= 0)
        {
            throw new InvalidOperationException("OPC UA tag bucketSeconds must be greater than zero.");
        }

        var unixSeconds = timestampUtc.ToUnixTimeSeconds();
        var bucketStartUnixSeconds = unixSeconds - unixSeconds % bucketSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(bucketStartUnixSeconds);
    }
}
