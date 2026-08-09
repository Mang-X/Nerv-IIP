namespace Nerv.IIP.ConnectorHost.TestUtilities;

/// <summary>
/// Bounded waits for asynchronously produced observations. Every await in a test that depends on a
/// background loop goes through here, so a lost fake-clock tick, a collector that never resumes, or
/// a child process that never reaches EOF surfaces as a reported failure instead of parking the
/// test — and therefore the whole test host — forever (the MAN-799 failure mode).
///
/// Every failure reports the four facts the repository's determinism rules require: the redacted
/// condition, the elapsed time, the attempt count, and the last observation.
/// </summary>
internal static class BoundedObservation
{
    /// <summary>Default budget for a single edge-triggered observation.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Awaits an edge-triggered completion signal under a bound. The observation is a signal rather
    /// than a poll, so a single bounded await is the whole attempt budget; that is reported
    /// explicitly instead of being left implicit.
    /// </summary>
    public static async Task ObserveAsync(
        Task observation,
        string condition,
        Func<string> lastObservation,
        TimeSpan? budget = null)
    {
        var effectiveBudget = budget ?? DefaultBudget;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await observation.WaitAsync(effectiveBudget);
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException(
                $"Timed out waiting for {condition} after {elapsed.Elapsed.TotalSeconds:0.###}s "
                + $"(budget {effectiveBudget.TotalSeconds:0.###}s, attempts 1/1 — single bounded "
                + $"await on a completion signal); last observation: {lastObservation()}");
        }
    }

    /// <summary>
    /// Awaits an edge-triggered completion signal that returns a value under the same diagnostic
    /// contract as <see cref="ObserveAsync(Task,string,Func{string},TimeSpan?)"/>.
    /// </summary>
    public static async Task<T> ObserveAsync<T>(
        Task<T> observation,
        string condition,
        Func<string> lastObservation,
        TimeSpan? budget = null)
    {
        var effectiveBudget = budget ?? DefaultBudget;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await observation.WaitAsync(effectiveBudget);
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException(
                $"Timed out waiting for {condition} after {elapsed.Elapsed.TotalSeconds:0.###}s "
                + $"(budget {effectiveBudget.TotalSeconds:0.###}s, attempts 1/1 — single bounded "
                + $"await on a completion signal); last observation: {lastObservation()}");
        }
    }

    /// <summary>
    /// Polls <paramref name="condition"/> under a bound. Used only where no completion signal
    /// exists (an out-of-process Host reporting over HTTP), and it reports its real attempt count
    /// so the poll interval is never mistaken for a fixed sleep-before-assert.
    /// </summary>
    public static async Task PollAsync(
        Func<bool> condition,
        string description,
        Func<string> lastObservation,
        TimeSpan budget,
        TimeSpan? interval = null)
    {
        var effectiveInterval = interval ?? TimeSpan.FromMilliseconds(50);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var attempts = 0;
        while (true)
        {
            attempts++;
            if (condition())
            {
                return;
            }

            if (elapsed.Elapsed >= budget)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Timed out waiting for {description} after {elapsed.Elapsed.TotalSeconds:0.###}s "
                    + $"(budget {budget.TotalSeconds:0.###}s, attempts {attempts}, poll interval "
                    + $"{effectiveInterval.TotalMilliseconds:0}ms); last observation: {lastObservation()}");
            }

            await Task.Delay(effectiveInterval);
        }
    }
}
