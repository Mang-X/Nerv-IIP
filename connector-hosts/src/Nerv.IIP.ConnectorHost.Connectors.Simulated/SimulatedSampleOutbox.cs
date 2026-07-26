using Nerv.IIP.ConnectorHost.Connectors.Abstractions;

namespace Nerv.IIP.ConnectorHost.Connectors.Simulated;

public sealed class SimulatedSampleOutbox
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly int _maxDeliveryAttempts;
    private readonly TimeSpan _retryBase;
    private readonly LinkedList<PendingSample> _pending = [];
    private readonly Dictionary<string, LinkedListNode<PendingSample>> _bySourceSequence =
        new(StringComparer.Ordinal);

    public SimulatedSampleOutbox(
        int capacity,
        int maxDeliveryAttempts,
        TimeSpan retryBase)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (maxDeliveryAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDeliveryAttempts));
        }

        if (retryBase <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryBase));
        }

        _capacity = capacity;
        _maxDeliveryAttempts = maxDeliveryAttempts;
        _retryBase = retryBase;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public bool Enqueue(
        RecordIndustrialTelemetrySampleRequest request,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_bySourceSequence.ContainsKey(request.SourceSequence))
            {
                return false;
            }

            var evicted = false;
            if (_pending.Count == _capacity)
            {
                var oldest = _pending.First!;
                _pending.RemoveFirst();
                _bySourceSequence.Remove(oldest.Value.Request.SourceSequence);
                evicted = true;
            }

            var node = _pending.AddLast(new PendingSample(request, 0, nowUtc));
            _bySourceSequence.Add(request.SourceSequence, node);
            return evicted;
        }
    }

    public IReadOnlyList<RecordIndustrialTelemetrySampleRequest> GetDue(
        DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            return _pending
                .Where(item => item.NextAttemptAtUtc <= nowUtc)
                .Select(item => item.Request)
                .ToArray();
        }
    }

    public void MarkDelivered(string sourceSequence)
    {
        lock (_gate)
        {
            if (_bySourceSequence.Remove(sourceSequence, out var node))
            {
                _pending.Remove(node);
            }
        }
    }

    public bool MarkFailed(string sourceSequence, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_bySourceSequence.TryGetValue(sourceSequence, out var node))
            {
                return false;
            }

            var attemptCount = node.Value.AttemptCount + 1;
            if (attemptCount >= _maxDeliveryAttempts)
            {
                _pending.Remove(node);
                _bySourceSequence.Remove(sourceSequence);
                return true;
            }

            var exponent = Math.Min(attemptCount - 1, 30);
            var delayTicks = checked(_retryBase.Ticks * (1L << exponent));
            node.Value = node.Value with
            {
                AttemptCount = attemptCount,
                NextAttemptAtUtc = nowUtc.AddTicks(delayTicks)
            };
            return false;
        }
    }

    private sealed record PendingSample(
        RecordIndustrialTelemetrySampleRequest Request,
        int AttemptCount,
        DateTimeOffset NextAttemptAtUtc);
}
