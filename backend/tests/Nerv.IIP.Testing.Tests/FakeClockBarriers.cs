using Microsoft.Extensions.Time.Testing;

namespace Nerv.IIP.Testing.Tests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that publishes an edge signal the moment a timer is registered
/// against it.
/// </summary>
/// <remarks>
/// <see cref="FakeTimeProvider.Advance"/> only fires timers that are <em>already</em> registered. Code
/// that registers its timer after the advance re-bases on the advanced now, nothing advances the clock
/// again, and the tick is lost permanently — the waiter never returns and the test host parks instead
/// of failing. Neither <c>await Task.Yield()</c> nor "the async method returned a pending task" is a
/// barrier against that. This provider turns the registration itself into an awaitable fact.
/// </remarks>
internal sealed class TimerRegistrationObservingTimeProvider : FakeTimeProvider
{
    private readonly TaskCompletionSource firstTimerCreated =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();
    private readonly List<(int ExpectedCount, TaskCompletionSource Reached)> countWaiters = [];
    private int timersCreated;

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
            () => $"fake now={GetUtcNow():O}, timers registered={TimersCreated}");

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
            () => $"fake now={GetUtcNow():O}, timers registered={TimersCreated}");
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

/// <summary>
/// Bounded await on an edge-triggered signal, reporting the redacted condition, elapsed time, attempt
/// count and last observation, so a lost fake-clock tick fails with a diagnosis instead of hanging.
/// </summary>
internal static class BoundedSignal
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    public static async Task ObserveAsync(
        Task observation,
        string condition,
        Func<string> lastObservation)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await observation.WaitAsync(Budget);
        }
        catch (TimeoutException)
        {
            throw new global::Xunit.Sdk.XunitException(
                $"Timed out waiting for {condition} after {elapsed.Elapsed.TotalSeconds:0.###}s "
                + $"(budget {Budget.TotalSeconds:0.###}s, attempts 1/1 — single bounded await on a "
                + $"completion signal); last observation: {lastObservation()}");
        }
    }
}
