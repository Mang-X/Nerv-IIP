namespace Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.TelemetryConnectorConfigAggregate;

public sealed class TelemetryConnectorConfig
{
    private TelemetryConnectorConfig() { }

    public TelemetryConnectorConfig(string organizationId, string environmentId, string connectorId)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ConnectorId = connectorId;
    }

    public string OrganizationId { get; private set; } = null!;
    public string EnvironmentId { get; private set; } = null!;
    public string ConnectorId { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string Protocol { get; private set; } = null!;
    public string Endpoint { get; private set; } = null!;
    public bool IsEnabled { get; private set; }
    public int PollIntervalSeconds { get; private set; }
    public string MappingsJson { get; private set; } = null!;
    public Guid Revision { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Configure(string displayName, string protocol, string endpoint, bool enabled,
        int pollIntervalSeconds, string mappingsJson, DateTimeOffset now)
    {
        DisplayName = displayName;
        Protocol = protocol;
        Endpoint = endpoint;
        IsEnabled = enabled;
        PollIntervalSeconds = pollIntervalSeconds;
        MappingsJson = mappingsJson;
        Revision = Guid.NewGuid();
        UpdatedAtUtc = now;
    }
}
