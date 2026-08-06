namespace Nerv.IIP.Testing;

/// <summary>
/// The single bounded-window driver behind <see cref="Eventually.WaitAsync"/> and
/// <see cref="Consistently.StaysAsync"/>: argument validation, the window clock and its linked
/// <see cref="CancellationTokenSource"/>s, the observe/adjudicate/poll loop, and the two cancellation
/// filters that tell caller cancellation apart from the window closing.
/// </summary>
/// <remarks>
/// <para>
/// The two callers were line-for-line copies of each other before this type existed, and they had already
/// started to drift (attempt counting and the moment of sanitization diverged). What they must <em>not</em>
/// share is their verdict semantics, so those stay outside as callbacks:
/// </para>
/// <list type="bullet">
/// <item><description>
/// A positive assertion (<see cref="Eventually"/>) ends the window on the first satisfying observation and
/// reports a timeout otherwise. An observation still running when the window closes is evidence of nothing —
/// "satisfied, but late" is exactly what the budget exists to reject — so it is abandoned.
/// </description></item>
/// <item><description>
/// A negative assertion (<see cref="Consistently"/>) ends the window on the first <em>violating</em>
/// observation. An observation that started inside the window is evidence, whenever it happens to finish, so
/// it is adjudicated under a separate <see cref="ObservationGrace{TObservation}"/> budget instead of being
/// dropped in favour of an earlier, cleaner one. Dropping it would silently downgrade the sensitivity of the
/// negative assertion: the one observation that would have exposed the violation is the one thrown away.
/// </description></item>
/// </list>
/// <para>
/// <strong>Resource invariant for every <c>observe</c> delegate.</strong> An observation may still be running
/// after the window that started it has closed — abandoned outright (positive assertions) or awaited under the
/// grace budget and then abandoned when that expires too. It therefore has to own every resource it touches:
/// its own connection, its own DI scope, its own <c>DbContext</c>. Closing over a connection or a
/// <c>DbContext</c> that the caller keeps using after the window returns is a use-after-window bug, and for
/// EF Core it surfaces as "A second operation was started on this context instance" from an arbitrary later
/// line. <see cref="Eventually.IsAssertionShaped"/> rethrows that shape immediately rather than retrying it,
/// which turns the symptom loud, but the invariant is what prevents it.
/// </para>
/// </remarks>
internal static class BoundedObservationWindow
{
    /// <summary>
    /// Decides what a completed observation means. Returns <see langword="true"/> to end the window and
    /// return that observation; throws to end the window with a failure.
    /// </summary>
    internal delegate bool Adjudicate<in TObservation>(TObservation observation, int attempts, TimeSpan elapsed);

    /// <summary>
    /// Produces the verdict for a window that closed with nothing left to adjudicate. Returns the result of
    /// the whole call, or throws.
    /// </summary>
    internal delegate TObservation WindowClosed<TObservation>(
        int attempts,
        TimeSpan elapsed,
        TObservation lastObservation);

    /// <summary>
    /// The policy for an observation that was still in flight when the window closed: how long it is given to
    /// finish so that it can still be adjudicated, and what failure to raise when it does not.
    /// </summary>
    /// <param name="Budget">Independent of the window budget, and measured from the moment the window closed.</param>
    /// <param name="OnExpired">
    /// Called with the number of observations that <em>did</em> complete and the total elapsed time. Always
    /// throws: an unfinished observation means the verdict is unknown, which is never a pass.
    /// </param>
    internal sealed record ObservationGrace<TObservation>(
        TimeSpan Budget,
        Func<int, TimeSpan, TObservation> OnExpired);

