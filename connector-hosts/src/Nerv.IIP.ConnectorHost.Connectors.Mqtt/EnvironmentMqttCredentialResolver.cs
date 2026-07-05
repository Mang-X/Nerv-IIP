namespace Nerv.IIP.ConnectorHost.Connectors.Mqtt;

public sealed class EnvironmentMqttCredentialResolver : IMqttCredentialResolver
{
    public ValueTask<MqttCredential?> ResolveAsync(string? credentialReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentialReference))
        {
            return ValueTask.FromResult<MqttCredential?>(null);
        }

        const string Prefix = "env:";
        if (!credentialReference.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MQTT credential references must use the env:<PREFIX> format.");
        }

        var variablePrefix = credentialReference[Prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(variablePrefix))
        {
            throw new InvalidOperationException("MQTT credential environment prefix cannot be empty.");
        }

        var userName = Environment.GetEnvironmentVariable($"{variablePrefix}_USERNAME");
        var password = Environment.GetEnvironmentVariable($"{variablePrefix}_PASSWORD");
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException($"MQTT credential reference '{credentialReference}' could not be resolved from environment variables.");
        }

        return ValueTask.FromResult<MqttCredential?>(new MqttCredential(userName, password));
    }
}
