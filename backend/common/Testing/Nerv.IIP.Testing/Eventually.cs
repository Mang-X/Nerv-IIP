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
