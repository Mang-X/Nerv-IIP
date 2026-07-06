using System.Globalization;
using System.Text.Json;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.OpcUa;

namespace Nerv.IIP.ConnectorHost.Connectors.Mqtt;

public sealed class MqttConnector(
    MqttConnectorOptions options,
    IMqttSubscriptionClient mqttClient,
    IIndustrialTelemetrySamplesClient samplesClient,
    Func<DateTimeOffset>? utcNow = null) : IConnector, IIndustrialTelemetryCollectionConnector
{
    private readonly Dictionary<(string TopicFilter, string TagKey, DateTimeOffset BucketStartUtc), MqttTelemetryBucket> _buckets = [];
    private readonly Dictionary<(string TopicFilter, string TagKey, DateTimeOffset BucketStartUtc), DateTimeOffset> _sealedBucketKeys = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly TimeSpan _sealedBucketRetention = CalculateSealedBucketRetention(options.TopicMappings);

    public MqttConnectorState CurrentState { get; private set; } = new(
        "stopped",
        "unknown",
        "MQTT collector has not run yet.",
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
            var subscriptionCompleted = false;
            try
            {
                if (options.TopicMappings.Count == 0)
                {
                    await UpdateStateAsync(state => state with
                    {
                        ReportedStatus = "running",
                        HealthStatus = "degraded",
                        Summary = "MQTT collector is configured but has no topic mappings."
                    }, cancellationToken);
                    return;
                }

                var topicFilters = options.TopicMappings.Select(x => x.TopicFilter).Distinct(StringComparer.Ordinal).ToArray();
                await mqttClient.ConnectAndSubscribeAsync(
                    new MqttConnectionOptions(options.Broker, options.ClientId, options.CredentialReference),
                    topicFilters,
                    HandleMessageAsync,
                    cancellationToken);

                subscriptionCompleted = true;
                await FlushClosedBucketsAsync(_utcNow(), cancellationToken);
                await MarkRunningAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (!subscriptionCompleted && ex is not OperationCanceledException && attempts < options.MaxReconnectAttempts)
            {
                attempts++;
                await UpdateStateAsync(state => state with
                {
                    HealthStatus = "degraded",
                    Summary = "MQTT subscription failed; reconnecting.",
                    ReconnectCount = state.ReconnectCount + 1
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await UpdateStateAsync(state => state with
                {
                    ReportedStatus = "stopped",
                    HealthStatus = "unhealthy",
                    Summary = $"MQTT collection failed: {ex.Message}"
                }, cancellationToken);
                throw;
            }
        }
    }

    public async Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
    {
        MqttConnectorState state;
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
                $"mqtt-{options.ConnectorId}",
                $"MQTT {options.ConnectorId}",
                "mqtt",
                "mqtt-collector",
                "MQTT Collector",
                "1.0",
                $"mqtt-{options.ConnectorId}",
                $"MQTT {options.Broker}",
                state.ReportedStatus,
                state.HealthStatus,
                [
                    new ConnectorCapability("runtime.status", "1.0", "runtime", ["inspect"]),
                    new ConnectorCapability("industrial-telemetry.ingest", "1.0", "telemetry", ["subscribe", "sample"])
                ],
                metadata)
        ];
        return targets;
    }

    private async Task HandleMessageAsync(MqttInboundMessage message, CancellationToken cancellationToken)
    {
        var matchedMappings = options.TopicMappings
            .Where(x => TopicMatches(x.TopicFilter, message.Topic))
            .ToArray();
        if (matchedMappings.Length == 0)
        {
            await MarkDroppedSampleAsync(cancellationToken);
            return;
        }

        foreach (var mapping in matchedMappings)
        {
            if (!TryExtractDecimal(message.Payload, mapping.ValueJsonPath, out var value))
            {
                await MarkDroppedSampleAsync(cancellationToken);
                continue;
            }

            await AddSampleAsync(mapping, value, message.ObservedAtUtc, cancellationToken);
        }
    }

    private async Task AddSampleAsync(MqttTopicMapping mapping, decimal value, DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var bucketStartUtc = FloorToBucket(observedAtUtc, mapping.BucketSeconds);
            var bucketKey = (mapping.TopicFilter, mapping.TagKey, bucketStartUtc);
            var bucketEndUtc = bucketStartUtc.AddSeconds(mapping.BucketSeconds);
            if (bucketEndUtc < _utcNow() - _sealedBucketRetention || _sealedBucketKeys.ContainsKey(bucketKey))
            {
                MarkDroppedSample();
                return;
            }

            if (!_buckets.TryGetValue(bucketKey, out var bucket))
            {
                bucket = new MqttTelemetryBucket(mapping, bucketStartUtc, bucketEndUtc);
                _buckets[bucketKey] = bucket;
            }

            bucket.Add(value);
            CurrentState = CurrentState with
            {
                ReceivedSamples = CurrentState.ReceivedSamples + 1,
                LastSampleAtUtc = observedAtUtc
            };
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
                .ThenBy(x => x.Value.Mapping.TagKey)
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

    private async Task MarkBucketPostedAsync(MqttTelemetryBucket bucket, CancellationToken cancellationToken)
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

    private async Task MarkDroppedSampleAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            MarkDroppedSample();
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
            Summary = "MQTT collector dropped one or more unmapped, invalid, or late messages."
        };
    }

    private RecordIndustrialTelemetrySampleRequest CreateRequest(MqttTelemetryBucket bucket)
    {
        var normalizedTagKey = bucket.Mapping.TagKey.Trim().ToLowerInvariant();
        var bucketStartUnixMilliseconds = bucket.BucketStartUtc.ToUnixTimeMilliseconds();
        return new RecordIndustrialTelemetrySampleRequest(
            options.OrganizationId,
            options.EnvironmentId,
            bucket.Mapping.DeviceAssetId,
            normalizedTagKey,
            bucket.BucketStartUtc,
            bucket.BucketEndUtc,
            bucket.SampleCount,
            bucket.MinValue,
            bucket.MaxValue,
            bucket.AverageValue,
            $"mqtt:{options.ConnectorId}:{normalizedTagKey}:{bucketStartUnixMilliseconds}",
            "mqtt",
            $"{options.ConnectorHostId}/{options.ConnectorId}");
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
                    ? "MQTT collector is subscribed and sampling."
                    : "MQTT collector is subscribed with recoverable sampling issues."
            };
        }, cancellationToken);
    }

    private async Task UpdateStateAsync(Func<MqttConnectorState, MqttConnectorState> update, CancellationToken cancellationToken)
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

    private IReadOnlyDictionary<string, string> CreateMetadata(MqttConnectorState state)
    {
        var metadata = new Dictionary<string, string>
        {
            ["broker"] = options.Broker,
            ["clientId"] = options.ClientId,
            ["credentialReference"] = options.CredentialReference ?? string.Empty,
            ["topicMappingCount"] = options.TopicMappings.Count.ToString(),
            ["receivedSamples"] = state.ReceivedSamples.ToString(),
            ["postedBuckets"] = state.PostedBuckets.ToString(),
            ["droppedSamples"] = state.DroppedSamples.ToString(),
            ["reconnectCount"] = state.ReconnectCount.ToString(),
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

    private static bool TopicMatches(string filter, string topic)
    {
        var filterLevels = filter.Split('/');
        var topicLevels = topic.Split('/');
        for (var i = 0; i < filterLevels.Length; i++)
        {
            if (filterLevels[i] == "#")
            {
                return i == filterLevels.Length - 1;
            }

            if (i >= topicLevels.Length)
            {
                return false;
            }

            if (filterLevels[i] != "+" && !string.Equals(filterLevels[i], topicLevels[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return filterLevels.Length == topicLevels.Length;
    }

    private static bool TryExtractDecimal(string payload, string jsonPath, out decimal value)
    {
        value = 0;
        if (!jsonPath.StartsWith("$.", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var current = document.RootElement;
            foreach (var segment in jsonPath[2..].Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    return false;
                }
            }

            return current.ValueKind switch
            {
                JsonValueKind.Number => current.TryGetDecimal(out value),
                JsonValueKind.String => decimal.TryParse(current.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value),
                JsonValueKind.True => SetDecimal(out value, 1m),
                JsonValueKind.False => SetDecimal(out value, 0m),
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool SetDecimal(out decimal value, decimal source)
    {
        value = source;
        return true;
    }

    private static DateTimeOffset FloorToBucket(DateTimeOffset timestampUtc, int bucketSeconds)
    {
        if (bucketSeconds <= 0)
        {
            throw new InvalidOperationException("MQTT topic mapping bucketSeconds must be greater than zero.");
        }

        var unixSeconds = timestampUtc.ToUnixTimeSeconds();
        var bucketStartUnixSeconds = unixSeconds - unixSeconds % bucketSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(bucketStartUnixSeconds);
    }

    private static TimeSpan CalculateSealedBucketRetention(IReadOnlyList<MqttTopicMapping> mappings)
    {
        var maxBucketSeconds = mappings.Count == 0 ? 60 : mappings.Max(x => Math.Max(x.BucketSeconds, 1));
        return TimeSpan.FromSeconds(Math.Max(60, maxBucketSeconds * 5));
    }

    private sealed record BucketFlushItem(
        (string TopicFilter, string TagKey, DateTimeOffset BucketStartUtc) Key,
        MqttTelemetryBucket Bucket);
}
