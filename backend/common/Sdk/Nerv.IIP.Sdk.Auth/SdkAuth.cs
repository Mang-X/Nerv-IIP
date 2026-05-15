using System.Net.Http.Headers;
using Nerv.IIP.Sdk.Core;

namespace Nerv.IIP.Sdk.Auth;

public sealed record ConnectorHostCredential(string ConnectorHostId, string Secret, string OrganizationId, string EnvironmentId);

public static class ConnectorHostAuthentication
{
    public static void Apply(HttpRequestMessage request, ConnectorHostCredential credential)
    {
        request.Headers.Add("X-Connector-Host-Id", credential.ConnectorHostId);
        request.Headers.Add("X-Connector-Secret", credential.Secret);
        request.Headers.Add("X-Organization-Id", credential.OrganizationId);
        request.Headers.Add("X-Environment-Id", credential.EnvironmentId);
        request.Headers.Authorization = new AuthenticationHeaderValue("ConnectorHost", credential.ConnectorHostId);
    }

    public static PlatformApiResult<ConnectorHostCredential> Validate(ConnectorHostCredential credential)
    {
        return string.IsNullOrWhiteSpace(credential.Secret)
            ? PlatformApiResult<ConnectorHostCredential>.Failure("connector-secret-required", "Connector Host secret is required.")
            : PlatformApiResult<ConnectorHostCredential>.Success(credential);
    }
}
