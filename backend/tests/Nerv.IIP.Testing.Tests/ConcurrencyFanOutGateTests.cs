namespace Nerv.IIP.Testing.Tests;

public sealed class ConcurrencyFanOutGateTests
{
    [Fact]
    public async Task PassAsync_parks_every_entrant_until_the_test_releases_the_gate()
    {
        var gate = new ConcurrencyFanOutGate("probe");
        var parked = Enumerable.Range(0, 4)
            .Select(_ => gate.PassAsync(CancellationToken.None))
            .ToArray();

        await gate.WaitForInFlightAsync(4, TimeSpan.FromSeconds(10));
        Assert.All(parked, pending => Assert.False(pending.IsCompleted));

        gate.Release();
        await Task.WhenAll(parked);

        Assert.Equal(0, gate.InFlight);
        Assert.Equal(4, gate.MaxInFlight);
        Assert.Equal(4, gate.TotalEntries);
    }

    /// <summary>
    /// The safety budget must never swallow the caller's cancellation: a downstream request whose token is
    /// cancelled has to unwind immediately rather than sit out the whole (30 s by default) budget. The two
    /// hand-copied fan-out gates this primitive replaced disagreed on exactly this point.
    /// </summary>
    [Fact]
    public async Task PassAsync_propagates_caller_cancellation_instead_of_waiting_out_the_safety_budget()
    {
        var gate = new ConcurrencyFanOutGate("probe");
        using var callerSource = new CancellationTokenSource();
        var pending = gate.PassAsync(callerSource.Token, safetyBudget: TimeSpan.FromMinutes(10));

        await gate.WaitForInFlightAsync(1, TimeSpan.FromSeconds(10));
        await callerSource.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(callerSource.Token, exception.CancellationToken);
        Assert.Equal(0, gate.InFlight);
    }

    [Fact]
    public async Task PassAsync_reports_its_own_budget_as_a_test_timeout()
    {
        var gate = new ConcurrencyFanOutGate("probe");

        var exception = await Assert.ThrowsAsync<TestTimeoutException>(
            () => gate.PassAsync(CancellationToken.None, safetyBudget: TimeSpan.FromMilliseconds(50)));

        Assert.Equal("probe fan-out gate", exception.Operation);
    }

    [Fact]
    public async Task StaysWithinAsync_fails_on_the_first_observation_that_exceeds_the_limit()
    {
        var gate = new ConcurrencyFanOutGate("probe");
        var parked = Enumerable.Range(0, 3)
            .Select(_ => gate.PassAsync(CancellationToken.None))
            .ToArray();
        await gate.WaitForInFlightAsync(3, TimeSpan.FromSeconds(10));

        var violation = await Assert.ThrowsAsync<ConsistentlyViolatedException>(
            async () => await gate.StaysWithinAsync(2, TimeSpan.FromMilliseconds(200), "the probe runs"));

        Assert.Contains("maxInFlight=3", violation.ViolatingObservation, StringComparison.Ordinal);

        gate.Release();
        await Task.WhenAll(parked);
    }
}
