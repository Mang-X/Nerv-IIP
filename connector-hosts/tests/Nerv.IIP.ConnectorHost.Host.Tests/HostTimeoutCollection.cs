namespace Nerv.IIP.ConnectorHost.Host.Tests;

/// <summary>
/// The single non-parallel collection for every test in this assembly whose last-resort backstop is
/// <c>Fact.Timeout</c>. xUnit v2 only honours <c>Timeout</c> for tests that do not run in parallel,
/// so a <c>Timeout</c> on a class outside a <c>DisableParallelization</c> collection is decorative —
/// and MAN-799 is exactly the failure mode (a parked loop hanging the whole test host) that those
/// timeouts exist to convert into a reported failure.
///
/// Membership is therefore not optional for: fake-clock scheduling tests, tests that own a child
/// process or a listening socket, and anything else that awaits a background loop. Pure in-process
/// unit tests (for example the sampling-policy parser) stay outside and keep running in parallel.
///
/// Membership is per class, not per test: xUnit rejects <c>Timeout</c> on a synchronous test, so a
/// member class may hold synchronous tests that carry no <c>Timeout</c> — they have no await to
/// park on. What membership guarantees is that every <c>Timeout</c> declared inside this collection
/// is real.
/// </summary>
[CollectionDefinition(HostTimeoutCollection.Name, DisableParallelization = true)]
public sealed class HostTimeoutCollection
{
    public const string Name = "Connector host bounded-wait tests";

    /// <summary>
    /// Per-test upper bound shared by every member of the collection. Deliberately far above each
    /// test's own observation/stop budgets so a genuine assertion failure always wins the race and
    /// reports its condition; this only exists so a regression that parks a loop fails the test
    /// instead of hanging the test host.
    /// </summary>
    public const int TestTimeoutMilliseconds = 120_000;
}
