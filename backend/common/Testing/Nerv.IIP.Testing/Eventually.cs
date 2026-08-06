namespace Nerv.IIP.Testing;

public sealed record EventuallyOptions(
    TimeSpan Timeout,
    TimeSpan PollInterval,
    IReadOnlyCollection<string?> SensitiveValues);

public sealed class EventuallyTimeoutException : TimeoutException
{
    public EventuallyTimeoutException(
        string condition,
        int attempts,
        TimeSpan elapsed,
        string lastObservation)
        : base(
            $"Condition '{condition}' was not satisfied after {elapsed} " +
            $"({attempts} observations). Last observation: {lastObservation}")
    {
        Condition = condition;
        Attempts = attempts;
        Elapsed = elapsed;
        LastObservation = lastObservation;
    }

    public string Condition { get; }

    public int Attempts { get; }

    public TimeSpan Elapsed { get; }

    public string LastObservation { get; }
}

public static class Eventually
{
    /// <summary>
    /// Retries an xUnit assertion block until it passes or the budget expires. This is the shared form of the
    /// "assert-until-it-holds" loop that Redis/RabbitMQ/CAP acceptance tests need: the observable fact is the
    /// assertion block itself, and the only useful diagnostic is the assertion failure last seen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only assertion-shaped failures are retried: <c>Xunit.Sdk.XunitException</c> (every <c>Assert.*</c>
    /// failure) and an <see cref="InvalidOperationException"/> <em>of exactly that type</em> (EF Core's
    /// <c>SingleAsync</c> on a row that has not been projected yet throws exactly that). Anything else — a
    /// broken connection string, a disposed context, a real bug in the query — is rethrown immediately
    /// instead of being retried for the whole budget and then reported as a timeout.
    /// </para>
    /// <para>
    /// The exact-type test is the whole point, not a stylistic detail. The interesting "this will never
    /// become true, stop waiting" failures are <em>subclasses</em> of <see cref="InvalidOperationException"/>:
    /// <see cref="ObjectDisposedException"/> (the scope/context the assertion closes over is gone) and
    /// Npgsql's <c>NpgsqlOperationInProgressException</c> (the connection is being used concurrently). An
    /// <c>is InvalidOperationException</c> test would swallow both and retry them for the whole budget before
    /// reporting a timeout — exactly the behaviour this contract says it avoids.
    /// <c>EventuallyAssertTests</c> pins both directions down.
    /// </para>
    /// <para>
    /// On timeout the sanitized type and message of the last assertion failure are reported through
    /// <see cref="EventuallyTimeoutException"/> alongside the attempt count and elapsed time.
    /// </para>
    /// </remarks>
    public static async ValueTask AssertAsync(
        string condition,
        Func<CancellationToken, Task> assertion,
        EventuallyOptions options,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(assertion);

        await WaitAsync<Exception?>(
            condition: condition,
            observe: async token =>
            {
                try
                {
                    await assertion(token).ConfigureAwait(false);
                    return null;
                }
                catch (Exception exception) when (IsAssertionShaped(exception))
                {
                    return exception;
                }
            },
            isSatisfied: static failure => failure is null,
            describe: static failure => failure is null
                ? "assertion holds"
                : $"assertion still failing: {failure.GetType().Name}: {failure.Message}",
            options: options,
            cancellationToken: cancellationToken,
            timeProvider: timeProvider).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one observation under the bounded window's own token and abandons it the moment the window
    /// closes.
    /// </summary>
    /// <remarks>
    /// The token is handed to <paramref name="observe"/> so a well-behaved observation unwinds at the
    /// source. This <c>WaitAsync</c> is the structural backstop for the observations that cannot:
    /// StackExchange.Redis exposes no <see cref="CancellationToken"/> overloads at all, and a lambda that
    /// simply discards the parameter is an easy regression to write. Without the backstop, one stuck
    /// observation holds the window open forever and the test <em>parks</em> instead of failing — and a
    /// <c>Consistently</c> window silently degrades into a single observation. With it, the worst case is a
    /// reported timeout. The abandoned task is not awaited, so its eventual fault is observed explicitly
    /// rather than resurfacing as an <c>UnobservedTaskException</c>.
    /// </remarks>
    internal static async ValueTask<TObservation> ObserveWithinWindowAsync<TObservation>(
        Func<CancellationToken, ValueTask<TObservation>> observe,
        CancellationToken windowToken)
    {
        var observation = observe(windowToken);
        if (observation.IsCompleted)
        {
            return observation.Result;
        }

        var pending = observation.AsTask();
        try
        {
            return await pending.WaitAsync(windowToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _ = pending.ContinueWith(
                static abandoned => _ = abandoned.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    /// <summary>
    /// True for the two failure shapes that mean "not projected yet, look again": an xUnit assertion
    /// failure, or an <see cref="InvalidOperationException"/> whose runtime type is exactly that. Derived
    /// types (<see cref="ObjectDisposedException"/>, <c>NpgsqlOperationInProgressException</c>, …) are
    /// deliberately excluded — see the remarks on <see cref="AssertAsync"/>.
    /// </summary>
    internal static bool IsAssertionShaped(Exception exception) =>
        exception is Xunit.Sdk.XunitException || exception.GetType() == typeof(InvalidOperationException);

    public static async ValueTask<TObservation> WaitAsync<TObservation>(
        string condition,
        Func<CancellationToken, ValueTask<TObservation>> observe,
        Predicate<TObservation> isSatisfied,
        Func<TObservation, string> describe,
        EventuallyOptions options,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(condition);
        ArgumentNullException.ThrowIfNull(observe);
        ArgumentNullException.ThrowIfNull(isSatisfied);
        ArgumentNullException.ThrowIfNull(describe);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollInterval, TimeSpan.Zero);

        var effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        var startedAt = effectiveTimeProvider.GetTimestamp();
        using var timeoutSource = new CancellationTokenSource(options.Timeout, effectiveTimeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        var attempts = 0;
        var lastObservation = "none";

        try
        {
            while (true)
            {
                var observation = await ObserveWithinWindowAsync(observe, linkedSource.Token)
                    .ConfigureAwait(false);
                attempts++;
                lastObservation = TestDiagnostic.Sanitize(
                    describe(observation),
                    options.SensitiveValues);

                if (isSatisfied(observation))
                {
                    return observation;
                }

                await Task.Delay(
                    options.PollInterval,
                    effectiveTimeProvider,
                    linkedSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            throw new EventuallyTimeoutException(
                TestDiagnostic.Sanitize(condition, options.SensitiveValues),
                attempts,
                effectiveTimeProvider.GetElapsedTime(startedAt),
                lastObservation);
        }
    }
}
