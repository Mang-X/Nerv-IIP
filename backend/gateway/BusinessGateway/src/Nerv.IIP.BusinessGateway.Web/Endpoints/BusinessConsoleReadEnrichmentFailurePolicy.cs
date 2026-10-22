using System.Net;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints;

/// <summary>
/// Defines downstream HTTP failures that optional Business Console read enrichment may degrade.
/// Authorization failures remain visible because they indicate an internal service identity or permission incident.
/// </summary>
internal static class BusinessConsoleReadEnrichmentFailurePolicy
{
    public static bool CanDegrade(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.NotFound or HttpStatusCode.RequestTimeout
        || (int)statusCode >= 500;
}
