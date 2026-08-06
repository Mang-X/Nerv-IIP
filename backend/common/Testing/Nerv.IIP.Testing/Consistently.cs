namespace Nerv.IIP.Testing;

/// <summary>
/// Raised when a bounded stability window observed a state that violated the asserted invariant.
/// </summary>
public sealed class ConsistentlyViolatedException : Exception
{
    public ConsistentlyViolatedException(
        string condition,
        int attempts,
        TimeSpan elapsed,
        string violatingObservation)
        : base(
            $"Condition '{condition}' stopped holding after {elapsed} " +
            $"({attempts} observations). Violating observation: {violatingObservation}")
    {
        Condition = condition;
        Attempts = attempts;
        Elapsed = elapsed;
        ViolatingObservation = violatingObservation;
    }

    public string Condition { get; }

    public int Attempts { get; }

    public TimeSpan Elapsed { get; }

    public string ViolatingObservation { get; }
}

/// <summary>
/// Bounded stability assertion: the counterpart of <see cref="Eventually"/> for negative assertions.
/// </summary>
/// <remarks>
/// <para>
/// A negative assertion ("no second command is dispatched", "no fourth delivery attempt happens") cannot be
/// settled by a single sleep followed by one assertion: that form passes whenever the window happened to be
/// shorter than the latency of the event it is supposed to exclude, and it reports nothing when it fails.
/// <see cref="StaysAsync"/> polls the same observable fact across the whole window and fails on the
/// <em>first</em> violating observation, reporting the sanitized observation, the attempt count and the
/// elapsed time, exactly like <see cref="Eventually"/> does for the positive direction.
/// </para>
/// <para>
/// The window is still wall-clock time and therefore still an admission that the excluded event has no
/// observable edge. Prefer removing the window entirely when the code under test can expose one (an injected
/// <see cref="TimeProvider"/> that is never advanced makes a timer-driven event structurally impossible, which
/// is strictly stronger than any window length).
/// </para>
/// </remarks>
public static class Consistently
{
    /// <summary>
    /// Observes <paramref name="observe"/> repeatedly for the whole of <c>options.Timeout</c> and throws a
    /// <see cref="ConsistentlyViolatedException"/> as soon as an observation does not satisfy
    /// <paramref name="isSatisfied"/>. Returns the last observation when the window elapsed without a violation.
    /// </summary>
    public static async ValueTask<TObservation> StaysAsync<TObservation>(
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
        using var windowSource = new CancellationTokenSource(options.Timeout, effectiveTimeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            windowSource.Token);

        var attempts = 0;
        TObservation lastObservation = default!;
        var observedAtLeastOnce = false;

        try
        {
            while (true)
            {
                var observation = await observe(linkedSource.Token).ConfigureAwait(false);
                attempts++;
                lastObservation = observation;
                observedAtLeastOnce = true;

                if (!isSatisfied(observation))
                {
                    throw new ConsistentlyViolatedException(
                        TestDiagnostic.Sanitize(condition, options.SensitiveValues),
                        attempts,
                        effectiveTimeProvider.GetElapsedTime(startedAt),
                        TestDiagnostic.Sanitize(describe(observation), options.SensitiveValues));
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
        catch (OperationCanceledException) when (windowSource.IsCancellationRequested)
        {
            if (!observedAtLeastOnce)
            {
                throw new ConsistentlyViolatedException(
                    TestDiagnostic.Sanitize(condition, options.SensitiveValues),
                    attempts,
                    effectiveTimeProvider.GetElapsedTime(startedAt),
                    "the stability window elapsed before a single observation was taken");
            }

            return lastObservation;
        }
    }
}
