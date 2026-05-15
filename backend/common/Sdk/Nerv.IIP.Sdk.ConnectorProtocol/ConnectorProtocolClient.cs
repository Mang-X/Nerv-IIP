using System.Net.Http.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Sdk.Auth;

namespace Nerv.IIP.Sdk.ConnectorProtocol;

public interface IConnectorProtocolClient
{
    Task SendRegistrationAsync(ApplicationRegistration registration, CancellationToken cancellationToken = default);
    Task SendHeartbeatAsync(ApplicationHeartbeat heartbeat, CancellationToken cancellationToken = default);
    Task SendStateSnapshotAsync(InstanceStateSnapshot snapshot, CancellationToken cancellationToken = default);
}

public sealed class HttpConnectorProtocolClient(HttpClient httpClient, ConnectorHostCredential credential) : IConnectorProtocolClient
{
    public Task SendRegistrationAsync(ApplicationRegistration registration, CancellationToken cancellationToken = default)
        => PostAsync("/api/connectors/v1/registrations", registration, cancellationToken);

    public Task SendHeartbeatAsync(ApplicationHeartbeat heartbeat, CancellationToken cancellationToken = default)
        => PostAsync("/api/connectors/v1/heartbeats", heartbeat, cancellationToken);

    public Task SendStateSnapshotAsync(InstanceStateSnapshot snapshot, CancellationToken cancellationToken = default)
        => PostAsync("/api/connectors/v1/state-snapshots", snapshot, cancellationToken);

    private async Task PostAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        ConnectorHostAuthentication.Apply(request, credential);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
