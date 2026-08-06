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
    /// Only assertion-shaped failures are retried: <c>Xunit.Sdk.XunitException</c> (every <c>Assert.*</c>
    /// failure) and <see cref="InvalidOperationException"/> (EF Core's <c>SingleAsync</c> on a row that has
    /// not been projected yet). Anything else — a broken connection string, a disposed context, a real bug in
    /// the query — is rethrown immediately instead of being retried for the whole budget and then reported as
    /// a timeout. On timeout the sanitized type and message of the last assertion failure are reported through
    /// <see cref="EventuallyTimeoutException"/> alongside the attempt count and elapsed time.
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
                catch (Exception exception) when (exception is Xunit.Sdk.XunitException or InvalidOperationException)
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
                var observation = await observe(linkedSource.Token).ConfigureAwait(false);
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
