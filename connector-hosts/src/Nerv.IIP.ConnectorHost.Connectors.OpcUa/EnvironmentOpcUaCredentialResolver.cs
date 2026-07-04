namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa;

public sealed class EnvironmentOpcUaCredentialResolver : IOpcUaCredentialResolver
{
    public ValueTask<OpcUaUserCredential?> ResolveAsync(string? credentialReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credentialReference))
        {
            return ValueTask.FromResult<OpcUaUserCredential?>(null);
        }

        if (!credentialReference.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported OPC UA credential reference scheme: {credentialReference}");
        }

        var prefix = credentialReference["env:".Length..].Trim();
        if (prefix.Length == 0)
        {
            throw new InvalidOperationException("OPC UA environment credential reference must include an environment variable prefix.");
        }

        var userName = Environment.GetEnvironmentVariable($"{prefix}_USERNAME");
        var password = Environment.GetEnvironmentVariable($"{prefix}_PASSWORD");
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException($"OPC UA credential reference '{credentialReference}' did not resolve both username and password.");
        }

        return ValueTask.FromResult<OpcUaUserCredential?>(new OpcUaUserCredential(userName, password));
    }
}
