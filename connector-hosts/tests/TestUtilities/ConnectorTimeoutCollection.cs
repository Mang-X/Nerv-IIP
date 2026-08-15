namespace Nerv.IIP.ConnectorHost.TestUtilities;

/// <summary>
/// The non-parallel collection for connector tests whose final backstop is xUnit's per-test
/// timeout. xUnit v2 only honours <c>Fact.Timeout</c> for tests that do not run in parallel, so
/// membership and the timeout must be applied together.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConnectorTimeoutCollection
{
    public const string Name = "Connector bounded-wait tests";

    /// <summary>
    /// Deliberately wider than the connector-specific observation budgets. The detailed bounded
    /// observation should fail first; this catches a regression that parks somewhere unexpected.
    /// </summary>
    public const int TestTimeoutMilliseconds = 120_000;
}
