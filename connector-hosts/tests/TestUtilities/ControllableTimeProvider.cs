namespace Nerv.IIP.ConnectorHost.TestUtilities;

public sealed class ControllableTimeProvider : TimeProvider
{
    private readonly object _gate = new();

    // Neither collection is pruned. `_createdTimers` must not be: it is the "has this signature
    // ever been registered" set that `WaitForTimerEverCreatedAsync` reports, and forgetting an
    // entry would turn an already-satisfied barrier back into a wait that nothing completes.
    // `_timers` keeps disposed timers too, which only costs an `IsDue` call per `Advance`; a
    // provider is created per test and dies with it, so the list is bounded by one test's timer
    // count (tens) rather than by run length.
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
        // Constructed before `_gate` is taken: the field initializer reads `GetUtcNow()`, which
        // would otherwise be a re-entrant acquisition of the provider gate.
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

    /// <summary>
    /// Completes once a timer with this exact <paramref name="dueTime"/>/<paramref name="period"/>
    /// signature has <em>ever</em> been created on this provider — already-completed if one exists
    /// already. <see cref="_createdTimers"/> is never pruned, so this deliberately cannot express
    /// "wait for the next creation": a second registration with the same signature is satisfied
    /// immediately by the first. Callers use it as a one-shot barrier before the first
    /// <see cref="Advance"/> for a given loop, which is the only thing it can honestly certify.
    /// </summary>
    public Task WaitForTimerEverCreatedAsync(TimeSpan dueTime, TimeSpan period)
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
        // `Advance` fires callbacks outside the provider lock, and a callback may itself advance
        // the clock (a monitor that moves time forward while it runs). Two `Advance` calls can
        // therefore reach the same timer concurrently, so the due-time transition has to be
        // atomic — otherwise a torn read of `_dueAtUtc` can double-fire or skip a tick.
        //
        // Lock order is `_gate` -> `_timerGate`, never the reverse: `Advance` calls `IsDue` while
        // holding the provider gate, so nothing may reach back into the provider (`GetUtcNow`,
        // `CreateTimer`, ...) while holding `_timerGate`. `IsDue`/`Fire` take the observed "now"
        // as a parameter, `Change` acquires the two gates in that same order, and `Dispose` needs
        // no clock at all — so the cycle does not exist.
        private readonly object _timerGate = new();
        private DateTimeOffset? _dueAtUtc = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
        private TimeSpan _period = period;
        private bool _disposed;

        public bool IsDue(DateTimeOffset utcNow)
        {
            lock (_timerGate)
            {
                return !_disposed && _dueAtUtc is { } dueAtUtc && dueAtUtc <= utcNow;
            }
        }

        public void Fire(DateTimeOffset utcNow)
        {
            lock (_timerGate)
            {
                if (_disposed || _dueAtUtc is not { } dueAtUtc || dueAtUtc > utcNow)
                {
                    return;
                }

                if (_period == Timeout.InfiniteTimeSpan)
                {
                    _dueAtUtc = null;
                }
                else
                {
                    var nextDueAtUtc = dueAtUtc + _period;
                    while (nextDueAtUtc <= utcNow)
                    {
                        nextDueAtUtc += _period;
                    }

                    _dueAtUtc = nextDueAtUtc;
                }
            }

            // Invoked outside the gate: the callback may re-enter the provider (and this timer)
            // from the resumed loop, and holding the gate across it would serialise unrelated
            // timers behind arbitrary user code.
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            // Both gates, in the canonical `_gate` -> `_timerGate` order that `Advance` also uses,
            // so reading "now" and re-arming against it is one non-interleavable window. Sampling
            // the clock outside `_gate` would avoid the deadlock but open a stale-clock window
            // instead: a concurrent `Advance` between the sample and the write re-arms the timer
            // against an already-superseded "now", which is the same lost-tick shape as MAN-799.
            lock (owner._gate)
            {
                lock (_timerGate)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    _period = period;

                    // Read directly under `owner._gate` rather than through `GetUtcNow()`; the
                    // value is the same, this just makes the "already holding the gate" fact local.
                    _dueAtUtc = dueTime == Timeout.InfiniteTimeSpan ? null : owner._utcNow + dueTime;
                    return true;
                }
            }
        }

        public void Dispose()
        {
            lock (_timerGate)
            {
                _disposed = true;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
