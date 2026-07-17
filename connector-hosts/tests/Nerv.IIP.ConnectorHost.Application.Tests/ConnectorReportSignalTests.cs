using Nerv.IIP.ConnectorHost.Application;

namespace Nerv.IIP.ConnectorHost.Application.Tests;

public sealed class ConnectorReportSignalTests
{
    [Fact]
    public async Task Capacity_one_signal_coalesces_repeated_notifications()
    {
        var signal = new ConnectorReportSignal();
        var timeProvider = new ControllableTimeProvider();

        signal.Signal("connector-a");
        signal.Signal("connector-a");

        await signal.WaitAsync(TimeSpan.FromMinutes(1), timeProvider, CancellationToken.None);
        var secondWait = signal.WaitAsync(TimeSpan.FromMinutes(1), timeProvider, CancellationToken.None);

        Assert.False(secondWait.IsCompleted);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await secondWait;
    }

    [Fact]
    public async Task Wait_completes_when_timeout_elapses_without_a_signal()
    {
        var signal = new ConnectorReportSignal();
        var timeProvider = new ControllableTimeProvider();
        var wait = signal.WaitAsync(TimeSpan.FromSeconds(2), timeProvider, CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromSeconds(2));

        await wait;
    }

    private sealed class ControllableTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ControllableTimer> _timers = [];
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
            lock (_gate)
            {
                _timers.Add(timer);
            }

            return timer;
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

                _dueAtUtc = _period == Timeout.InfiniteTimeSpan ? null : utcNow + _period;
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
}
