using Microsoft.Extensions.Time.Testing;

namespace Nerv.IIP.Testing;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that publishes an edge signal the moment a timer is registered
/// against it.
/// </summary>
/// <remarks>
/// <see cref="FakeTimeProvider.Advance"/> only fires timers that are <em>already</em> registered. Code
/// that registers its timer after the advance re-bases on the advanced now, nothing advances the clock
/// again, and the tick is lost permanently — the waiter never returns and the test host parks instead
/// of failing. Neither <c>await Task.Yield()</c> nor "the async method returned a pending task" is a
/// barrier against that, and neither is "the statement that creates the timer happens to come first in
/// the production method" — that is a property of today's source order, not an observable fact. This
/// provider turns the registration itself into an awaitable fact.
/// </remarks>
public sealed class TimerRegistrationObservingTimeProvider : FakeTimeProvider
{
    private readonly TaskCompletionSource firstTimerCreated =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();
    private readonly List<(int ExpectedCount, TaskCompletionSource Reached)> countWaiters = [];
    private readonly TimeSpan registrationBudget;
    private int timersCreated;

    /// <param name="registrationBudget">
    /// Wall-clock budget for the <c>WaitFor…</c> barriers below; defaults to
    /// <see cref="BoundedSignal.DefaultBudget"/>. A healthy run never spends it.
    /// </param>
    public TimerRegistrationObservingTimeProvider(TimeSpan? registrationBudget = null)
    {
        this.registrationBudget = ResolveBudget(registrationBudget);
    }

    /// <param name="startDateTime">
    /// Where this clock starts. <see cref="FakeTimeProvider"/> otherwise starts in the year 2000, which
    /// breaks any subject whose <em>other</em> guard still reads the process wall clock (a domain rule such
    /// as "the deadline must be in the future" is checked against real <c>UtcNow</c>, and injecting a
    /// <see cref="TimeProvider"/> only covers the side that reads the injected clock). Such a test anchors
    /// at real now and advances from there.
    /// </param>
    /// <param name="registrationBudget">
    /// Wall-clock budget for the <c>WaitFor…</c> barriers below; defaults to
    /// <see cref="BoundedSignal.DefaultBudget"/>. A healthy run never spends it.
    /// </param>
    /// <remarks>
    /// The anchor is a constructor parameter rather than a <see cref="FakeTimeProvider.SetUtcNow"/> call
    /// made after construction on purpose: setting the clock forward fires every timer already registered
    /// against it, so a two-step "construct, then anchor" is only safe while nothing has registered yet —
    /// an ordering property, which is exactly what this type exists to stop tests from relying on.
    /// </remarks>
    public TimerRegistrationObservingTimeProvider(
        DateTimeOffset startDateTime,
        TimeSpan? registrationBudget = null)
        : base(startDateTime)
    {
        this.registrationBudget = ResolveBudget(registrationBudget);
    }

    private static TimeSpan ResolveBudget(TimeSpan? registrationBudget)
    {
        var resolved = registrationBudget ?? BoundedSignal.DefaultBudget;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(resolved, TimeSpan.Zero);
        return resolved;
    }

    /// <summary>Completes once at least one timer has been registered against this clock.</summary>
    public Task FirstTimerCreated => firstTimerCreated.Task;

    public int TimersCreated => Volatile.Read(ref timersCreated);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        var created = Interlocked.Increment(ref timersCreated);
        firstTimerCreated.TrySetResult();
        ReleaseCountWaiters(created);
        return timer;
    }

    /// <summary>
    /// Bounded wait for this clock's first timer registration, to be awaited immediately before
    /// <see cref="FakeTimeProvider.Advance"/>.
    /// </summary>
    public Task WaitForFirstTimerAsync() =>
        BoundedSignal.ObserveAsync(
            FirstTimerCreated,
            "the operation under test to register its timer on the fake clock",
            () => $"fake now={GetUtcNow():O}, timers registered={TimersCreated}",
            registrationBudget);

    /// <summary>
    /// Bounded wait until <paramref name="expectedCount"/> timers have been registered against this clock.
    /// A polling loop re-registers a timer per iteration, so "the previous tick was delivered" is not the
    /// same fact as "the next timer exists"; only the latter makes the next
    /// <see cref="FakeTimeProvider.Advance"/> safe.
    /// </summary>
    public Task WaitForTimerCountAsync(int expectedCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedCount, 1);

        TaskCompletionSource reached;
        lock (gate)
        {
            if (Volatile.Read(ref timersCreated) >= expectedCount)
            {
                return Task.CompletedTask;
            }

            reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            countWaiters.Add((expectedCount, reached));
        }

        return BoundedSignal.ObserveAsync(
            reached.Task,
            $"the operation under test to register timer #{expectedCount} on the fake clock",
            () => $"fake now={GetUtcNow():O}, timers registered={TimersCreated}",
            registrationBudget);
    }

    private void ReleaseCountWaiters(int created)
    {
        List<TaskCompletionSource>? released = null;
        lock (gate)
        {
            for (var index = countWaiters.Count - 1; index >= 0; index--)
            {
                if (countWaiters[index].ExpectedCount > created)
                {
                    continue;
                }

                (released ??= []).Add(countWaiters[index].Reached);
                countWaiters.RemoveAt(index);
            }
        }

        foreach (var waiter in released ?? [])
        {
            waiter.TrySetResult();
        }
    }
}
