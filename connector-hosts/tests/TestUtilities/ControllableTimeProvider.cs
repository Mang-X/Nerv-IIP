namespace Nerv.IIP.ConnectorHost.TestUtilities;

public sealed class ControllableTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ControllableTimer> _timers = [];
    private readonly HashSet<(TimeSpan DueTime, TimeSpan Period)> _createdTimers = [];
    private readonly Dictionary<(TimeSpan DueTime, TimeSpan Period), TaskCompletionSource> _timerCreationWaiters = [];
    private DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-07-17T00:00:00Z");

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ControllableTimer(this, callback, state, dueTime, period);
        TaskCompletionSource? creationWaiter = null;
        lock (_gate)
        {
            _timers.Add(timer);
            var registration = (dueTime, period);
            _createdTimers.Add(registration);
            _timerCreationWaiters.Remove(registration, out creationWaiter);
        }

        creationWaiter?.TrySetResult();
        return timer;
    }

    public Task WaitForTimerCreatedAsync(TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            var registration = (dueTime, period);
            if (_createdTimers.Contains(registration))
            {
                return Task.CompletedTask;
            }

            if (!_timerCreationWaiters.TryGetValue(registration, out var waiter))
            {
                waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _timerCreationWaiters.Add(registration, waiter);
            }

            return waiter.Task;
        }
    }

    public void Advance(TimeSpan amount)
    {
        ControllableTimer[] due;
        lock (_gate)
        {
            _utcNow += amount;
            due = _timers.Where(timer => timer.IsDue(_utcNow)).ToArray();
        }

        foreach (var timer in due)
        {
            timer.Fire(GetUtcNow());
        }
    }

    private sealed class ControllableTimer(
        ControllableTimeProvider owner,
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period) : ITimer
    {
        private DateTimeOffset? _dueAtUtc = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
        private TimeSpan _period = period;
        private bool _disposed;

        public bool IsDue(DateTimeOffset utcNow) => !_disposed && _dueAtUtc <= utcNow;

        public void Fire(DateTimeOffset utcNow)
        {
            if (_disposed || _dueAtUtc > utcNow)
            {
                return;
            }

            if (_period == Timeout.InfiniteTimeSpan)
            {
                _dueAtUtc = null;
            }
            else
            {
                var nextDueAtUtc = _dueAtUtc!.Value + _period;
                while (nextDueAtUtc <= utcNow)
                {
                    nextDueAtUtc += _period;
                }

                _dueAtUtc = nextDueAtUtc;
            }

            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
            {
                return false;
            }

            _period = period;
            _dueAtUtc = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
            return true;
        }

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
