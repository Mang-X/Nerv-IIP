namespace Nerv.IIP.Testing;

/// <summary>
/// Test-owned barrier for asserting a client-side fan-out limit without a per-request hold time.
/// </summary>
/// <remarks>
/// <para>
/// Every downstream request parks in <see cref="PassAsync"/> until the test calls <see cref="Release"/>, so
/// the test can first wait for the in-flight count to <em>reach</em> the limit (a real edge) and then assert
/// it <em>stays</em> within the limit while all requests are outstanding. That turns the assertion from an
/// upper bound — which a machine slow enough to serialize the requests satisfies vacuously — into an
/// equality.
/// </para>
/// <para>
/// The safety budget in <see cref="PassAsync"/> is never spent on a healthy run: the test releases the gate
/// itself. It exists so that a regression in the throttle fails the test instead of parking it forever. It is
/// linked to the caller's <see cref="CancellationToken"/>, so a cancelled downstream request unwinds
/// immediately rather than sitting out the whole budget.
/// </para>
/// </remarks>
public sealed class ConcurrencyFanOutGate
{
    private static readonly TimeSpan DefaultSafetyBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string operation;

    private int inFlight;
    private int maxInFlight;
    private int totalEntries;

    public ConcurrencyFanOutGate(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        this.operation = operation;
    }

    /// <summary>Requests currently parked at the gate.</summary>
    public int InFlight => Volatile.Read(ref inFlight);

    /// <summary>Highest number of simultaneously parked requests observed so far.</summary>
    public int MaxInFlight => Volatile.Read(ref maxInFlight);

    /// <summary>Total number of requests that ever entered the gate.</summary>
    public int TotalEntries => Volatile.Read(ref totalEntries);

    /// <summary>Lets every parked request complete. Called by the test, never by a timer.</summary>
    public void Release() => gate.TrySetResult();

    /// <summary>
    /// Enters the gate, records the concurrency, and parks until <see cref="Release"/> is called or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task PassAsync(CancellationToken cancellationToken, TimeSpan? safetyBudget = null)
    {
        Interlocked.Increment(ref totalEntries);
        TrackMaximum(Interlocked.Increment(ref inFlight));
        try
        {
            await TestTimeout.RunAsync(
                operation: $"{operation} fan-out gate",
                action: async token => await gate.Task.WaitAsync(token).ConfigureAwait(false),
                timeout: safetyBudget ?? DefaultSafetyBudget,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref inFlight);
        }
    }

    /// <summary>Waits for the in-flight count to reach <paramref name="expected"/>.</summary>
    public async ValueTask WaitForInFlightAsync(
        int expected,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        await Eventually.WaitAsync(
            condition: $"concurrent {operation} requests reach the {expected} request limit",
            // In-memory counter read; nothing here can block, so discarding the window token is not a
            // dropped budget.
            observe: _ => ValueTask.FromResult(InFlight),
            isSatisfied: current => current >= expected,
            describe: Describe,
            options: new EventuallyOptions(timeout, PollInterval, []),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asserts that the observed maximum stays within <paramref name="limit"/> for the whole
    /// <paramref name="window"/>, i.e. while every request is still outstanding.
    /// </summary>
    public async ValueTask StaysWithinAsync(
        int limit,
        TimeSpan window,
        string scope,
        CancellationToken cancellationToken = default)
    {
        await Consistently.StaysAsync(
            condition: $"concurrent {operation} requests never exceed {limit} while {scope}",
            // In-memory counters: the observation cannot block, so there is nothing for the window token to
            // cancel here. The observed maximum is what decides the verdict, so it is also what the
            // diagnostic leads with — re-reading a counter at diagnosis time would report a value other
            // than the one that tripped the assertion.
            observe: _ => ValueTask.FromResult(MaxInFlight),
            isSatisfied: max => max <= limit,
            describe: max => $"maxInFlight={max} (violating); inFlight={InFlight}; totalEntries={TotalEntries}",
            options: new EventuallyOptions(window, PollInterval, []),
            cancellationToken).ConfigureAwait(false);
    }

    private string Describe(int current) =>
        $"inFlight={current}; maxInFlight={MaxInFlight}; totalEntries={TotalEntries}";

    private void TrackMaximum(int current)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref maxInFlight);
            if (current <= snapshot)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref maxInFlight, current, snapshot) == snapshot)
            {
                return;
            }
        }
    }
}
