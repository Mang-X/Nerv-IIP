namespace Nerv.IIP.Testing.Tests;

/// <summary>
/// What happens when an <c>observe</c> implementation ignores the token it is handed.
/// </summary>
/// <remarks>
/// <para>
/// This is not a hypothetical. Two shapes produce it in this repository: a lambda written as
/// <c>observe: _ =&gt; …</c> that simply discards the parameter, and an observation whose client library has
/// no <see cref="CancellationToken"/> overloads at all (StackExchange.Redis). Before
/// <c>Eventually.ObserveWithinWindowAsync</c> existed, a single stuck observation held the window open for
/// as long as the observation took, with two distinct consequences:
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
/// Deleting the <c>WaitAsync</c> in <c>ObserveWithinWindowAsync</c> makes every test in this class hang
/// rather than fail, which is exactly the symptom being prevented; each one therefore also carries its own
/// bounded outer budget so the regression surfaces as a red test.
/// </para>
/// </remarks>
public sealed class ObservationBudgetTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(22);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OuterBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StaysAsync_ReportsATimeoutWhenTheObservationIgnoresTheWindowToken()
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

        var exception = await Assert.ThrowsAsync<ConsistentlyObservationTimeoutException>(
            async () => await wait.WaitAsync(OuterBudget));
        Assert.Equal("no second maintenance work order is generated", exception.Condition);
        Assert.Equal(Window, exception.Elapsed);

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
    /// The verdict is decided the moment the window closes. An observation that was given up on and then
    /// faults afterwards must not rewrite that verdict, and its fault must be consumed by the primitive
    /// (the <c>ContinueWith</c> in <c>ObserveWithinWindowAsync</c>) rather than left dangling for the
    /// finalizer to report as an <c>UnobservedTaskException</c> against some unrelated test.
    /// </summary>
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
                abandonedObservation = FaultAfterAsync(release.Task);
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

    private static async Task<int> WaitForeverAsync(CancellationToken abandonToken)
    {
        await PendingOperation.UntilCanceledAsync(abandonToken).ConfigureAwait(false);
        return 1;
    }

    private static async Task<int> FaultAfterAsync(Task release)
    {
        await release.ConfigureAwait(false);
        throw new InvalidOperationException("the abandoned observation faulted after it was given up on");
    }
}
