using Microsoft.Extensions.Configuration;

namespace Nerv.IIP.ServiceAuth;

public static class ScopedCallerOutboundToken
{
    public static string Resolve(
        IConfigurationSection? credentials,
        string organizationId,
        string environmentId,
        string legacyBearer)
    {
        if (credentials is null || !credentials.Exists())
        {
            return legacyBearer;
        }

        var scope = credentials.GetChildren().SingleOrDefault(profile =>
            profile["OrganizationId"] == organizationId && profile["EnvironmentId"] == environmentId);
        var bearer = scope?["BearerToken"];
        if (bearer is null || !InternalServiceBearerToken.IsValidToken(bearer) || bearer == legacyBearer)
        {
            throw new InvalidOperationException("A distinct scoped caller credential is required for the requested organization and environment.");
        }

        return bearer;
    }
}
