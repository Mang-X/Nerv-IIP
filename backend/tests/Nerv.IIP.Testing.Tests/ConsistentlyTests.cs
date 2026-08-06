namespace Nerv.IIP.Testing.Tests;

public sealed class ConsistentlyTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(22);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StaysAsync_ReturnsTheLastObservationWhenTheWindowElapsesWithoutAViolation()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var attempts = 0;
        var wait = Consistently.StaysAsync(
            "the deduplicated scope dispatches exactly one command",
            _ => ValueTask.FromResult(++attempts),
            _ => true,
            observation => $"commands={observation}",
            Options(),
            timeProvider: clock).AsTask();

        var last = await DriveAsync(clock, wait);

        // Window 22s / poll 5s: observations land at 0s, 5s, 10s, 15s, 20s and the window then closes.
        Assert.Equal(5, last);
        Assert.Equal(5, attempts);
    }

    [Fact]
    public async Task StaysAsync_FailsOnTheFirstViolatingObservationInsteadOfAtTheEndOfTheWindow()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var observations = new Queue<int>([1, 1, 2, 1, 1, 1]);
        var wait = Consistently.StaysAsync(
            "commands stay at 1",
            _ => ValueTask.FromResult(observations.Dequeue()),
            observation => observation == 1,
            observation => $"commands={observation}",
            Options(),
            timeProvider: clock).AsTask();

        var exception = await Assert.ThrowsAsync<ConsistentlyViolatedException>(
            async () => await DriveAsync(clock, wait));

        Assert.Equal("commands stay at 1", exception.Condition);
        Assert.Equal(3, exception.Attempts);
        Assert.Equal(TimeSpan.FromSeconds(10), exception.Elapsed);
        Assert.Equal("commands=2", exception.ViolatingObservation);

        // The window was 22s wide; the failure landed on the third observation, not at its end.
        Assert.Equal(3, observations.Count);
    }

    [Fact]
    public async Task StaysAsync_SanitizesTheConditionAndTheViolatingObservation()
    {
        const string secret = "super-sensitive-value";
        var clock = new TimerRegistrationObservingTimeProvider();

        var wait = Consistently.StaysAsync(
            $"connection={secret}; state=commands stay at 1",
            _ => ValueTask.FromResult(2),
            observation => observation == 1,
            observation => $"commands={observation}; token={secret}",
            new EventuallyOptions(Window, PollInterval, [secret]),
            timeProvider: clock).AsTask();

        var exception = await Assert.ThrowsAsync<ConsistentlyViolatedException>(() => wait);
        Assert.Equal("connection=[REDACTED]; state=commands stay at 1", exception.Condition);
        Assert.Equal("commands=2; token=[REDACTED]", exception.ViolatingObservation);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaysAsync_PropagatesCallerCancellation()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        using var callerSource = new CancellationTokenSource();
        var wait = Consistently.StaysAsync(
            "commands stay at 1",
            async token =>
            {
                await PendingOperation.UntilCanceledAsync(token).ConfigureAwait(false);
                return 1;
            },
            observation => observation == 1,
            observation => $"commands={observation}",
            Options(),
            callerSource.Token,
            clock).AsTask();

        await callerSource.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(callerSource.Token, exception.CancellationToken);
    }

    /// <summary>
    /// The observation that is in flight when the window closes is the single observation most likely to
    /// expose a late violation, so it is adjudicated rather than discarded in favour of an earlier, cleaner
    /// one. This is the "flips at T-ε" case: two clean observations are already banked and the third — started
    /// inside the window, finished after it closed — violates. Returning the banked observation instead would
    /// report a pass and let the violation through, which is a false green, not a conservative one.
    /// </summary>
    [Fact]
    public async Task StaysAsync_AdjudicatesAnObservationThatStartedInsideTheWindowAndFinishedAfterItClosed()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdObservationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        var wait = Consistently.StaysAsync(
            "commands stay at 1",
            _ =>
            {
                if (++started < 3)
                {
                    return ValueTask.FromResult(1);
                }

                thirdObservationStarted.TrySetResult();
                return new ValueTask<int>(release.Task);
            },
            observation => observation == 1,
            observation => $"commands={observation}",
            Options(),
            timeProvider: clock).AsTask();

        // Two clean observations at 0s and 5s (timer #1 is the window itself, #2 and #3 the poll delays).
        await clock.WaitForTimerCountAsync(2);
        clock.Advance(PollInterval);
        await clock.WaitForTimerCountAsync(3);
        clock.Advance(PollInterval);

        // The third observation is in flight at 10s; close the 22s window underneath it. Timer #4 is the
        // grace budget, whose registration is the observable proof that the window closed on an in-flight
        // observation instead of on a poll delay.
        await thirdObservationStarted.Task;
        clock.Advance(Window);
        await AwaitGraceRegistrationAsync(clock, wait, release);

        release.SetResult(2);

        var exception = await Assert.ThrowsAsync<ConsistentlyViolatedException>(() => wait);
        Assert.Equal(3, exception.Attempts);
        Assert.Equal("commands=2", exception.ViolatingObservation);
        Assert.Equal(TimeSpan.FromSeconds(32), exception.Elapsed);
        Assert.Equal(3, started);
    }

    /// <summary>
    /// The other half of the same rule: when the in-flight observation does not finish within the grace
    /// budget either, the verdict is unknown. Unknown is reported as a timeout — never as a pass, and never as
    /// a violation. The observation's own token is cancelled on the way out so it stops burning the runner.
    /// </summary>
    [Fact]
    public async Task StaysAsync_ReportsATimeoutWhenTheInFlightObservationOverrunsTheGraceBudgetToo()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var thirdObservationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? overrunning = null;
        var started = 0;

        var wait = Consistently.StaysAsync(
            "commands stay at 1",
            token =>
            {
                if (++started < 3)
                {
                    return ValueTask.FromResult(1);
                }

                thirdObservationStarted.TrySetResult();
                overrunning = HoldUntilCanceledAsync(token);
                return new ValueTask<int>(overrunning);
            },
            observation => observation == 1,
            observation => $"commands={observation}",
            Options(),
            timeProvider: clock).AsTask();

        await clock.WaitForTimerCountAsync(2);
        clock.Advance(PollInterval);
        await clock.WaitForTimerCountAsync(3);
        clock.Advance(PollInterval);
        await thirdObservationStarted.Task;

        clock.Advance(Window);
        await AwaitGraceRegistrationAsync(clock, wait, release: null);
        clock.Advance(Window);

        var exception = await Assert.ThrowsAsync<ConsistentlyObservationTimeoutException>(() => wait);
        Assert.Equal("commands stay at 1", exception.Condition);
        Assert.Equal(2, exception.CompletedObservations);
        Assert.Equal(Window, exception.Grace);
        Assert.Equal(TimeSpan.FromSeconds(54), exception.Elapsed);
        Assert.Contains("verdict is unknown, not a pass", exception.Message, StringComparison.Ordinal);
        Assert.IsNotType<ConsistentlyViolatedException>(exception);

        // Grace expiry cancels the observation's own token rather than leaving it running.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await overrunning!);
    }

    /// <summary>
    /// A window that closes while the very first observation is still in flight, and whose grace budget then
    /// expires too, learned nothing about the invariant, so it must not be reported as a violation. The
    /// canonical real case is a negative assertion whose observation is a query against a cold Docker
    /// PostgreSQL on a loaded runner: calling that "the invariant was violated" turns slow infrastructure into
    /// a business defect, and the diagnostic would have to name an observation that never existed.
    /// </summary>
    [Fact]
    public async Task StaysAsync_ReportsATimeoutRatherThanAViolationWhenNoObservationEverCompleted()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = Consistently.StaysAsync(
            "no second maintenance work order is generated",
            async token =>
            {
                observationStarted.TrySetResult();
                await PendingOperation.UntilCanceledAsync(token).ConfigureAwait(false);
                return 1;
            },
            observation => observation == 1,
            observation => $"workOrders={observation}",
            Options(),
            timeProvider: clock).AsTask();

        // Timer #1 is the window's own CancellationTokenSource, #2 the grace budget the still-parked first
        // observation is given once the window closes. Advancing past both is exactly the race being pinned.
        await observationStarted.Task;
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(Window);
        await clock.WaitForTimerCountAsync(2);
        clock.Advance(Window);

        var exception = await Assert.ThrowsAsync<ConsistentlyObservationTimeoutException>(() => wait);
        Assert.Equal("no second maintenance work order is generated", exception.Condition);
        Assert.Equal(0, exception.CompletedObservations);
        Assert.Equal(Window + Window, exception.Elapsed);
        Assert.Contains("never observed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not a violation", exception.Message, StringComparison.Ordinal);
        Assert.IsNotType<ConsistentlyViolatedException>(exception);
    }

    [Fact]
    public async Task StaysAsync_SanitizesTheConditionOnTheNoObservationTimeout()
    {
        const string secret = "Host=db;Password=super-sensitive-value";
        var clock = new TimerRegistrationObservingTimeProvider();
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = Consistently.StaysAsync(
            $"connection={secret}; state=no second work order",
            async token =>
            {
                observationStarted.TrySetResult();
                await PendingOperation.UntilCanceledAsync(token).ConfigureAwait(false);
                return 1;
            },
            observation => observation == 1,
            observation => $"workOrders={observation}",
            new EventuallyOptions(Window, PollInterval, [secret]),
            timeProvider: clock).AsTask();

        await observationStarted.Task;
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(Window);
        await clock.WaitForTimerCountAsync(2);
        clock.Advance(Window);

        var exception = await Assert.ThrowsAsync<ConsistentlyObservationTimeoutException>(() => wait);
        Assert.Equal("connection=[REDACTED]; state=no second work order", exception.Condition);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The grace budget is a parameter, not a constant: a caller whose observation is much cheaper than its
    /// window can shorten it.
    /// </summary>
    [Fact]
    public async Task StaysAsync_UsesTheCallerSuppliedGraceBudget()
    {
        var grace = TimeSpan.FromSeconds(3);
        var clock = new TimerRegistrationObservingTimeProvider();
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = Consistently.StaysAsync(
            "no second maintenance work order is generated",
            async token =>
            {
                observationStarted.TrySetResult();
                await PendingOperation.UntilCanceledAsync(token).ConfigureAwait(false);
                return 1;
            },
            observation => observation == 1,
            observation => $"workOrders={observation}",
            Options(),
            timeProvider: clock,
            observationGrace: grace).AsTask();

        await observationStarted.Task;
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(Window);
        await clock.WaitForTimerCountAsync(2);
        clock.Advance(grace);

        var exception = await Assert.ThrowsAsync<ConsistentlyObservationTimeoutException>(() => wait);
        Assert.Equal(grace, exception.Grace);
        Assert.Equal(Window + grace, exception.Elapsed);
    }

    /// <summary>
    /// Waits for the grace budget's own timer (#4 after the window and two poll delays), which is the
    /// observable proof that the window closed on an in-flight observation and is going to adjudicate it.
    /// If the window instead ends right there, that <em>is</em> the regression this file exists to catch, so
    /// it is reported as a verdict failure rather than as a barrier timeout five seconds later.
    /// </summary>
    private static async Task AwaitGraceRegistrationAsync(
        TimerRegistrationObservingTimeProvider clock,
        Task<int> wait,
        TaskCompletionSource<int>? release)
    {
        var graceRegistered = clock.WaitForTimerCountAsync(4);
        if (await Task.WhenAny(wait, graceRegistered).ConfigureAwait(false) != wait)
        {
            await graceRegistered.ConfigureAwait(false);
            return;
        }

        release?.TrySetResult(0);
        Assert.Fail(
            "The stability window ended without adjudicating the observation that was in flight when it "
            + $"closed. Verdict: {Describe(wait)}");
    }

    private static string Describe(Task<int> wait) => wait.Status switch
    {
        TaskStatus.RanToCompletion => $"returned commands={wait.Result} (a pass)",
        TaskStatus.Faulted => $"threw {wait.Exception!.InnerException?.GetType().Name}",
        _ => wait.Status.ToString(),
    };

    private static async Task<int> HoldUntilCanceledAsync(CancellationToken cancellationToken)
    {
        await PendingOperation.UntilCanceledAsync(cancellationToken).ConfigureAwait(false);
        return 1;
    }

    /// <summary>
    /// Advances the fake clock one poll interval at a time, always waiting for the *next* timer
    /// registration first. The window's own <see cref="CancellationTokenSource"/> registers timer #1 and
    /// each poll delay registers the next one, so timer #(round + 1) is the edge that makes advancing
    /// round <c>round</c> safe.
    /// </summary>
    private static async Task<int> DriveAsync(TimerRegistrationObservingTimeProvider clock, Task<int> pending)
    {
        const int maxRounds = 10;
        for (var round = 1; !pending.IsCompleted; round++)
        {
            Assert.True(round <= maxRounds, $"The stability window did not close within {maxRounds} poll rounds.");
            var barrier = clock.WaitForTimerCountAsync(round + 1);
            if (await Task.WhenAny(pending, barrier).ConfigureAwait(false) == pending)
            {
                break;
            }

            await barrier.ConfigureAwait(false);
            clock.Advance(PollInterval);
        }

        return await pending.ConfigureAwait(false);
    }

    private static EventuallyOptions Options() => new(Window, PollInterval, []);
}
