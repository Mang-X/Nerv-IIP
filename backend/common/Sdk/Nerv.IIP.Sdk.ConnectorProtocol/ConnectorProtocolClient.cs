using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Sdk.Auth;

namespace Nerv.IIP.Sdk.ConnectorProtocol;

public interface IConnectorProtocolClient
{
    /// <summary>
    /// Registers an application instance and caches the returned ingestion token in this client instance.
    /// </summary>
    Task<ApplicationRegistrationResult> SendRegistrationAsync(ApplicationRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a heartbeat using the token cached by a prior successful registration for the same instance in this client instance.
    /// </summary>
    Task SendHeartbeatAsync(ApplicationHeartbeat heartbeat, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a state snapshot using the token cached by a prior successful registration for the same instance in this client instance.
    /// </summary>
    Task SendStateSnapshotAsync(InstanceStateSnapshot snapshot, CancellationToken cancellationToken = default);
}

public sealed class HttpConnectorProtocolClient(HttpClient httpClient, ConnectorHostCredential credential) : IConnectorProtocolClient
{
    private readonly ConcurrentDictionary<string, string> _ingestionTokens = new(StringComparer.Ordinal);

    public async Task<ApplicationRegistrationResult> SendRegistrationAsync(ApplicationRegistration registration, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/connectors/v1/registrations") { Content = JsonContent.Create(registration) };
        ConnectorHostAuthentication.Apply(request, credential);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ApplicationRegistrationResult>>(cancellationToken);
        if (envelope?.Data is null || string.IsNullOrWhiteSpace(envelope.Data.IngestionToken))
        {
            throw new HttpRequestException("Connector registration response did not include an ingestion token.");
        }

        _ingestionTokens[TokenKey(registration.Context, envelope.Data.InstanceKey)] = envelope.Data.IngestionToken;
        return envelope.Data;
    }

    public Task SendHeartbeatAsync(ApplicationHeartbeat heartbeat, CancellationToken cancellationToken = default)
        => PostIngestionAsync("/api/connectors/v1/heartbeats", heartbeat, heartbeat.Context, heartbeat.InstanceKey, cancellationToken);

    public Task SendStateSnapshotAsync(InstanceStateSnapshot snapshot, CancellationToken cancellationToken = default)
        => PostIngestionAsync("/api/connectors/v1/state-snapshots", snapshot, snapshot.Context, snapshot.InstanceKey, cancellationToken);

    private async Task PostIngestionAsync<T>(
        string path,
        T payload,
        ConnectorRequestContext context,
        string instanceKey,
        CancellationToken cancellationToken)
    {
        if (!_ingestionTokens.TryGetValue(TokenKey(context, instanceKey), out var ingestionToken))
        {
            throw new InvalidOperationException($"Connector instance '{instanceKey}' must be registered before ingestion.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Connector-Ingestion-Token", ingestionToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static string TokenKey(ConnectorRequestContext context, string instanceKey)
    {
        return string.Join('\u001f', context.OrganizationId, context.EnvironmentId, context.ConnectorHostId, instanceKey);
    }

    private sealed record ResponseDataEnvelope<T>(
        [property: JsonPropertyName("data")] T? Data,
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("code")] int Code);
}
