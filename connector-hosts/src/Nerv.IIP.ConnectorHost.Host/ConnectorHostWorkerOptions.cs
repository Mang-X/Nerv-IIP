namespace Nerv.IIP.ConnectorHost.Host;

public sealed class ConnectorHostWorkerOptions
{
    public const string SectionName = "ConnectorHost";

    public int HeartbeatSeconds { get; init; } = 2;
    public int ConnectionProbeSeconds { get; init; } = 4;
    public int CollectionCycleSeconds { get; init; } = 30;
    public int OperationPollSeconds { get; init; } = 30;
    public int ConnectionDetectionBudgetSeconds { get; init; } = 4;
    public int BackendDeadlineSeconds { get; init; } = 8;

    public void Validate()
    {
        if (HeartbeatSeconds <= 0 || ConnectionProbeSeconds <= 0 || CollectionCycleSeconds <= 0 || OperationPollSeconds <= 0)
        {
            throw new InvalidOperationException("Connector Host worker periods must be greater than zero.");
        }

        if (HeartbeatSeconds != 2)
        {
            throw new InvalidOperationException("Connector Host heartbeat period must be 2 seconds for the governed profile.");
        }

        if (ConnectionProbeSeconds != 4)
        {
            throw new InvalidOperationException("Connector connection probe period must be 4 seconds for the governed profile.");
        }

        if (ConnectionDetectionBudgetSeconds <= 0 || ConnectionDetectionBudgetSeconds > 4)
        {
            throw new InvalidOperationException("Connector connection detection budget must be between 1 and 4 seconds.");
        }

        if (BackendDeadlineSeconds <= 0 || BackendDeadlineSeconds > 8)
        {
            throw new InvalidOperationException("Connector backend deadline must be between 1 and 8 seconds.");
        }
    }
}
