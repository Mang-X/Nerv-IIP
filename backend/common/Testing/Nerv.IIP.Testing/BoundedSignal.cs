namespace Nerv.IIP.Testing;

/// <summary>
/// Bounded await on an edge-triggered signal, reporting the redacted condition, elapsed time, attempt
/// count and last observation, so a lost fake-clock tick fails with a diagnosis instead of hanging.
/// </summary>
/// <remarks>
/// The budget is genuine wall clock and there is no way around that: what is being waited for is real code on
/// a real thread reaching the line that registers a timer, which is precisely the fact the fake clock cannot
/// model. It is therefore a parameter with a default rather than a constant — a caller on a slow or heavily
/// parallel runner can widen it without editing this library, which is the difference between a budget and a
/// hard-coded wall-clock sleep.
/// </remarks>
public static class BoundedSignal
{
    /// <summary>
    /// Default budget for a signal that a healthy run reaches in microseconds. It is never spent on a
    /// passing test; it exists so a lost edge fails loudly instead of parking the run.
    /// </summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(5);

    public static async Task ObserveAsync(
        Task observation,
        string condition,
        Func<string> lastObservation,
        TimeSpan? budget = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(condition);
        ArgumentNullException.ThrowIfNull(lastObservation);

        var effectiveBudget = budget ?? DefaultBudget;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(effectiveBudget, TimeSpan.Zero);

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await observation.WaitAsync(effectiveBudget).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException(
                $"Timed out waiting for {condition} after {elapsed.Elapsed.TotalSeconds:0.###}s "
                + $"(budget {effectiveBudget.TotalSeconds:0.###}s, attempts 1/1 — single bounded await on a "
                + $"completion signal); last observation: {lastObservation()}");
        }
    }
}
