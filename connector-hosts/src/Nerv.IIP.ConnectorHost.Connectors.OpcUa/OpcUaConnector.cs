using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa;

public sealed class OpcUaConnector(
    OpcUaConnectorOptions options,
    IOpcUaClient opcUaClient,
    IIndustrialTelemetrySamplesClient samplesClient,
    Func<DateTimeOffset>? utcNow = null) : IConnector, IIndustrialTelemetryCollectionConnector
{
    private readonly Dictionary<(string NodeId, DateTimeOffset BucketStartUtc), TelemetryBucket> _buckets = [];
    private readonly Dictionary<(string NodeId, DateTimeOffset BucketStartUtc), DateTimeOffset> _sealedBucketKeys = [];
    private readonly Dictionary<string, OpcUaTagSubscription> _tagsByNodeId = options.Tags.ToDictionary(x => x.NodeId, StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly TimeSpan _sealedBucketRetention = CalculateSealedBucketRetention(options.Tags);
    private readonly Guid _counterEpoch = Guid.CreateVersion7();

    public OpcUaConnectorState CurrentState { get; private set; } = new(
        "stopped",
        "unknown",
        "OPC UA collector has not run yet.",
        0,
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
                await FlushClosedBucketsAsync(_utcNow(), cancellationToken);
                await MarkRunningAsync(cancellationToken);
                return;
            }
            catch (OpcUaConnectionLostException) when (attempts < options.MaxReconnectAttempts)
            {
                attempts++;
                await UpdateStateAsync(state => state with
                {
                    HealthStatus = "degraded",
                    Summary = "OPC UA subscription disconnected; reconnecting.",
                    ErrorCount = state.ErrorCount + 1,
                    ReconnectCount = state.ReconnectCount + 1,
                    SubscriptionRecoveries = state.SubscriptionRecoveries + 1
                }, cancellationToken);
                await opcUaClient.DisconnectAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await UpdateStateAsync(state => state with
                {
                    ReportedStatus = "stopped",
                    HealthStatus = "unhealthy",
                    ErrorCount = state.ErrorCount + 1,
                    Summary = $"OPC UA collection failed: {ex.Message}"
                }, cancellationToken);
                throw;
            }
        }
    }

    public async Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
    {
        OpcUaConnectorState state;
        IReadOnlyDictionary<string, string> metadata;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            state = CurrentState;
            metadata = CreateMetadata(state);
        }
        finally
        {
            _gate.Release();
        }

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
                metadata,
                CreateCollectionHealth(state))
        ];
        return targets;
    }

    private ConnectorCollectionHealthSnapshot CreateCollectionHealth(OpcUaConnectorState state)
    {
        var known = state.ReceivedSamples > 0 || state.DroppedSamples > 0 || state.ErrorCount > 0 || state.LastSampleAtUtc is not null;
        return new($"opcua-{options.ConnectorId}", "opcua", _counterEpoch,
            known ? state.ReceivedSamples : null, known ? state.DroppedSamples : null, known ? state.ErrorCount : null, state.LastSampleAtUtc);
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
            await UpdateStateAsync(state => state with
            {
                ReportedStatus = "running",
                HealthStatus = "degraded",
                Summary = "OPC UA collector is connected but has no configured tag subscriptions."
            }, cancellationToken);
            return;
        }

        await opcUaClient.SubscribeAsync(options.Tags, HandleDataChangeAsync, cancellationToken);
    }

    private async Task HandleDataChangeAsync(OpcUaDataChange change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CurrentState = CurrentState with
            {
                ReceivedSamples = CurrentState.ReceivedSamples + 1,
                LastSampleAtUtc = change.SourceTimestampUtc
            };
            if (!_tagsByNodeId.TryGetValue(change.NodeId, out var tag)
                || !string.Equals(change.Status, "Good", StringComparison.OrdinalIgnoreCase)
                || !OpcUaValueConversion.TryConvertDecimal(change.Value, out var value))
            {
                MarkDroppedSample();
                return;
            }

            var bucketStartUtc = FloorToBucket(change.SourceTimestampUtc, tag.BucketSeconds);
            var bucketKey = (tag.NodeId, bucketStartUtc);
            var bucketEndUtc = bucketStartUtc.AddSeconds(tag.BucketSeconds);
            if (bucketEndUtc < _utcNow() - _sealedBucketRetention)
            {
                MarkDroppedSample();
                return;
            }

            if (_sealedBucketKeys.ContainsKey(bucketKey))
            {
                MarkDroppedSample();
                return;
            }

            if (!_buckets.TryGetValue(bucketKey, out var bucket))
            {
                bucket = new TelemetryBucket(tag, bucketStartUtc, bucketEndUtc);
                _buckets[bucketKey] = bucket;
            }

            bucket.Add(value);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task FlushClosedBucketsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        while (true)
        {
            var item = await TryTakeNextClosedBucketAsync(nowUtc, cancellationToken);
            if (item is null)
            {
                return;
            }

            try
            {
                await samplesClient.RecordSampleAsync(CreateRequest(item.Bucket), cancellationToken);
            }
            catch
            {
                await RestoreBucketForRetryAsync(item, cancellationToken);
                throw;
            }

            await MarkBucketPostedAsync(item.Bucket, cancellationToken);
        }
    }

    private async Task<BucketFlushItem?> TryTakeNextClosedBucketAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            PruneSealedBucketKeys(nowUtc);
            var closedBuckets = _buckets
                .Where(x => x.Value.BucketEndUtc <= nowUtc)
                .OrderBy(x => x.Value.BucketStartUtc)
                .ThenBy(x => x.Value.Tag.TagKey)
                .ToArray();
            if (closedBuckets.Length == 0)
            {
                return null;
            }

            var (key, bucket) = closedBuckets[0];
            _buckets.Remove(key);
            _sealedBucketKeys[key] = bucket.BucketEndUtc;
            return new BucketFlushItem(key, bucket);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PruneSealedBucketKeys(DateTimeOffset nowUtc)
    {
        var cutoffUtc = nowUtc - _sealedBucketRetention;
        var staleKeys = _sealedBucketKeys
            .Where(x => x.Value < cutoffUtc)
            .Select(x => x.Key)
            .ToArray();
        foreach (var key in staleKeys)
        {
            _sealedBucketKeys.Remove(key);
        }
    }

    private async Task RestoreBucketForRetryAsync(BucketFlushItem item, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _sealedBucketKeys.Remove(item.Key);
            _buckets.TryAdd(item.Key, item.Bucket);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MarkBucketPostedAsync(TelemetryBucket bucket, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CurrentState = CurrentState with
            {
                PostedBuckets = CurrentState.PostedBuckets + 1,
                LastPostedBucketEndUtc = bucket.BucketEndUtc
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private void MarkDroppedSample()
    {
        CurrentState = CurrentState with
        {
            DroppedSamples = CurrentState.DroppedSamples + 1,
            HealthStatus = "degraded",
            Summary = "OPC UA collector dropped one or more invalid or late samples."
        };
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
            $"{options.ConnectorHostId}/{options.ConnectorId}",
            FirstValue: bucket.FirstValue,
            LastValue: bucket.LastValue);
    }

    private async Task MarkRunningAsync(CancellationToken cancellationToken)
    {
        await UpdateStateAsync(state =>
        {
            var health = state.DroppedSamples > 0 || state.ReconnectCount > 0 ? "degraded" : "healthy";
            return state with
            {
                ReportedStatus = "running",
                HealthStatus = health,
                Summary = health == "healthy"
                ? "OPC UA collector is connected and sampling."
                : "OPC UA collector is connected with recoverable sampling issues."
            };
        }, cancellationToken);
    }

    private async Task UpdateStateAsync(Func<OpcUaConnectorState, OpcUaConnectorState> update, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CurrentState = update(CurrentState);
        }
        finally
        {
            _gate.Release();
        }
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
            ["subscriptionRecoveries"] = state.SubscriptionRecoveries.ToString(),
            ["sealedBucketCount"] = _sealedBucketKeys.Count.ToString()
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

    private static TimeSpan CalculateSealedBucketRetention(IReadOnlyList<OpcUaTagSubscription> tags)
    {
        var maxBucketSeconds = tags.Count == 0 ? 60 : tags.Max(x => Math.Max(x.BucketSeconds, 1));
        return TimeSpan.FromSeconds(Math.Max(60, maxBucketSeconds * 5));
    }

    private sealed record BucketFlushItem(
        (string NodeId, DateTimeOffset BucketStartUtc) Key,
        TelemetryBucket Bucket);
}