    internal static void ValidateArguments<TObservation>(
        string condition,
        Func<CancellationToken, ValueTask<TObservation>> observe,
        Predicate<TObservation> isSatisfied,
        Func<TObservation, string> describe,
        EventuallyOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(condition);
        ArgumentNullException.ThrowIfNull(observe);
        ArgumentNullException.ThrowIfNull(isSatisfied);
        ArgumentNullException.ThrowIfNull(describe);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollInterval, TimeSpan.Zero);
    }

    internal static async ValueTask<TObservation> RunAsync<TObservation>(
        Func<CancellationToken, ValueTask<TObservation>> observe,
        EventuallyOptions options,
        Adjudicate<TObservation> adjudicate,
        WindowClosed<TObservation> onWindowClosed,
        ObservationGrace<TObservation>? grace,
        CancellationToken cancellationToken,
        TimeProvider? timeProvider)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var startedAt = clock.GetTimestamp();
        using var windowSource = new CancellationTokenSource(options.Timeout, clock);
        using var windowLinkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            windowSource.Token);

        var attempts = 0;
        TObservation lastObservation = default!;

        try
        {
            while (true)
            {
                // Which token the observation itself is handed is a policy decision, not a detail. Without a
                // grace policy it gets the window token, so a well-behaved observation unwinds at the source
                // the moment the window closes. With one it gets a token tied to the caller only: the window
                // closing must not abort an observation whose result is still going to be adjudicated.
                using var observationSource = grace is null
                    ? null
                    : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                var observed = await ObserveAsync(
                        observe,
                        observationSource?.Token ?? windowLinkedSource.Token,
                        windowLinkedSource.Token)
                    .ConfigureAwait(false);

                if (!observed.Completed)
                {
                    var pending = observed.Pending!;
                    if (grace is null || cancellationToken.IsCancellationRequested)
                    {
                        ConsumeLateFault(pending);
                        cancellationToken.ThrowIfCancellationRequested();
                        return onWindowClosed(attempts, clock.GetElapsedTime(startedAt), lastObservation);
                    }

                    lastObservation = await AdjudicateWithinGraceAsync(
                            pending,
                            observationSource!,
                            grace,
                            attempts,
                            startedAt,
                            clock,
                            cancellationToken)
                        .ConfigureAwait(false);
                    attempts++;

                    return adjudicate(lastObservation, attempts, clock.GetElapsedTime(startedAt))
                        ? lastObservation
                        : onWindowClosed(attempts, clock.GetElapsedTime(startedAt), lastObservation);
                }

                attempts++;
                lastObservation = observed.Value;

                if (adjudicate(lastObservation, attempts, clock.GetElapsedTime(startedAt)))
                {
                    return lastObservation;
                }

                await Task.Delay(options.PollInterval, clock, windowLinkedSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (windowSource.IsCancellationRequested)
        {
            return onWindowClosed(attempts, clock.GetElapsedTime(startedAt), lastObservation);
        }
    }

    /// <summary>
    /// Runs one observation and reports either its value or the fact that the window closed first, leaving
    /// the observation running.
    /// </summary>
    /// <remarks>
    /// The <c>WaitAsync</c> is the structural backstop for observations that cannot honour
    /// <paramref name="observationToken"/>: StackExchange.Redis exposes no
    /// <see cref="CancellationToken"/> overloads at all, and a lambda that simply discards the parameter is an
    /// easy regression to write. Without it, one stuck observation holds the window open forever and the test
    /// <em>parks</em> instead of failing. The <c>!pending.IsCompleted</c> guard keeps this branch to its one
    /// meaning: an observation that unwound at the source is a completed (cancelled) task and its
    /// <see cref="OperationCanceledException"/> must keep propagating to the caller's filters.
    /// </remarks>
    private static async ValueTask<WindowObservation<TObservation>> ObserveAsync<TObservation>(
        Func<CancellationToken, ValueTask<TObservation>> observe,
        CancellationToken observationToken,
        CancellationToken windowToken)
    {
        var observation = observe(observationToken);
        if (observation.IsCompleted)
        {
            return WindowObservation<TObservation>.FromValue(observation.Result);
        }

        var pending = observation.AsTask();
        try
        {
            return WindowObservation<TObservation>.FromValue(
                await pending.WaitAsync(windowToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (windowToken.IsCancellationRequested && !pending.IsCompleted)
        {
            return WindowObservation<TObservation>.FromPending(pending);
        }
    }

    private static async ValueTask<TObservation> AdjudicateWithinGraceAsync<TObservation>(
        Task<TObservation> pending,
        CancellationTokenSource observationSource,
        ObservationGrace<TObservation> grace,
        int completedObservations,
        long startedAt,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        using var graceSource = new CancellationTokenSource(grace.Budget, clock);
        using var graceLinkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            graceSource.Token);

        try
        {
            return await pending.WaitAsync(graceLinkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            graceSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            ConsumeLateFault(pending);
            await observationSource.CancelAsync().ConfigureAwait(false);
            return grace.OnExpired(completedObservations, clock.GetElapsedTime(startedAt));
        }
    }

    /// <summary>
    /// The abandoned task is never awaited, so its eventual fault is observed here rather than resurfacing as
    /// an <c>UnobservedTaskException</c> against some unrelated test on the next finalization pass.
    /// </summary>
    private static void ConsumeLateFault<TObservation>(Task<TObservation> abandoned) =>
        _ = abandoned.ContinueWith(
            static observed => _ = observed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private readonly struct WindowObservation<TObservation>
    {
        private WindowObservation(bool completed, TObservation value, Task<TObservation>? pending)
        {
            Completed = completed;
            Value = value;
            Pending = pending;
        }

        /// <summary>False when the window closed while the observation was still running.</summary>
        public bool Completed { get; }

        public TObservation Value { get; }

        /// <summary>The still-running observation; non-null exactly when <see cref="Completed"/> is false.</summary>
        public Task<TObservation>? Pending { get; }

        public static WindowObservation<TObservation> FromValue(TObservation value) => new(true, value, null);

        public static WindowObservation<TObservation> FromPending(Task<TObservation> pending) =>
            new(false, default!, pending);
    }
}
