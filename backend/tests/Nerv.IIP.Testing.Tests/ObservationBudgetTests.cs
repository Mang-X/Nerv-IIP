using System.Runtime.CompilerServices;

namespace Nerv.IIP.Testing.Tests;

/// <summary>
/// What happens when an <c>observe</c> implementation ignores the token it is handed.
/// </summary>
/// <remarks>
/// <para>
/// This is not a hypothetical. Two shapes produce it in this repository: a lambda written as
/// <c>observe: _ =&gt; …</c> that simply discards the parameter, and an observation whose client library has
/// no <see cref="CancellationToken"/> overloads at all (StackExchange.Redis). Before the bounded-window
/// driver put a structural budget around each observation, a single stuck observation held the window open
/// for as long as the observation took, with two distinct consequences:
/// </para>
/// <list type="number">
/// <item><description>
/// <see cref="Eventually.WaitAsync"/> never returned — a wedged Npgsql connection <em>parked</em> the test
/// run instead of failing it, which is the most expensive failure mode there is.
/// </description></item>
/// <item><description>
/// <see cref="Consistently.StaysAsync"/> silently degraded into a single observation: the window it was
/// supposed to hold open across had already elapsed by the time the first observation returned, so the
/// negative assertion it was defending was never actually re-checked.
/// </description></item>
/// </list>
/// <para>
/// Deleting the <c>WaitAsync</c> in <c>BoundedObservationWindow.ObserveAsync</c> makes every test in this
/// class hang rather than fail, which is exactly the symptom being prevented; each one therefore also carries
/// its own bounded outer budget so the regression surfaces as a red test.
/// </para>
/// </remarks>
public sealed class ObservationBudgetTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(22);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OuterBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StaysAsync_ReportsATimeoutWhenTheObservationIgnoresItsTokenPastTheWindowAndTheGrace()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        using var abandoned = new CancellationTokenSource();
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var wait = Consistently.StaysAsync(
            "no second maintenance work order is generated",
            // The token is deliberately discarded: this is the exact shape being defended against.
            _ =>
            {
                observationStarted.TrySetResult();
                return new ValueTask<int>(WaitForeverAsync(abandoned.Token));
            },
            observation => observation == 1,
            observation => $"workOrders={observation}",
            new EventuallyOptions(Window, PollInterval, []),
            timeProvider: clock).AsTask();

        await observationStarted.Task;
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(Window);
        // Timer #2 is the grace budget: the window closed on an in-flight observation and gave it a further
        // chance to be adjudicated. An observation that ignores that budget too is a timeout, never a pass.
        await clock.WaitForTimerCountAsync(2);
        clock.Advance(Window);

        var exception = await Assert.ThrowsAsync<ConsistentlyObservationTimeoutException>(
            async () => await wait.WaitAsync(OuterBudget));
        Assert.Equal("no second maintenance work order is generated", exception.Condition);
        Assert.Equal(0, exception.CompletedObservations);
        Assert.Equal(Window + Window, exception.Elapsed);

        await abandoned.CancelAsync();
    }

    [Fact]
    public async Task WaitAsync_ReportsATimeoutWhenTheObservationIgnoresTheBudgetToken()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        using var abandoned = new CancellationTokenSource();
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var wait = Eventually.WaitAsync(
            "the real scheduler generated the work order",
            _ =>
            {
                observationStarted.TrySetResult();
                return new ValueTask<int>(WaitForeverAsync(abandoned.Token));
            },
            observation => observation == 1,
            observation => $"workOrders={observation}",
            new EventuallyOptions(Window, PollInterval, []),
            timeProvider: clock).AsTask();

        await observationStarted.Task;
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(Window);

        var exception = await Assert.ThrowsAsync<EventuallyTimeoutException>(
            async () => await wait.WaitAsync(OuterBudget));
        Assert.Equal("the real scheduler generated the work order", exception.Condition);
        Assert.Equal(0, exception.Attempts);
        Assert.Equal("none", exception.LastObservation);

        await abandoned.CancelAsync();
    }

    /// <summary>
    /// Caller cancellation still wins over the window: abandoning a stuck observation must not rewrite a
    /// caller-owned cancellation into a helper-owned timeout.
    /// </summary>
    [Fact]
    public async Task CallerCancellationStillPropagatesThroughAnObservationThatIgnoresTheToken()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        using var abandoned = new CancellationTokenSource();
        using var callerSource = new CancellationTokenSource();
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var wait = Eventually.WaitAsync(
            "the real scheduler generated the work order",
            _ =>
            {
                observationStarted.TrySetResult();
                return new ValueTask<int>(WaitForeverAsync(abandoned.Token));
            },
            observation => observation == 1,
            observation => $"workOrders={observation}",
            new EventuallyOptions(Window, PollInterval, []),
            callerSource.Token,
            clock).AsTask();

        await observationStarted.Task;
        await callerSource.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await wait.WaitAsync(OuterBudget));
        Assert.Equal(callerSource.Token, exception.CancellationToken);

        await abandoned.CancelAsync();
    }

    /// <summary>
    /// The verdict of a positive assertion is decided the moment its budget expires. An observation that was
    /// given up on and then faults afterwards must not rewrite that verdict.
    /// </summary>
    /// <remarks>
    /// This case is about the verdict only. The separate question of <em>who consumes</em> the late fault is
    /// pinned by <see cref="TheLateFaultOfAnAbandonedObservationIsConsumedByThePrimitive"/> — and cannot be
    /// pinned here, because the <c>await</c> below is itself an observation of that fault, so this test passes
    /// with or without the primitive's own consumption.
    /// </remarks>
    [Fact]
    public async Task AnAbandonedObservationThatLaterFaultsDoesNotRewriteTheTimeout()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? abandonedObservation = null;

        var wait = Eventually.WaitAsync(
            "the real scheduler generated the work order",
            _ =>
            {
                observationStarted.TrySetResult();
                abandonedObservation = FaultAfterAsync(release.Task, "the abandoned observation faulted");
                return new ValueTask<int>(abandonedObservation);
            },
            observation => observation == 1,
            observation => $"workOrders={observation}",
            new EventuallyOptions(Window, PollInterval, []),
            timeProvider: clock).AsTask();

        await observationStarted.Task;
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(Window);
        await Assert.ThrowsAsync<EventuallyTimeoutException>(async () => await wait.WaitAsync(OuterBudget));

        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await abandonedObservation!);

        // The caller already received its timeout, and the late fault stays where it happened.
        Assert.True(wait.IsFaulted);
        Assert.IsType<EventuallyTimeoutException>(wait.Exception!.InnerException);
    }

    /// <summary>
    /// The primitive itself consumes the late fault of an abandoned observation, so it never resurfaces as an
    /// <see cref="TaskScheduler.UnobservedTaskException"/> against whichever unrelated test happens to be
    /// running at the next finalization pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing in this test may hold a reference to the abandoned task, because <em>awaiting it is itself an
    /// observation</em> and would make the assertion pass whether or not the primitive consumes anything. The
    /// observation task is therefore created and dropped inside a non-inlined helper, and the only handle kept
    /// is a <see cref="WeakReference{T}"/>, which does not keep it alive.
    /// </para>
    /// <para>
    /// The finalization pass is what turns an unobserved fault into the event, hence the explicit
    /// collect/finalize rounds. The marker string filters the process-wide event down to this test's own
    /// exception, so a leaked faulted task from a test running in parallel can neither fail this one nor be
    /// silently swallowed by it. Removing the <c>ConsumeLateFault</c> call in
    /// <c>BoundedObservationWindow</c> turns this test red.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheLateFaultOfAnAbandonedObservationIsConsumedByThePrimitive()
    {
        const string marker = "MAN-1470 late fault of an abandoned observation";
        var surfaced = new List<string>();

        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            if (!args.Exception.Flatten().InnerExceptions.Any(
                    inner => inner.Message.Contains(marker, StringComparison.Ordinal)))
            {
                return;
            }

            lock (surfaced)
            {
                surfaced.Add(args.Exception.Flatten().InnerExceptions[0].Message);
            }

            args.SetObserved();
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var observation = await AbandonAnObservationAsync(release, marker);

            release.SetResult();
            await WaitUntilFaultedAsync(observation);
            await ForceFinalizationAsync();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        lock (surfaced)
        {
            Assert.True(
                surfaced.Count == 0,
                "The abandoned observation's fault reached TaskScheduler.UnobservedTaskException, which means "
                + "the bounded-window driver no longer consumes it: "
                + string.Join(" | ", surfaced));
        }
    }

    /// <summary>
    /// Runs a window that abandons a still-pending observation and returns only a weak handle on it, so the
    /// task becomes collectable the moment it faults. Not inlined: an inlined frame would let the JIT keep the
    /// strong reference alive in the caller for the rest of the test, which is exactly what must not happen.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference<Task<int>>> AbandonAnObservationAsync(
        TaskCompletionSource release,
        string marker)
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WeakReference<Task<int>>? handle = null;

        var wait = Eventually.WaitAsync(
            "the real scheduler generated the work order",
            _ =>
            {
                observationStarted.TrySetResult();
                var pending = FaultAfterAsync(release.Task, marker);
                handle = new WeakReference<Task<int>>(pending);
                return new ValueTask<int>(pending);
            },
            observation => observation == 1,
            observation => $"workOrders={observation}",
            new EventuallyOptions(Window, PollInterval, []),
            timeProvider: clock).AsTask();

        await observationStarted.Task;
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(Window);
        await Assert.ThrowsAsync<EventuallyTimeoutException>(async () => await wait.WaitAsync(OuterBudget));

        return handle!;
    }

    /// <summary>
    /// Bounded wait for the abandoned observation to actually reach its faulted state, without ever letting a
    /// strong reference escape this frame. "Already collected" counts as reached: it can only have been
    /// collected after faulting.
    /// </summary>
    /// <remarks>
    /// The budget is a yield count rather than a <c>Task.Delay</c> on purpose. What is being waited for is a
    /// thread-pool continuation that was already queued by <c>release.SetResult()</c>, so yielding <em>is</em>
    /// the edge — a wall-clock interval would only be guessing at it, and a guessed interval in this library
    /// is exactly what the determinism contract forbids.
    /// </remarks>
    private static async Task WaitUntilFaultedAsync(WeakReference<Task<int>> handle)
    {
        const int maxYields = 10_000;
        for (var yields = 0; yields < maxYields && !IsCompletedOrCollected(handle); yields++)
        {
            await Task.Yield();
        }

        Assert.True(
            IsCompletedOrCollected(handle),
            $"The abandoned observation had not reached its faulted state after {maxYields} yields.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsCompletedOrCollected(WeakReference<Task<int>> handle) =>
        !handle.TryGetTarget(out var pending) || pending.IsCompleted;

    private static async Task ForceFinalizationAsync()
    {
        // Two rounds: the first finalization pass queues the TaskExceptionHolder's report, the second makes
        // sure the object graph it belonged to is gone before the assertion reads the event log.
        for (var round = 0; round < 3; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Yield();
        }
    }

    private static async Task<int> WaitForeverAsync(CancellationToken abandonToken)
    {
        await PendingOperation.UntilCanceledAsync(abandonToken).ConfigureAwait(false);
        return 1;
    }

    private static async Task<int> FaultAfterAsync(Task release, string marker)
    {
        await release.ConfigureAwait(false);
        throw new InvalidOperationException(marker);
    }
}
