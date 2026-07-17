using System.Net.Sockets;
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.ConnectorHost.Connectors.OpcUa;

namespace Nerv.IIP.ConnectorHost.Connectors.Modbus;

public sealed class ModbusConnector(
    ModbusConnectorOptions options,
    IModbusTcpClient modbusClient,
    IIndustrialTelemetrySamplesClient samplesClient,
    Func<DateTimeOffset>? utcNow = null,
    TimeProvider? timeProvider = null,
    IConnectorReportSignal? reportSignal = null) : IConnector, IIndustrialTelemetryCollectionConnector, IConnectorConnectionMonitor
{
    private readonly Dictionary<(byte UnitId, ModbusRegisterTable Table, ushort Address, DateTimeOffset BucketStartUtc), ModbusTelemetryBucket> _buckets = [];
    private readonly Dictionary<(byte UnitId, ModbusRegisterTable Table, ushort Address, DateTimeOffset BucketStartUtc), DateTimeOffset> _sealedBucketKeys = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly TimeSpan _sealedBucketRetention = CalculateSealedBucketRetention(options.Registers);
    private readonly Guid _counterEpoch = Guid.CreateVersion7();
    private readonly ConnectorConnectionStateTracker _connectionTracker = new(
        options.EffectiveCollectionConnectorId,
        timeProvider ?? TimeProvider.System,
        reportSignal is null ? static _ => { } : reportSignal.Signal);

    public ModbusConnectorState CurrentState { get; private set; } = new(
        "stopped",
        "unknown",
        "Modbus TCP collector has not run yet.",
        0,
        0,
        0,
        0,
        0,
        null,
        null);

    public async Task RunCollectionCycleAsync(CancellationToken cancellationToken)
    {
        var enabledMappings = options.Registers.Where(x => x.Enabled).ToArray();
        if (enabledMappings.Length == 0)
        {
            await UpdateStateAsync(state => state with
            {
                ReportedStatus = "running",
                HealthStatus = "degraded",
                Summary = "Modbus TCP collector has no enabled register mappings."
            }, cancellationToken);
            return;
        }

        var attempts = 0;
        while (true)
        {
            var pollingCompleted = false;
            try
            {
                await ConnectForCollectionAsync(cancellationToken);

                var observedAtUtc = _utcNow();
                foreach (var mapping in enabledMappings)
                {
                    var samples = await ReadForCollectionAsync(mapping, observedAtUtc, cancellationToken);
                    _connectionTracker.MarkAlive();
                    if (samples.Count == 0)
                    {
                        continue;
                    }

                    foreach (var sample in samples)
                    {
                        await HandleSampleAsync(mapping, sample, cancellationToken);
                    }
                }

                pollingCompleted = true;
                await FlushClosedBucketsAsync(_utcNow(), cancellationToken);
                await MarkRunningAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (!pollingCompleted && ex is not OperationCanceledException && attempts < options.MaxReconnectAttempts)
            {
                attempts++;
                await UpdateStateAsync(state => state with
                {
                    HealthStatus = "degraded",
                    Summary = "Modbus TCP polling failed; reconnecting.",
                    ErrorCount = state.ErrorCount + 1,
                    ReconnectCount = state.ReconnectCount + 1
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await UpdateStateAsync(state => state with
                {
                    ReportedStatus = "stopped",
                    HealthStatus = "unhealthy",
                    ErrorCount = state.ErrorCount + 1,
                    Summary = "Modbus TCP collection failed."
                }, cancellationToken);
                throw;
            }
        }
    }

    private async Task ConnectForCollectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await modbusClient.ConnectAsync(
                new ModbusConnectionOptions(options.Endpoint, options.CredentialReference),
                cancellationToken);
        }
        catch (Exception ex) when (IsConnectionLoss(ex))
        {
            MarkLostIfTransportFailure(ex);
            throw;
        }
    }

    private async Task<IReadOnlyList<ModbusRegisterSample>> ReadForCollectionAsync(
        ModbusRegisterMapping mapping,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            return await modbusClient.ReadRegistersAsync(mapping, observedAtUtc, cancellationToken);
        }
        catch (Exception ex) when (IsConnectionLoss(ex))
        {
            MarkLostIfTransportFailure(ex);
            throw;
        }
    }

    public async Task RunConnectionCheckAsync(CancellationToken cancellationToken)
    {
        var mapping = options.Registers.FirstOrDefault(x => x.Enabled);
        if (mapping is null)
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            await modbusClient.ConnectAsync(
                new ModbusConnectionOptions(options.Endpoint, options.CredentialReference),
                timeout.Token);
            await modbusClient.ProbeAsync(mapping, timeout.Token);
            _connectionTracker.MarkAlive();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _connectionTracker.MarkLost("transport", "modbus.probe-timeout");
            throw;
        }
        catch (TimeoutException)
        {
            _connectionTracker.MarkLost("transport", "modbus.probe-timeout");
            throw;
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            _connectionTracker.MarkLost("transport", "modbus.transport-failure");
            throw;
        }
    }

    public async Task<IReadOnlyList<ConnectorTarget>> DiscoverAsync(CancellationToken cancellationToken)
    {
        ModbusConnectorState state;
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
                options.EffectiveCollectionConnectorId,
                $"Modbus TCP {options.ConnectorId}",
                "modbus-tcp",
                "modbus-collector",
                "Modbus TCP Collector",
                "1.0",
                options.EffectiveCollectionConnectorId,
                $"Modbus TCP {options.Endpoint}",
                state.ReportedStatus,
                state.HealthStatus,
                [
                    new ConnectorCapability("runtime.status", "1.0", "runtime", ["inspect"]),
                    new ConnectorCapability("industrial-telemetry.ingest", "1.0", "telemetry", ["poll", "sample"])
                ],
                metadata,
                CreateCollectionHealth(state))
        ];
        return targets;
    }

    private ConnectorCollectionHealthSnapshot CreateCollectionHealth(ModbusConnectorState state)
    {
        var known = state.ReceivedSamples > 0 || state.DroppedSamples > 0 || state.ErrorCount > 0 || state.LastSampleAtUtc is not null;
        return new(options.EffectiveCollectionConnectorId, "modbus", _counterEpoch,
            known ? state.ReceivedSamples : null, known ? state.DroppedSamples : null, known ? state.ErrorCount : null, state.LastSampleAtUtc,
            _connectionTracker.Snapshot);
    }

    private void MarkLostIfTransportFailure(Exception exception)
    {
        if (exception is TimeoutException)
        {
            _connectionTracker.MarkLost("transport", "modbus.transaction-timeout");
        }
        else if (IsTransportFailure(exception))
        {
            _connectionTracker.MarkLost("transport", "modbus.transport-failure");
        }
    }

    private static bool IsTransportFailure(Exception exception)
    {
        return exception is SocketException or IOException;
    }

    private static bool IsConnectionLoss(Exception exception)
    {
        return exception is TimeoutException || IsTransportFailure(exception);
    }

    private async Task HandleSampleAsync(ModbusRegisterMapping mapping, ModbusRegisterSample sample, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            CurrentState = CurrentState with
            {
                ReceivedSamples = CurrentState.ReceivedSamples + 1,
                LastSampleAtUtc = sample.ObservedAtUtc
            };
            if (sample.UnitId != mapping.UnitId || sample.Table != mapping.Table || sample.Address != mapping.Address)
            {
                MarkDroppedSample();
                return;
            }

            var value = sample.Value * mapping.Scale + mapping.Offset;
            var bucketStartUtc = FloorToBucket(sample.ObservedAtUtc, mapping.BucketSeconds);
            var bucketKey = (mapping.UnitId, mapping.Table, mapping.Address, bucketStartUtc);
            var bucketEndUtc = bucketStartUtc.AddSeconds(mapping.BucketSeconds);
            if (bucketEndUtc < _utcNow() - _sealedBucketRetention || _sealedBucketKeys.ContainsKey(bucketKey))
            {
                MarkDroppedSample();
                return;
            }

            if (!_buckets.TryGetValue(bucketKey, out var bucket))
            {
                bucket = new ModbusTelemetryBucket(mapping, bucketStartUtc, bucketEndUtc);
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

    private async Task MarkBucketPostedAsync(ModbusTelemetryBucket bucket, CancellationToken cancellationToken)
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
            Summary = "Modbus TCP collector dropped one or more invalid or late samples."
        };
    }

    private RecordIndustrialTelemetrySampleRequest CreateRequest(ModbusTelemetryBucket bucket)
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
            $"modbus:{options.ConnectorId}:{normalizedTagKey}:{bucketStartUnixMilliseconds}",
            "modbus",
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
                    ? "Modbus TCP collector is polling and sampling."
                    : "Modbus TCP collector is polling with recoverable sampling issues."
            };
        }, cancellationToken);
    }

    private async Task UpdateStateAsync(Func<ModbusConnectorState, ModbusConnectorState> update, CancellationToken cancellationToken)
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

    private IReadOnlyDictionary<string, string> CreateMetadata(ModbusConnectorState state)
    {
        var metadata = new Dictionary<string, string>
        {
            ["endpoint"] = options.Endpoint,
            ["credentialReference"] = options.CredentialReference ?? string.Empty,
            ["registerCount"] = options.Registers.Count.ToString(),
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

    private static DateTimeOffset FloorToBucket(DateTimeOffset timestampUtc, int bucketSeconds)
    {
        if (bucketSeconds <= 0)
        {
            throw new InvalidOperationException("Modbus register bucketSeconds must be greater than zero.");
        }

        var unixSeconds = timestampUtc.ToUnixTimeSeconds();
        var bucketStartUnixSeconds = unixSeconds - unixSeconds % bucketSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(bucketStartUnixSeconds);
    }

    private static TimeSpan CalculateSealedBucketRetention(IReadOnlyList<ModbusRegisterMapping> mappings)
    {
        var maxBucketSeconds = mappings.Count == 0 ? 60 : mappings.Max(x => Math.Max(x.BucketSeconds, 1));
        return TimeSpan.FromSeconds(Math.Max(60, maxBucketSeconds * 5));
    }

    private sealed record BucketFlushItem(
        (byte UnitId, ModbusRegisterTable Table, ushort Address, DateTimeOffset BucketStartUtc) Key,
        ModbusTelemetryBucket Bucket);
}
