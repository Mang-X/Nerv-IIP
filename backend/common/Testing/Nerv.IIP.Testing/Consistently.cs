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
/// Raised when the observation that was in flight as the stability window closed did not finish within the
/// additional grace budget, so its verdict is unknown.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately <em>not</em> a <see cref="ConsistentlyViolatedException"/>, and just as deliberately
/// not a pass. Nothing about the asserted invariant was learned from that observation: it simply never
/// finished (a cold Docker PostgreSQL query on a loaded CI runner is the canonical case). Reporting it as a
/// violation turns "the infrastructure was slower than the window" into "the negative assertion failed";
/// reporting it as a pass throws away the one observation most likely to have exposed a late violation and
/// silently lowers the sensitivity of the assertion. It is a <see cref="TimeoutException"/> so it lines up
/// with <see cref="EventuallyTimeoutException"/> and <see cref="TestTimeoutException"/>.
/// </para>
/// <para>
/// <see cref="CompletedObservations"/> is a real variable, not a constant dressed up as a diagnostic: it is
/// zero when the window closed before the very first observation returned, and the number of clean
/// observations already banked when a later one overran. The two cases read very differently — "nothing was
/// ever observed" versus "it held N times and then the tail went unread" — and the message says which.
/// </para>
/// </remarks>
public sealed class ConsistentlyObservationTimeoutException : TimeoutException
{
    public ConsistentlyObservationTimeoutException(
        string condition,
        int completedObservations,
        TimeSpan elapsed,
        TimeSpan grace)
        : base(Describe(condition, completedObservations, elapsed, grace))
    {
        Condition = condition;
        CompletedObservations = completedObservations;
        Elapsed = elapsed;
        Grace = grace;
    }

    public string Condition { get; }

    /// <summary>Observations that completed inside the window before the one that overran.</summary>
    public int CompletedObservations { get; }

    /// <summary>Total time from the start of the window to the moment the grace budget expired.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>The grace budget the unfinished observation was given after the window closed.</summary>
    public TimeSpan Grace { get; }

    private static string Describe(
        string condition,
        int completedObservations,
        TimeSpan elapsed,
        TimeSpan grace) =>
        completedObservations == 0
            ? $"Condition '{condition}' was never observed: the stability window closed and the first "
                + $"observation had still not completed {grace} later (total {elapsed}). This is a timeout, "
                + "not a violation of the invariant — widen the window or make the observation cheaper."
            : $"Condition '{condition}' held across {completedObservations} completed observation(s), but the "
                + $"observation still running when the stability window closed had not completed {grace} "
                + $"later (total {elapsed}). The verdict is unknown, not a pass — widen the window or make "
                + "the observation cheaper.";
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
    /// <paramref name="isSatisfied"/>. Returns the last observation when the window elapsed without a
    /// violation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An observation that <em>started</em> inside the window is evidence about the window, whenever it
    /// happens to finish, so the window closing does not cancel it: it is awaited for a further
    /// <paramref name="observationGrace"/> and then adjudicated like any other observation. Returning an
    /// earlier, cleaner observation instead would mean the single observation most likely to expose a
    /// late violation is the one discarded — the negative assertion would quietly lose sensitivity exactly
    /// where it matters. When the grace budget expires too, the verdict is unknown and
    /// <see cref="ConsistentlyObservationTimeoutException"/> says so rather than passing.
    /// </para>
    /// <para>
    /// <paramref name="observationGrace"/> defaults to <c>options.Timeout</c>: a budget of the same order as
    /// the window the caller already decided was a reasonable amount of wall clock to spend, and independent
    /// of it. Because the observation is not handed the window token, an <c>observe</c> that honours
    /// cancellation unwinds only on caller cancellation or on grace expiry.
    /// </para>
    /// </remarks>
    public static async ValueTask<TObservation> StaysAsync<TObservation>(
        string condition,
        Func<CancellationToken, ValueTask<TObservation>> observe,
        Predicate<TObservation> isSatisfied,
        Func<TObservation, string> describe,
        EventuallyOptions options,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null,
        TimeSpan? observationGrace = null)
    {
        BoundedObservationWindow.ValidateArguments(condition, observe, isSatisfied, describe, options);
        var grace = observationGrace ?? options.Timeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(grace, TimeSpan.Zero);

        return await BoundedObservationWindow.RunAsync(
            observe,
            options,
            adjudicate: (observation, attempts, elapsed) => !isSatisfied(observation)
                ? throw new ConsistentlyViolatedException(
                    TestDiagnostic.Sanitize(condition, options.SensitiveValues),
                    attempts,
                    elapsed,
                    TestDiagnostic.Sanitize(describe(observation), options.SensitiveValues))
                // A holding observation never ends the window early: the whole point is to keep looking.
                : false,
            onWindowClosed: (_, _, lastObservation) => lastObservation,
            grace: new BoundedObservationWindow.ObservationGrace<TObservation>(
                grace,
                (completedObservations, elapsed) => throw new ConsistentlyObservationTimeoutException(
                    TestDiagnostic.Sanitize(condition, options.SensitiveValues),
                    completedObservations,
                    elapsed,
                    grace)),
            cancellationToken,
            timeProvider).ConfigureAwait(false);
    }
}
