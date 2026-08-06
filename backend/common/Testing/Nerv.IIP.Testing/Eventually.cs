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
    /// EF Core's marker for "this <c>DbContext</c> is already busy with another operation". EF raises it as a
    /// plain <see cref="InvalidOperationException"/>, so the exact-type test below cannot tell it apart from
    /// "the row is not projected yet" by type alone.
    /// </summary>
    /// <remarks>
    /// The string is EF Core's own <c>CoreStrings.ConcurrentMethodInvocation</c>, which ships English-only
    /// (EF Core has no localized resource satellites), so matching on it is stable across cultures.
    /// <c>EventuallyAssertTests.EfCoreConcurrentContextUse_…</c> pins both the runtime type and this wording
    /// against the real EF Core in use rather than against a memory of it — if a future EF version rewords or
    /// retypes it, that test goes red instead of this guard going quietly ineffective.
    /// </remarks>
    internal const string EfConcurrentContextUseMarker = "A second operation was started on this context";

    /// <summary>
    /// True for the two failure shapes that mean "not projected yet, look again": an xUnit assertion
    /// failure, or an <see cref="InvalidOperationException"/> whose runtime type is exactly that. Derived
    /// types (<see cref="ObjectDisposedException"/>, <c>NpgsqlOperationInProgressException</c>, …) are
    /// deliberately excluded — see the remarks on <see cref="AssertAsync"/>.
    /// </summary>
    /// <remarks>
    /// The one exception carved out of the exact-type test is EF Core's concurrent-context-use error, which
    /// is a plain <see cref="InvalidOperationException"/> and would otherwise be retried for the whole budget
    /// and then reported as a timeout. It is the EF spelling of the same "this can never become true" family
    /// as Npgsql's <c>NpgsqlOperationInProgressException</c>, and it is the shape an observation that outlives
    /// its window produces when it shares a <c>DbContext</c> with its caller — see the resource invariant on
    /// <see cref="BoundedObservationWindow"/>.
    /// </remarks>
    internal static bool IsAssertionShaped(Exception exception) =>
        exception is Xunit.Sdk.XunitException
        || (exception.GetType() == typeof(InvalidOperationException)
            && !exception.Message.Contains(EfConcurrentContextUseMarker, StringComparison.Ordinal));

    /// <summary>
    /// Polls <paramref name="observe"/> until <paramref name="isSatisfied"/> holds or the budget expires.
    /// An observation still running when the budget expires is abandoned: "satisfied, but late" is exactly
    /// what a positive assertion's budget exists to reject.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Invariant: <typeparamref name="TObservation"/> must be a value snapshot.</strong>
    /// <paramref name="describe"/> is deliberately <em>not</em> evaluated after each observation — formatting a
    /// diagnostic on every poll of a 2-minute window is pure waste — so it runs exactly once, on the failure
    /// path, against the observation that was banked as the last one. If that observation is a live handle
    /// rather than a snapshot (a <c>DbContext</c>, an entity still attached to a scope the observation
    /// disposed, a collection some other thread keeps mutating, an open connection), the timeout would report
    /// the state at <em>diagnosis</em> time rather than the state that failed — or <paramref name="describe"/>
    /// would throw on a disposed resource. Return scalars, strings, tuples of scalars, detached records, or a
    /// freshly allocated collection; never a handle whose owner outlives the observation.
    /// </para>
    /// <para>
    /// Closing over live counters as <em>supplementary</em> context is a deliberate exception and reads as one
    /// at the call site: <c>ConcurrencyFanOutGate.StaysWithinAsync</c> leads with the observed value that
    /// decided the verdict and appends the current gate counters behind it. The observation still decides; the
    /// live reads only add colour.
    /// </para>
    /// <para>
    /// A <paramref name="describe"/> that throws anyway cannot destroy the diagnostic: it degrades to a
    /// sanitized placeholder naming the exception, and <see cref="EventuallyTimeoutException"/> is still what
    /// the caller sees — see <c>BoundedObservationWindow.SafeDescribe</c>.
    /// </para>
    /// </remarks>
    public static async ValueTask<TObservation> WaitAsync<TObservation>(
        string condition,
        Func<CancellationToken, ValueTask<TObservation>> observe,
        Predicate<TObservation> isSatisfied,
        Func<TObservation, string> describe,
        EventuallyOptions options,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null)
    {
        BoundedObservationWindow.ValidateArguments(condition, observe, isSatisfied, describe, options);

        return await BoundedObservationWindow.RunAsync(
            observe,
            options,
            adjudicate: (observation, _, _) => isSatisfied(observation),
            onWindowClosed: (attempts, elapsed, lastObservation) => throw new EventuallyTimeoutException(
                TestDiagnostic.Sanitize(condition, options.SensitiveValues),
                attempts,
                elapsed,
                attempts == 0
                    ? "none"
                    : BoundedObservationWindow.SafeDescribe(
                        describe,
                        lastObservation,
                        options.SensitiveValues)),
            grace: null,
            cancellationToken,
            timeProvider).ConfigureAwait(false);
    }
}
